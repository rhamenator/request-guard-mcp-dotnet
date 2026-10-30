#!/usr/bin/env python3
"""Running-build MCP envelope and pinned official Python SDK qualification."""
import argparse
import asyncio
from datetime import timedelta
import json
import http.client
import re
import urllib.parse
from pathlib import Path
import socket
import struct
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

from jsonschema import Draft202012Validator
from mcp import ClientSession
from mcp.client.streamable_http import streamablehttp_client
from mcp.shared.exceptions import McpError

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'parity'))
from contract import CORE_CASES, WebSocket, normalize_payload
from run import clean_environment

TOKEN = 'synthetic-protocol-client-token-at-least-32-bytes'
OTLP_SECRET = 'synthetic-otlp-password-never-log'
CARDINALITY_MARKER = 'attacker-cardinality-probe-'
INVALID = [
    ('{}', None, 200), ('[]', None, 200), ('null', None, 200),
    ('{"id":1,"method":"health"}', None, 200),
    ('{"jsonrpc":"1.0","id":1,"method":"health"}', None, 200),
    ('{"jsonrpc":"2.0","id":null,"method":"health"}', None, 200),
    ('{"jsonrpc":"2.0","id":true,"method":"health"}', None, 200),
    ('{"jsonrpc":"2.0","id":1.5,"method":"health"}', None, 200),
    ('{"jsonrpc":"2.0","id":{},"method":"health"}', None, 200),
    ('{"jsonrpc":"2.0","id":1,"method":42}', None, 200),
    ('{"jsonrpc":"2.0","id":1,"method":"health","params":[]}', 1, 200),
    ('{"jsonrpc":"2.0","method":"health","params":null}', None, 400),
    ('{"jsonrpc":"2.0","id":1,"method":"health","result":{}}', None, 200),
    ('{"jsonrpc":"2.0","id":1,"result":{},"error":null}', None, 400),
    ('{"jsonrpc":"2.0","id":1,"error":{"code":1.5,"message":"bad"}}', None, 400),
]


def post(url, text):
    request = urllib.request.Request(url + '/mcp', text.encode(), headers={
        'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream', 'Authorization': f'Bearer {TOKEN}'})
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def check_otlp_sink_failure(url):
    payload = '{"jsonrpc":"2.0","id":1,"method":"health"}'
    for _ in range(20):
        status, body = post(url, payload)
        response = json.loads(body)
        assert status == 200 and response['result']['status'] == 'healthy', response
    # The batch exporter runs out of band. Give it an opportunity to observe the refused
    # connection, then prove later requests still complete normally.
    time.sleep(6)
    status, body = post(url, payload)
    response = json.loads(body)
    assert status == 200 and response['result']['status'] == 'healthy', response
    print('PASS unavailable OTLP sink does not delay or stop MCP responses')


def get_metrics(url):
    with urllib.request.urlopen(url + '/metrics', timeout=5) as response:
        assert response.status == 200, response.status
        return response.read().decode()


def process_resources(pid):
    """Return Linux process resource counters when /proc is available."""
    status_path = Path(f'/proc/{pid}/status')
    if not status_path.is_file():
        return None
    values = {}
    for line in status_path.read_text().splitlines():
        key, separator, value = line.partition(':')
        if separator and key in {'VmRSS', 'VmHWM', 'Threads'}:
            values[key] = int(value.strip().split()[0])
    values['FileDescriptors'] = len(list(Path(f'/proc/{pid}/fd').iterdir()))
    return values


def metric_value(metrics, family, labels):
    expected = dict(labels)
    for match in re.finditer(rf'^{re.escape(family)}\{{([^}}]+)\}} ([0-9]+(?:\.[0-9]+)?)$',
                             metrics, re.MULTILINE):
        actual = dict(re.findall(r'(\w+)="([^"]*)"', match.group(1)))
        if actual == expected:
            return float(match.group(2))
    raise AssertionError((family, expected))


def check_telemetry_endurance(url, seconds, pid):
    """Exercise the running artifact while proving attacker labels stay bounded."""
    assert seconds >= 1
    started = time.monotonic()
    deadline = started + seconds
    request_id = 0
    unknown_count = 0
    health_count = 0
    resource_samples = []
    next_request = started
    while time.monotonic() < deadline:
        request_id += 1
        probe = f'{CARDINALITY_MARKER}{request_id:08d}-synthetic-secret'
        status, body = post(url, json.dumps({
            'jsonrpc': '2.0', 'id': request_id, 'method': probe,
        }))
        response = json.loads(body)
        assert status == 200 and response['error']['code'] == -404, response
        unknown_count += 1

        request_id += 1
        status, body = post(url, json.dumps({
            'jsonrpc': '2.0', 'id': request_id, 'method': 'health',
        }))
        response = json.loads(body)
        assert status == 200 and response['result']['status'] == 'healthy', response
        health_count += 1

        if unknown_count == 1 or unknown_count % 250 == 0:
            interim = get_metrics(url)
            assert CARDINALITY_MARKER not in interim
            assert len(interim) < 64 * 1024, len(interim)
            resources = process_resources(pid)
            if resources:
                resource_samples.append(resources)

        # Hold a predictable 25 mixed request pairs/second instead of turning the
        # hosted runner into an unbounded throughput benchmark.
        next_request += 0.04
        delay = next_request - time.monotonic()
        if delay > 0:
            time.sleep(delay)

    metrics = get_metrics(url)
    assert CARDINALITY_MARKER not in metrics
    tool_labels = set(re.findall(r'tool="([^"]+)"', metrics))
    assert tool_labels == {'__unknown__', 'health'}, tool_labels
    assert metric_value(metrics, 'mcp_requests_total',
                        [('tool', '__unknown__'), ('status', 'error')]) == unknown_count
    assert metric_value(metrics, 'mcp_tool_errors_total',
                        [('tool', '__unknown__'), ('error_code', 'TOOL_NOT_FOUND')]) == unknown_count
    assert metric_value(metrics, 'mcp_request_duration_seconds_count',
                        [('tool', '__unknown__')]) == unknown_count
    assert metric_value(metrics, 'mcp_requests_total',
                        [('tool', 'health'), ('status', 'ok')]) == health_count
    assert metric_value(metrics, 'mcp_request_duration_seconds_count',
                        [('tool', 'health')]) == health_count
    assert re.search(r'^mcp_active_connections 0$', metrics, re.MULTILINE), metrics

    resources = process_resources(pid)
    if resources:
        resource_samples.append(resources)
    if resource_samples:
        max_rss_kib = max(sample['VmRSS'] for sample in resource_samples)
        max_hwm_kib = max(sample['VmHWM'] for sample in resource_samples)
        max_threads = max(sample['Threads'] for sample in resource_samples)
        max_fds = max(sample['FileDescriptors'] for sample in resource_samples)
        assert max_rss_kib <= 768 * 1024, max_rss_kib
        assert max_hwm_kib <= 768 * 1024, max_hwm_kib
        assert max_threads <= 256, max_threads
        assert max_fds <= 512, max_fds
        resource_summary = (f'; max RSS/HWM={max_rss_kib}/{max_hwm_kib} KiB, '
                            f'threads={max_threads}, fds={max_fds}')
    else:
        resource_summary = '; /proc resource counters unavailable'
    elapsed = time.monotonic() - started
    assert elapsed >= seconds, elapsed
    print(f'PASS telemetry endurance: {elapsed:.1f}s, {unknown_count} unique unknown + '
          f'{health_count} healthy calls, two bounded tool labels{resource_summary}')


def check_envelopes(url, reject_duplicate_properties=True, expected_allow_header='POST'):
    client = WebSocket(url, TOKEN)
    try:
        cases = [(text, -32600, expected_id, http_status)
                 for text, expected_id, http_status in INVALID]
        cases.append(('{', -32700, None, 200))
        for text, expected, expected_id, http_status in cases:
            status, body = post(url, text)
            assert status == http_status, (text, status, body)
            if http_status == 400:
                assert not body, (text, body)
                http = None
            else:
                http = json.loads(body)
            client.send(text.encode())
            opcode, body = client.receive()
            ws = json.loads(body)
            assert opcode == 1, opcode
            if http is not None:
                assert ws == http, (ws, http)
            assert set(ws) == {'jsonrpc', 'id', 'error'} and ws['id'] == expected_id, (text, ws)
            assert ws['error']['code'] == expected, (text, ws)

        # The .NET port intentionally rejects ambiguous duplicate properties. The exact Rust
        # references use serde_json's last-key-wins behavior; exercise and record both contracts
        # instead of weakening the .NET parser to match it.
        duplicate = '{"jsonrpc":"2.0","id":1,"id":2,"method":"health"}'
        status, body = post(url, duplicate)
        assert status == 200, (status, body)
        http = json.loads(body)
        client.send(duplicate.encode())
        opcode, body = client.receive()
        ws = json.loads(body)
        assert opcode == 1 and ws == http, (ws, http)
        if reject_duplicate_properties:
            assert http == {'jsonrpc': '2.0', 'id': None,
                            'error': {'code': -32600, 'message': 'Invalid Request'}}, http
        else:
            assert http['jsonrpc'] == '2.0' and http['id'] == 2, http
            assert http['result']['status'] == 'healthy', http
        # Valid notifications/responses have no reply. Verify WS ordering by following
        # each with a ping and requiring that ping to be the next response.
        for ignored in [
            {'jsonrpc': '2.0', 'method': 'notifications/initialized'},
            {'jsonrpc': '2.0', 'id': 1, 'result': {}},
            {'jsonrpc': '2.0', 'id': 1, 'error': {'code': -1, 'message': 'test'}},
        ]:
            status, body = post(url, json.dumps(ignored))
            assert status == 202 and not body, (status, body)
            client.send(json.dumps(ignored).encode())
            client.send(b'{"jsonrpc":"2.0","id":"after-ignored","method":"ping"}')
            opcode, body = client.receive()
            assert json.loads(body) == {'jsonrpc': '2.0', 'id': 'after-ignored', 'result': {}}, body
    finally:
        client.close()
    request = urllib.request.Request(url + '/mcp', headers={'Authorization': f'Bearer {TOKEN}'})
    try:
        urllib.request.urlopen(request, timeout=5)
        raise AssertionError('GET unexpectedly accepted')
    except urllib.error.HTTPError as error:
        assert error.code == 405, error.code
        assert error.headers['Allow'] == expected_allow_header, dict(error.headers)
    print(f'PASS envelope contract: {len(INVALID)} invalid envelopes + syntax error over HTTP and WS; ignored input/recovery; GET405')


def check_http_profile(url, allowed_origin=None, dotnet_hardened_profile=True):
    parsed = urllib.parse.urlparse(url)
    payload = b'{"jsonrpc":"2.0","id":1,"method":"ping"}'
    normal = [('Authorization', f'Bearer {TOKEN}'), ('Content-Type', 'application/json'),
              ('Accept', 'application/json, text/event-stream')]

    def send(headers):
        connection = http.client.HTTPConnection(parsed.hostname, parsed.port, timeout=5)
        try:
            connection.putrequest('POST', '/mcp')
            for key, value in headers:
                connection.putheader(key, value)
            connection.putheader('Content-Length', str(len(payload)))
            connection.endheaders(payload)
            response = connection.getresponse()
            response.read()
            return response.status
        finally:
            connection.close()

    cases = [
        ([entry for entry in normal if entry[0] != 'Content-Type'],
         415 if dotnet_hardened_profile else 200),
        ([(k, 'text/plain' if k == 'Content-Type' else v) for k, v in normal], 415),
        ([(k, 'application/json; charset=utf-16' if k == 'Content-Type' else v) for k, v in normal], 415),
        ([entry for entry in normal if entry[0] != 'Accept'],
         406 if dotnet_hardened_profile else 200),
        ([(k, 'application/json' if k == 'Accept' else v) for k, v in normal],
         406 if dotnet_hardened_profile else 200),
        ([(k, '*/*' if k == 'Accept' else v) for k, v in normal],
         406 if dotnet_hardened_profile else 200),
        ([(k, 'application/json, text/event-stream;q=0' if k == 'Accept' else v) for k, v in normal],
         406 if dotnet_hardened_profile else 200),
        (normal + [('MCP-Protocol-Version', '2024-11-05')], 400),
        (normal + [('MCP-Protocol-Version', '2025-06-18'), ('MCP-Protocol-Version', '2025-06-18')], 400),
        (normal + [('Authorization', f'Bearer {TOKEN}')],
         401 if dotnet_hardened_profile else 200),
        ([(k, f'Bearer {TOKEN}, Bearer other' if k == 'Authorization' else v) for k, v in normal],
         401 if dotnet_hardened_profile else 403),
        ([(k, 'Bearer wrong-token' if k == 'Authorization' else v) for k, v in normal], 403),
        (normal + [('Origin', 'https://unlisted.test')], 403),
        (normal + [('Origin', 'null')], 403),
        (normal + [('Origin', 'https://client.test'), ('Origin', 'https://client.test')], 403),
        ([(k, f'bEaReR {TOKEN}' if k == 'Authorization' else v) for k, v in normal],
         200 if dotnet_hardened_profile else 401),
        (normal + [('MCP-Protocol-Version', '2025-06-18')], 200),
        (normal + [('Origin', allowed_origin or 'https://client.test')], 200 if allowed_origin else 403),
    ]
    for headers, expected in cases:
        actual = send(headers)
        assert actual == expected, (headers, expected, actual)
    assert send(normal) == 200, 'valid traffic must recover after rejected headers'
    print(f'PASS HTTP profile: {len(cases)} negotiation/auth/origin cases, recovery; configured origin={bool(allowed_origin)}')


def check_ws_lifetime(url, seconds):
    client = WebSocket(url, TOKEN, subprotocol='mcp')
    try:
        payload = json.dumps({'jsonrpc': '2.0', 'id': 'fragmented', 'method': 'ping', 'params': {'text': 'café'}}, ensure_ascii=False).encode()
        split = payload.index('é'.encode()) + 1
        client.send(payload[:split], final=False)
        client.send(payload[split:], opcode=0)
        opcode, body = client.receive()
        assert opcode == 1 and json.loads(body)['id'] == 'fragmented', body
    finally:
        client.close()
    # Exactly one complete-message budget applies, including a stalled continuation.
    for partial in (False, True):
        client = WebSocket(url, TOKEN)
        client.sock.settimeout(seconds + 3)
        started = time.monotonic()
        try:
            if partial:
                client.send(b'{', final=False)
            try:
                opcode, _ = client.receive()
                assert opcode == 8, opcode
            except (ConnectionError, OSError):
                pass
            assert time.monotonic() - started < seconds + 3
        finally:
            client.sock.close()
    client = WebSocket(url, TOKEN)
    try:
        client.send(b'x' * (1024 * 1024 + 1), opcode=2)
        try:
            opcode, body = client.receive()
            assert opcode == 8 and len(body) >= 2 and struct.unpack('!H', body[:2])[0] == 1009, (opcode, body[:128])
        except (ConnectionError, OSError):
            pass
    finally:
        client.sock.close()
    # A fresh valid connection must work after every rejected/expired peer.
    client = WebSocket(url, TOKEN)
    try:
        client.send(b'{"jsonrpc":"2.0","id":"recovered","method":"ping"}')
        assert json.loads(client.receive()[1])['id'] == 'recovered'
    finally:
        client.close()
    print(f'PASS WebSocket: mcp subprotocol, fragmented UTF-8, idle/unfinished-message {seconds}s expiry, oversize and recovery')


async def check_sdk(url, expected_server_name):
    async with streamablehttp_client(url + '/mcp', headers={'Authorization': f'Bearer {TOKEN}'}, timeout=timedelta(seconds=10)) as (read, write, get_session):
        async with ClientSession(read, write, read_timeout_seconds=timedelta(seconds=10)) as session:
            initialized = await session.initialize()
            assert initialized.protocolVersion == '2025-06-18', initialized
            assert initialized.serverInfo.name == expected_server_name, initialized
            assert initialized.capabilities.tools is not None
            assert get_session() is None
            await session.send_ping()
            listed = await session.list_tools()
            names = [tool.name for tool in listed.tools]
            assert len(names) == 23 and names == sorted(set(names)), names
            for tool in listed.tools:
                assert tool.inputSchema['type'] == 'object', tool
                Draft202012Validator.check_schema(tool.inputSchema)
            result = await session.call_tool('classify', {'user_agent': 'GPTBot/1.0', 'path': '/', 'request_id': 'official-sdk'})
            assert not result.isError and result.structuredContent['request_id'] == 'official-sdk', result
            assert any(s['name'] == 'ua_ai_bot' for s in result.structuredContent['signals']), result
            assert json.loads(result.content[0].text) == result.structuredContent, result
            health = await session.call_tool('health', {})
            assert health.structuredContent['status'] == 'healthy', health
            # Send one representative call for each advertised tool through the real
            # SDK. Compare to its direct contract; unavailable backends must remain
            # explicit failures rather than synthetic successful tool results.
            cases = {case.name: case for case in reversed(CORE_CASES)}
            assert set(cases) == set(names), (set(cases), set(names))
            for identifier, tool in enumerate(listed.tools, 1000):
                case = cases[tool.name]
                # Current Rust narrows this previously ignored hint to the only format the
                # structured MCP result actually emits. "json" is accepted by historical and
                # .NET builds too, so it is the cross-reference SDK qualification input.
                if tool.name == 'explain':
                    params = {**case.params, 'format': 'json'}
                elif tool.name == 'score_breakdown':
                    params = {'request_id': 'protocol-score',
                              'signals': {'user_agent': 'GPTBot/1.0'}}
                else:
                    params = case.params
                Draft202012Validator(tool.inputSchema).validate(params)
                _, body = post(url, json.dumps({'jsonrpc': '2.0', 'id': identifier,
                                               'method': case.name, 'params': params}))
                direct = json.loads(body)
                try:
                    wrapped = await session.call_tool(case.name, params)
                except McpError as error:
                    assert error.error.code == direct['error']['code'], (case.name, error, direct)
                else:
                    assert 'error' not in direct and not wrapped.isError, (case.name, wrapped, direct)
                    assert normalize_payload(wrapped.structuredContent) == normalize_payload(direct['result']), (case.name, wrapped, direct)
                    assert json.loads(wrapped.content[0].text) == wrapped.structuredContent
            try:
                await session.call_tool('no_such_tool', {})
                raise AssertionError('unknown tool accepted')
            except McpError as error:
                assert error.error.code == -404, error
    print('PASS official Python SDK: initialize/counteroffer, initialized notification, ping, 23 schemas and all-tool calls/structuredContent and unknown-tool failure')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument('--dotnet-dll', type=Path)
    target.add_argument('--binary', type=Path)
    target.add_argument('--url', help='Explicit isolated test server; no host process is started')
    parser.add_argument('--allowed-origin', help='Configure/test one explicit origin on a spawned host; external URLs must already match')
    parser.add_argument('--ws-deadline-seconds', type=int, help='Set/test a short receive budget on an isolated host (default message cap required)')
    parser.add_argument('--otlp-failure', action='store_true', help='Qualify unavailable OTLP sink isolation and log redaction')
    parser.add_argument('--telemetry-endurance-seconds', type=int,
                        help='Run a bounded-label and process-resource endurance probe')
    parser.add_argument('--expected-server-name',
                        help='Expected initialize serverInfo.name (inferred for spawned known runtimes)')
    args = parser.parse_args()
    if args.ws_deadline_seconds is not None and args.ws_deadline_seconds < 1:
        parser.error('WebSocket budget must be positive')
    if args.telemetry_endurance_seconds is not None and args.telemetry_endurance_seconds < 1:
        parser.error('telemetry endurance duration must be positive')
    if args.otlp_failure and args.telemetry_endurance_seconds:
        parser.error('run OTLP failure and telemetry endurance as separate isolated-host probes')
    if args.url:
        if args.otlp_failure or args.telemetry_endurance_seconds:
            parser.error('OTLP/endurance probes require a spawned host so logs/resources can be inspected')
        check_envelopes(args.url)
        check_http_profile(args.url, args.allowed_origin)
        if args.ws_deadline_seconds:
            check_ws_lifetime(args.url, args.ws_deadline_seconds)
        asyncio.run(check_sdk(args.url, args.expected_server_name or 'request-guard-mcp-dotnet'))
        return
    executable = (args.binary or args.dotnet_dll).resolve()
    if not executable.is_file():
        parser.error(f'build the host first; executable not found: {executable}')
    command = [str(executable)] if args.binary else ['dotnet', str(executable)]
    expected_server_name = args.expected_server_name or ('request-guard-mcp' if args.binary else 'request-guard-mcp-dotnet')
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0))
        port = reservation.getsockname()[1]
    url = f'http://127.0.0.1:{port}'
    with tempfile.TemporaryDirectory(prefix='dotnet-protocol-') as directory:
        environment = {**clean_environment(), 'MCP__HOST': '127.0.0.1', 'MCP__PORT': str(port),
                       'AUTH_TOKENS': TOKEN, 'CACHE_SCOPE_HMAC_KEY': 'synthetic-protocol-cache-scope-key-32-bytes'}
        if args.allowed_origin:
            environment['MCP_ALLOWED_ORIGINS'] = args.allowed_origin
        if args.ws_deadline_seconds:
            environment['MCP__LIMITS__WEBSOCKET_IDLE_TIMEOUT_SECS'] = str(args.ws_deadline_seconds)
        if args.otlp_failure:
            environment['MCP__TELEMETRY__OTLP_ENDPOINT'] = f'http://user:{OTLP_SECRET}@127.0.0.1:1'
        log_path = Path(directory) / 'server.log'
        with open(log_path, 'w+') as log:
            process = subprocess.Popen(command, cwd=directory, env=environment, stdout=log, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 20
                while True:
                    if process.poll() is not None:
                        raise RuntimeError('server exited at startup')
                    try:
                        with urllib.request.urlopen(url + '/health', timeout=1):
                            break
                    except OSError:
                        if time.monotonic() >= deadline:
                            raise TimeoutError('server startup')
                        time.sleep(0.1)
                if args.otlp_failure:
                    check_otlp_sink_failure(url)
                elif args.telemetry_endurance_seconds:
                    check_telemetry_endurance(url, args.telemetry_endurance_seconds, process.pid)
                else:
                    check_envelopes(url, reject_duplicate_properties=args.binary is None,
                                    expected_allow_header=('POST, GET' if args.binary else 'POST'))
                    check_http_profile(url, args.allowed_origin,
                                       dotnet_hardened_profile=args.binary is None)
                    if args.ws_deadline_seconds:
                        check_ws_lifetime(url, args.ws_deadline_seconds)
                    asyncio.run(check_sdk(url, expected_server_name))
            except BaseException:
                log.seek(0)
                print(log.read(), file=sys.stderr)
                raise
            finally:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
        if args.otlp_failure or args.telemetry_endurance_seconds:
            captured = log_path.read_text(errors='replace')
            if args.otlp_failure:
                assert OTLP_SECRET not in captured and f'user:{OTLP_SECRET}' not in captured, captured
                print('PASS configured OTLP credentials are absent from JSON console logs')
            if args.telemetry_endurance_seconds:
                assert CARDINALITY_MARKER not in captured, captured
                print('PASS attacker-selected unknown method names are absent from JSON console logs')


if __name__ == '__main__':
    main()
