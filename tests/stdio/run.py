#!/usr/bin/env python3
"""Actual-process stdio contract, stdout discipline and official SDK smoke."""
import argparse
import asyncio
from datetime import timedelta
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client
from mcp.shared.exceptions import McpError

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'parity'))
from run import clean_environment


async def sdk(command, environment, directory, log):
    server = StdioServerParameters(command=command[0], args=command[1:] + ['--stdio'], env=environment, cwd=directory)
    async with stdio_client(server, errlog=log) as (read, write):
        async with ClientSession(read, write, read_timeout_seconds=timedelta(seconds=10)) as session:
            initialized = await session.initialize()
            assert initialized.protocolVersion == '2025-06-18', initialized
            await session.send_ping()
            listed = await session.list_tools()
            assert len(listed.tools) == 23
            result = await session.call_tool('classify', {'user_agent': 'GPTBot/1.0', 'request_id': 'stdio-sdk'})
            assert result.structuredContent['request_id'] == 'stdio-sdk' and not result.isError, result
            assert json.loads(result.content[0].text) == result.structuredContent
            try:
                await session.call_tool('explain', {})
                raise AssertionError('invalid tool call accepted')
            except McpError as error:
                assert error.error.code == -400, error
    print('PASS official SDK stdio: initialization, notification, ping, 23 definitions, classification and tool error')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument('--dotnet-dll', type=Path)
    target.add_argument('--binary', type=Path)
    args = parser.parse_args()
    executable = (args.binary or args.dotnet_dll).resolve()
    if not executable.is_file():
        parser.error('build the host first')
    prefix = [str(executable)] if args.binary else ['dotnet', str(executable)]
    # Reserve the configured HTTP port throughout. Stdio must start and work without
    # taking it or serving any network traffic, even with no AUTH_TOKENS configured.
    with socket.socket() as reservation, tempfile.TemporaryDirectory(prefix='dotnet-stdio-') as directory:
        reservation.bind(('127.0.0.1', 0))
        reservation.listen()
        environment = {**clean_environment(), 'MCP__PORT': str(reservation.getsockname()[1]),
                       'MCP__HOST': '127.0.0.1', 'MCP__LIMITS__MAX_REQUEST_BYTES': '256', 'LOG_LEVEL': 'trace'}
        command = prefix + ['--stdio']
        lines = [
            b'{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}',
            b'{"jsonrpc":"2.0","method":"notifications/initialized"}',
            b'{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"explain","arguments":{}}}',
            b'x' * 10000,
            b'\xff',
            b'{"jsonrpc":"2.0","id":"last","method":"ping"}',
        ]
        result = subprocess.run(command, input=b'\n'.join(lines) + b'\n', cwd=directory, env=environment,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
        assert result.returncode == 0, (result.returncode, result.stderr.decode(errors='replace'))
        responses = [json.loads(line) for line in result.stdout.splitlines()]
        assert len(responses) == 5, result.stdout
        assert responses[0]['result']['protocolVersion'] == '2025-06-18'
        assert [r['error']['code'] for r in responses[1:4]] == [-400, -413, -32700], responses
        assert responses[-1] == {'jsonrpc': '2.0', 'id': 'last', 'result': {}}, responses
        assert result.stderr, 'expected tool-error diagnostics on stderr'
        print('PASS actual-process stdio: no HTTP bind/auth requirement; JSON-only stdout, stderr diagnostics; bounded oversized/UTF-8 input recovery and EOF exit')
        postgres_environment = {**environment, 'MCP__POSTGRES__URL': 'Host=127.0.0.1;Port=1;Database=unreachable'}
        postgres = subprocess.run(command, input=b'', cwd=directory, env=postgres_environment,
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
        assert postgres.returncode == 1 and b'PostgreSQL is not yet supported in stdio mode' in postgres.stderr, postgres
        assert not postgres.stdout, postgres.stdout
        print('PASS actual-process stdio: PostgreSQL persistence is rejected before connection')
        with open(Path(directory) / 'sdk.log', 'w+') as log:
            asyncio.run(sdk(prefix, environment, directory, log))
        # Check termination while waiting for more input, after the process is ready.
        process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                   cwd=directory, env=environment)
        try:
            process.stdin.write(b'{"jsonrpc":"2.0","id":1,"method":"ping"}\n')
            process.stdin.flush()
            # A bounded helper thread avoids an unbounded readline if startup regresses.
            import concurrent.futures
            with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
                ready = pool.submit(process.stdout.readline)
                try:
                    assert json.loads(ready.result(timeout=10))['id'] == 1
                except BaseException:
                    process.kill()
                    raise
            process.terminate()
            process.wait(timeout=5)
            assert process.returncode == 0 if os.name != 'nt' else process.returncode is not None
            print('PASS stdio termination while waiting for input')
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=5)
            process.stdin.close()
            process.stdout.close()


if __name__ == '__main__':
    main()
