#!/usr/bin/env python3
"""Running-build MCP envelope and pinned official Python SDK qualification."""
import argparse
import asyncio
from datetime import timedelta
import json
import http.client
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
INVALID = [
    '{}', '[]', 'null',
    '{"id":1,"method":"health"}',
    '{"jsonrpc":"1.0","id":1,"method":"health"}',
    '{"jsonrpc":"2.0","id":null,"method":"health"}',
    '{"jsonrpc":"2.0","id":true,"method":"health"}',
    '{"jsonrpc":"2.0","id":1.5,"method":"health"}',
    '{"jsonrpc":"2.0","id":{},"method":"health"}',
    '{"jsonrpc":"2.0","id":1,"method":42}',
    '{"jsonrpc":"2.0","id":1,"method":"health","params":[]}',
    '{"jsonrpc":"2.0","method":"health","params":null}',
    '{"jsonrpc":"2.0","id":1,"method":"health","result":{}}',
    '{"jsonrpc":"2.0","id":1,"result":{},"error":null}',
    '{"jsonrpc":"2.0","id":1,"error":{"code":1.5,"message":"bad"}}',
    '{"jsonrpc":"2.0","id":1,"id":2,"method":"health"}',
]


def post(url, text):
    request = urllib.request.Request(url + '/mcp', text.encode(), headers={
        'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream', 'Authorization': f'Bearer {TOKEN}'})
    with urllib.request.urlopen(request, timeout=5) as response:
        return response.status, response.read()


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


def check_envelopes(url):
    client = WebSocket(url, TOKEN)
    try:
        for text, expected in [(text, -32600) for text in INVALID] + [('{', -32700)]:
            status, body = post(url, text)
            assert status == 200, status
            http = json.loads(body)
            client.send(text.encode())
            opcode, body = client.receive()
            ws = json.loads(body)
            assert opcode == 1 and ws == http, (ws, http)
            assert set(http) == {'jsonrpc', 'id', 'error'} and http['id'] is None, http
            assert http['error']['code'] == expected, http
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
        assert error.code == 405 and error.headers['Allow'] == 'POST', error.code
    print(f'PASS envelope contract: {len(INVALID)} invalid envelopes + syntax error over HTTP and WS; ignored input/recovery; GET405')


def check_http_profile(url, allowed_origin=None):
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
        ([entry for entry in normal if entry[0] != 'Content-Type'], 415),
        ([(k, 'text/plain' if k == 'Content-Type' else v) for k, v in normal], 415),
        ([(k, 'application/json; charset=utf-16' if k == 'Content-Type' else v) for k, v in normal], 415),
        ([entry for entry in normal if entry[0] != 'Accept'], 406),
        ([(k, 'application/json' if k == 'Accept' else v) for k, v in normal], 406),
        ([(k, 'application/json, text/event-stream;q=0' if k == 'Accept' else v) for k, v in normal], 406),
        (normal + [('MCP-Protocol-Version', '2024-11-05')], 400),
        (normal + [('MCP-Protocol-Version', '2025-06-18'), ('MCP-Protocol-Version', '2025-06-18')], 400),
        (normal + [('Authorization', f'Bearer {TOKEN}')], 401),
        ([(k, f'Bearer {TOKEN}, Bearer other' if k == 'Authorization' else v) for k, v in normal], 401),
        ([(k, 'Bearer wrong-token' if k == 'Authorization' else v) for k, v in normal], 403),
        (normal + [('Origin', 'https://unlisted.test')], 403),
        (normal + [('Origin', 'null')], 403),
        (normal + [('Origin', 'https://client.test'), ('Origin', 'https://client.test')], 403),
        ([(k, f'bEaReR {TOKEN}' if k == 'Authorization' else v) for k, v in normal], 200),
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


async def check_sdk(url):
    async with streamablehttp_client(url + '/mcp', headers={'Authorization': f'Bearer {TOKEN}'}, timeout=timedelta(seconds=10)) as (read, write, get_session):
        async with ClientSession(read, write, read_timeout_seconds=timedelta(seconds=10)) as session:
            initialized = await session.initialize()
            assert initialized.protocolVersion == '2025-06-18', initialized
            assert initialized.serverInfo.name == 'request-guard-mcp-dotnet', initialized
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
                Draft202012Validator(tool.inputSchema).validate(case.params)
                _, body = post(url, json.dumps({'jsonrpc': '2.0', 'id': identifier,
                                               'method': case.name, 'params': case.params}))
                direct = json.loads(body)
                try:
                    wrapped = await session.call_tool(case.name, case.params)
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
    args = parser.parse_args()
    if args.ws_deadline_seconds is not None and args.ws_deadline_seconds < 1:
        parser.error('WebSocket budget must be positive')
    if args.url:
        if args.otlp_failure:
            parser.error('--otlp-failure requires a spawned host so its logs can be inspected')
        check_envelopes(args.url)
        check_http_profile(args.url, args.allowed_origin)
        if args.ws_deadline_seconds:
            check_ws_lifetime(args.url, args.ws_deadline_seconds)
        asyncio.run(check_sdk(args.url))
        return
    executable = (args.binary or args.dotnet_dll).resolve()
    if not executable.is_file():
        parser.error(f'build the host first; executable not found: {executable}')
    command = [str(executable)] if args.binary else ['dotnet', str(executable)]
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0))
        port = reservation.getsockname()[1]
    url = f'http://127.0.0.1:{port}'
    with tempfile.TemporaryDirectory(prefix='dotnet-protocol-') as directory:
        environment = {**clean_environment(), 'MCP__HOST': '127.0.0.1', 'MCP__PORT': str(port),
                       'AUTH_TOKENS': TOKEN, 'CACHE_SCOPE_HMAC_KEY': 'synthetic-protocol-cache-scope-key-32-bytes'}
        if args.allowed_origin:
            environment['MCP__AUTH__ALLOWED_ORIGINS__0'] = args.allowed_origin
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
                else:
                    check_envelopes(url)
                    check_http_profile(url, args.allowed_origin)
                    if args.ws_deadline_seconds:
                        check_ws_lifetime(url, args.ws_deadline_seconds)
                    asyncio.run(check_sdk(url))
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
        if args.otlp_failure:
            captured = log_path.read_text(errors='replace')
            assert OTLP_SECRET not in captured and f'user:{OTLP_SECRET}' not in captured, captured
            print('PASS configured OTLP credentials are absent from JSON console logs')


if __name__ == '__main__':
    main()
