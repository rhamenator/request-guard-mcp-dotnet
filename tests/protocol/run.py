#!/usr/bin/env python3
"""Running-build MCP envelope and pinned official Python SDK qualification."""
import argparse
import asyncio
from datetime import timedelta
import json
from pathlib import Path
import socket
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

TOKEN = 'synthetic-protocol-client-token'
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
        'Content-Type': 'application/json', 'Authorization': f'Bearer {TOKEN}'})
    with urllib.request.urlopen(request, timeout=5) as response:
        return response.status, response.read()


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
    target.add_argument('--url', help='Explicit isolated test server; no host process is started')
    args = parser.parse_args()
    if args.url:
        check_envelopes(args.url)
        asyncio.run(check_sdk(args.url))
        return
    dll = args.dotnet_dll.resolve()
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0))
        port = reservation.getsockname()[1]
    url = f'http://127.0.0.1:{port}'
    with tempfile.TemporaryDirectory(prefix='dotnet-protocol-') as directory:
        environment = {**clean_environment(), 'MCP__HOST': '127.0.0.1', 'MCP__PORT': str(port),
                       'AUTH_TOKENS': TOKEN, 'CACHE_SCOPE_HMAC_KEY': 'synthetic-protocol-cache-scope-key-32-bytes'}
        with open(Path(directory) / 'server.log', 'w+') as log:
            process = subprocess.Popen(['dotnet', str(dll)], cwd=directory, env=environment, stdout=log, stderr=subprocess.STDOUT)
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
                check_envelopes(url)
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


if __name__ == '__main__':
    main()
