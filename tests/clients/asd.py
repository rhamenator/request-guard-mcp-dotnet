#!/usr/bin/env python3
"""Qualify the pinned AI Scraping Defense MCP adapter against this server."""

import argparse
from importlib.metadata import version
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import tempfile
import time
import tomllib
import urllib.error
import urllib.request


TOKEN = 'synthetic-asd-client-token-at-least-32-bytes'
ASD_VERSION = '0.0.13'
ASD_REVISION = '94db05fba00b5cb7c8b1753ff7ba7ee885b86eb4'
DEPENDENCIES = {
    'mcp': '2.3.0',
    'httpx': '0.28.1',
    'websockets': '16.1.1',
    'joblib': '1.5.3',
}


def clean_environment() -> dict[str, str]:
    excluded = {
        'AUTH_TOKENS',
        'CACHE_SCOPE_HMAC_KEY',
        'CONFIG_FILE',
        'LOG_LEVEL',
        'MCP_ALLOWED_ORIGINS',
        'TLS_FINGERPRINT_ATTESTATION_KEY',
        'TLS_FINGERPRINT_ATTESTATION_PREVIOUS_KEY',
    }
    return {
        key: value for key, value in os.environ.items()
        if not key.startswith(('MCP__', 'MCP_SERVER_')) and key not in excluded
    }


def import_adapter(source: Path):
    source = source.resolve()
    actual = subprocess.run(
        ['git', '-C', str(source), 'rev-parse', 'HEAD'], check=True,
        text=True, stdout=subprocess.PIPE).stdout.strip()
    if actual != ASD_REVISION:
        raise AssertionError(
            f'AI Scraping Defense source must be exact {ASD_REVISION}; found {actual}')
    with (source / 'pyproject.toml').open('rb') as project_file:
        source_version = tomllib.load(project_file)['project']['version']
    if source_version != ASD_VERSION:
        raise AssertionError(
            f'AI Scraping Defense version must be {ASD_VERSION}; found {source_version}')
    installed = {name: version(name) for name in DEPENDENCIES}
    if installed != DEPENDENCIES:
        raise AssertionError(f'AI Scraping Defense dependency drift: {installed}')
    sys.path.insert(0, str(source))
    from src.shared.model_adapters import MCPAdapter  # pylint: disable=import-outside-toplevel
    return MCPAdapter


def wait_ready(process: subprocess.Popen, url: str) -> None:
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError('server exited during startup')
        try:
            with urllib.request.urlopen(url + '/health', timeout=1) as response:
                if response.status == 200:
                    return
        except (OSError, urllib.error.URLError):
            time.sleep(0.1)
    raise TimeoutError('server startup')


def read_metrics(url: str) -> str:
    with urllib.request.urlopen(url + '/metrics', timeout=5) as response:
        assert response.status == 200, response.status
        return response.read().decode().replace('\r\n', '\n')


def adapter(MCPAdapter, *, transport: str, endpoint: str | None = None,
            executable: str | None = None, args: list[str] | None = None,
            token: str | None = TOKEN):
    config = {'transport': transport, 'timeout': 10}
    if endpoint:
        config['endpoint'] = endpoint
    if executable:
        config['executable'] = executable
    if args is not None:
        config['args'] = args
    if token is not None:
        config['auth_token'] = token
    return MCPAdapter(
        'mcp://primary/classify?argument_key=&options_key=', config=config)


def require_classification(result: dict, request_id: str) -> None:
    assert 'error' not in result, result
    assert result.get('request_id') == request_id, result
    assert result.get('verdict') == 'block', result
    assert result.get('threat_category') == 'ai_scraping', result
    assert 0 <= result.get('score', -1) <= 1, result
    assert any(signal['name'] == 'ua_ai_bot' for signal in result.get('signals', [])), result
    assert 'structured_content' not in result and 'structuredContent' not in result, result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet-dll', type=Path, required=True)
    parser.add_argument('--asd-source', type=Path, required=True)
    args = parser.parse_args()
    dll = args.dotnet_dll.resolve()
    if not dll.is_file():
        parser.error(f'build the host first; executable not found: {dll}')
    MCPAdapter = import_adapter(args.asd_source)

    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0))
        port = reservation.getsockname()[1]
    http_url = f'http://127.0.0.1:{port}'
    websocket_url = f'ws://127.0.0.1:{port}/mcp'
    environment = {
        **clean_environment(),
        'MCP__HOST': '127.0.0.1',
        'MCP__PORT': str(port),
        'AUTH_TOKENS': TOKEN,
        'CACHE_SCOPE_HMAC_KEY': 'synthetic-asd-cache-scope-key-at-least-32-bytes',
    }
    payload = {
        'ip': '203.0.113.10',
        'user_agent': 'GPTBot/1.0',
        'path': '/wp-login.php',
        'method': 'POST',
        'request_id': 'asd-websocket',
    }

    with tempfile.TemporaryDirectory(prefix='request-guard-asd-client-') as directory:
        log_path = Path(directory) / 'server.log'
        with log_path.open('w+', encoding='utf-8') as log:
            process = subprocess.Popen(
                ['dotnet', str(dll)], cwd=directory, env=environment,
                stdout=log, stderr=subprocess.STDOUT, text=True)
            try:
                wait_ready(process, http_url)
                for supplied, expected_status in ((None, '401'), ('wrong-token', '403')):
                    rejected = adapter(
                        MCPAdapter, transport='ws', endpoint=websocket_url,
                        token=supplied).predict(payload)
                    assert set(rejected) == {'error'}, rejected
                    assert f'HTTP {expected_status}' in rejected['error'], rejected
                    assert TOKEN not in rejected['error'], rejected
                assert not re.search(
                    r'^mcp_requests_total\{', read_metrics(http_url), re.MULTILINE), (
                    'authentication failures reached the dispatcher')

                websocket_result = adapter(
                    MCPAdapter, transport='ws', endpoint=websocket_url).predict(payload)
                require_classification(websocket_result, 'asd-websocket')
                assert ('mcp_requests_total{tool="classify",status="ok"} 1'
                        in read_metrics(http_url))

                stdio_payload = {**payload, 'request_id': 'asd-stdio'}
                stdio_result = adapter(
                    MCPAdapter, transport='stdio', executable='dotnet',
                    args=[str(dll), '--stdio'], token=None).predict(stdio_payload)
                require_classification(stdio_result, 'asd-stdio')
            except BaseException:
                log.flush()
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
            log.flush()
            log.seek(0)
            assert TOKEN not in log.read(), 'server log exposed the configured token'

    print(
        f'PASS AI Scraping Defense client {ASD_VERSION} ({ASD_REVISION}): authenticated WebSocket and '
        'stdio classification shape; safe pre-dispatch 401/403 Bearer rejection')


if __name__ == '__main__':
    main()
