#!/usr/bin/env python3
"""Qualify TLS/ALPN and HTTP/WebSocket behavior through a local reverse proxy."""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import ssl
import struct
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import sys

TOKEN = 'synthetic-protocol-client-token-at-least-32-bytes'


def clean_environment():
    names = {
        'AUTH_TOKENS', 'CACHE_SCOPE_HMAC_KEY', 'CONFIG_FILE', 'LOG_LEVEL',
        'MCP_ALLOWED_ORIGINS', 'TLS_FINGERPRINT_ATTESTATION_KEY',
        'TLS_FINGERPRINT_ATTESTATION_PREVIOUS_KEY',
        'TLS_FINGERPRINT_ATTESTATION_MAX_AGE_SECONDS', 'TLS_KNOWN_BAD_JA3',
        'TLS_KNOWN_BAD_JA4',
    }
    return {key: value for key, value in os.environ.items()
            if not key.startswith('MCP__') and key not in names}


def reserve_port():
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0))
        return reservation.getsockname()[1]


def run_checked(command, **kwargs):
    return subprocess.run(command, check=True, text=True, capture_output=True, **kwargs)


def create_certificate(directory):
    ca_key = directory / 'ca.key'
    ca_cert = directory / 'ca.pem'
    server_key = directory / 'server.key'
    server_request = directory / 'server.csr'
    server_cert = directory / 'server.pem'
    extensions = directory / 'server.ext'
    extensions.write_text('subjectAltName=DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\n')
    run_checked(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                 '-subj', '/CN=Request Guard test CA',
                 '-addext', 'basicConstraints=critical,CA:TRUE',
                 '-addext', 'keyUsage=critical,keyCertSign,cRLSign',
                 '-addext', 'subjectKeyIdentifier=hash',
                 '-keyout', str(ca_key), '-out', str(ca_cert)])
    run_checked(['openssl', 'req', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=localhost',
                 '-keyout', str(server_key), '-out', str(server_request)])
    run_checked(['openssl', 'x509', '-req', '-days', '1', '-in', str(server_request),
                 '-CA', str(ca_cert), '-CAkey', str(ca_key), '-CAcreateserial',
                 '-extfile', str(extensions), '-out', str(server_cert)])
    return ca_cert, server_cert, server_key


def write_nginx_config(directory, proxy_port, backend_port, certificate, key):
    config = directory / 'nginx.conf'
    config.write_text(f'''worker_processes 1;
error_log {directory / 'nginx-error.log'} info;
pid {directory / 'nginx.pid'};
events {{ worker_connections 128; }}
http {{
    access_log {directory / 'nginx-access.log'};
    map $http_upgrade $connection_upgrade {{ default upgrade; '' close; }}
    server {{
        listen 127.0.0.1:{proxy_port} ssl http2;
        server_name localhost;
        ssl_certificate {certificate};
        ssl_certificate_key {key};
        ssl_protocols TLSv1.2 TLSv1.3;
        location / {{
            proxy_pass http://127.0.0.1:{backend_port};
            proxy_http_version 1.1;
            proxy_set_header Host $host;
            proxy_set_header Authorization $http_authorization;
            proxy_set_header Origin $http_origin;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection $connection_upgrade;
            proxy_request_buffering off;
            proxy_buffering off;
        }}
    }}
}}
''')
    return config


def wait_for_health(url, context, process, label):
    deadline = time.monotonic() + 20
    while True:
        if process.poll() is not None:
            raise RuntimeError(f'{label} exited during startup')
        try:
            with urllib.request.urlopen(url + '/health', context=context, timeout=1) as response:
                if response.status == 200:
                    return
        except OSError:
            if time.monotonic() >= deadline:
                raise TimeoutError(f'{label} startup')
            time.sleep(0.1)


def curl_json(base_url, ca_cert, *, http2, token=TOKEN, origin=None):
    payload = '{"jsonrpc":"2.0","id":41,"method":"health"}'
    command = ['curl', '--silent', '--show-error', '--cacert', str(ca_cert),
               '--resolve', f'localhost:{base_url.rsplit(":", 1)[1]}:127.0.0.1',
               '--http2' if http2 else '--http1.1', '-H', 'Content-Type: application/json',
               '-H', 'Accept: application/json, text/event-stream', '-X', 'POST',
               '--data-binary', payload, '--write-out', '\n%{http_code} %{http_version}']
    if token is not None:
        command.extend(['-H', f'Authorization: Bearer {token}'])
    if origin is not None:
        command.extend(['-H', f'Origin: {origin}'])
    command.append(base_url + '/mcp')
    result = run_checked(command)
    body, status_line = result.stdout.rsplit('\n', 1)
    status, version = status_line.split()
    if not body.strip():
        parsed = None
    else:
        try:
            parsed = json.loads(body)
        except json.JSONDecodeError:
            parsed = body
    return int(status), version, parsed


def websocket_health(base_url, ca_cert):
    port = int(base_url.rsplit(':', 1)[1])
    context = ssl.create_default_context(cafile=str(ca_cert))
    context.set_alpn_protocols(['http/1.1'])
    with socket.create_connection(('127.0.0.1', port), timeout=5) as raw:
        with context.wrap_socket(raw, server_hostname='localhost') as connection:
            assert connection.selected_alpn_protocol() == 'http/1.1'
            key = base64.b64encode(os.urandom(16)).decode()
            request = '\r\n'.join([
                'GET /mcp HTTP/1.1', f'Host: localhost:{port}', 'Upgrade: websocket',
                'Connection: Upgrade', f'Sec-WebSocket-Key: {key}', 'Sec-WebSocket-Version: 13',
                'Sec-WebSocket-Protocol: mcp', f'Authorization: Bearer {TOKEN}',
                'Origin: https://client.test', '', '',
            ]).encode()
            connection.sendall(request)
            headers = bytearray()
            while b'\r\n\r\n' not in headers:
                headers.extend(connection.recv(4096))
            assert headers.startswith(b'HTTP/1.1 101 '), headers
            expected = base64.b64encode(hashlib.sha1(
                (key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').encode()).digest())
            assert b'sec-websocket-accept: ' + expected.lower() in bytes(headers).lower(), headers
            assert b'sec-websocket-protocol: mcp' in bytes(headers).lower(), headers

            payload = b'{"jsonrpc":"2.0","id":42,"method":"health"}'
            mask = os.urandom(4)
            frame = bytearray([0x81, 0x80 | len(payload)])
            frame.extend(mask)
            frame.extend(byte ^ mask[index % 4] for index, byte in enumerate(payload))
            connection.sendall(frame)
            first, second = receive_exact(connection, 2)
            assert first & 0x0f == 1 and not second & 0x80
            length = second & 0x7f
            if length == 126:
                length = struct.unpack('!H', receive_exact(connection, 2))[0]
            response = json.loads(receive_exact(connection, length))
            assert response['id'] == 42 and response['result']['status'] == 'healthy', response


def receive_exact(connection, length):
    data = bytearray()
    while len(data) < length:
        chunk = connection.recv(length - len(data))
        if not chunk:
            raise ConnectionError('unexpected end of TLS/WebSocket stream')
        data.extend(chunk)
    return bytes(data)


def stop(process):
    if process is None or process.poll() is not None:
        return
    process.terminate()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=5)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument('--dotnet-dll', type=Path)
    target.add_argument('--binary', type=Path)
    parser.add_argument('--nginx', default='nginx')
    args = parser.parse_args()
    executable = (args.binary or args.dotnet_dll).resolve()
    if not executable.is_file():
        parser.error(f'build the host first; executable not found: {executable}')
    command = [str(executable)] if args.binary else ['dotnet', str(executable)]
    backend_port, proxy_port = reserve_port(), reserve_port()

    with tempfile.TemporaryDirectory(prefix='request-guard-proxy-') as temporary:
        directory = Path(temporary)
        ca_cert, certificate, key = create_certificate(directory)
        config = write_nginx_config(directory, proxy_port, backend_port, certificate, key)
        environment = {**clean_environment(), 'MCP__HOST': '127.0.0.1',
                       'MCP__PORT': str(backend_port), 'AUTH_TOKENS': TOKEN,
                       'CACHE_SCOPE_HMAC_KEY': 'synthetic-proxy-cache-scope-key-32-bytes',
                       'MCP_ALLOWED_ORIGINS': 'https://client.test'}
        backend_log = open(directory / 'backend.log', 'w+')
        proxy_log = open(directory / 'proxy.log', 'w+')
        backend = proxy = None
        try:
            backend = subprocess.Popen(command, cwd=directory, env=environment,
                                       stdout=backend_log, stderr=subprocess.STDOUT)
            wait_for_health(f'http://127.0.0.1:{backend_port}', None, backend, 'backend')
            run_checked([args.nginx, '-t', '-p', str(directory), '-c', str(config)])
            proxy = subprocess.Popen([args.nginx, '-p', str(directory), '-c', str(config),
                                      '-g', 'daemon off;'], stdout=proxy_log,
                                     stderr=subprocess.STDOUT)
            tls_context = ssl.create_default_context(cafile=str(ca_cert))
            base_url = f'https://localhost:{proxy_port}'
            wait_for_health(base_url, tls_context, proxy, 'TLS proxy')

            untrusted = subprocess.run(['curl', '--silent', '--show-error', '--http2',
                                        '--resolve', f'localhost:{proxy_port}:127.0.0.1',
                                        base_url + '/health'], capture_output=True)
            assert untrusted.returncode == 60, untrusted.stderr.decode(errors='replace')

            with socket.create_connection(('127.0.0.1', proxy_port), timeout=5) as raw:
                context = ssl.create_default_context(cafile=str(ca_cert))
                context.minimum_version = ssl.TLSVersion.TLSv1_3
                context.set_alpn_protocols(['h2', 'http/1.1'])
                with context.wrap_socket(raw, server_hostname='localhost') as secured:
                    assert secured.version() == 'TLSv1.3', secured.version()
                    assert secured.selected_alpn_protocol() == 'h2', secured.selected_alpn_protocol()

            status, version, response = curl_json(base_url, ca_cert, http2=True,
                                                   origin='https://client.test')
            assert status == 200 and version == '2' and response['result']['status'] == 'healthy'
            status, version, response = curl_json(base_url, ca_cert, http2=False,
                                                   origin='https://client.test')
            assert status == 200 and version == '1.1' and response['result']['status'] == 'healthy'
            status, _, response = curl_json(base_url, ca_cert, http2=True, token=None,
                                             origin='https://client.test')
            assert status == 401 and not isinstance(response, dict), (status, response)
            status, _, response = curl_json(base_url, ca_cert, http2=True,
                                             origin='https://evil.test')
            assert status == 403 and not isinstance(response, dict), (status, response)
            websocket_health(base_url, ca_cert)

            with urllib.request.urlopen(f'http://127.0.0.1:{backend_port}/metrics', timeout=3) as response:
                metrics = response.read().decode()
            assert re.search(r'^mcp_requests_total\{tool="health",status="ok"\} 3$',
                             metrics, re.MULTILINE), metrics
            with urllib.request.urlopen(f'http://127.0.0.1:{backend_port}/health', timeout=3) as response:
                assert response.status == 200
            print('PASS TLS proxy: CA trust/rejection, TLS 1.3, ALPN h2, HTTP/1.1, auth/origin, WebSocket and backend recovery')
        except BaseException:
            for log in (backend_log, proxy_log):
                log.flush()
                log.seek(0)
                print(log.read(), file=sys.stderr)
            nginx_error = directory / 'nginx-error.log'
            if nginx_error.exists():
                print(nginx_error.read_text(errors='replace'), file=sys.stderr)
            raise
        finally:
            stop(proxy)
            stop(backend)
            backend_log.close()
            proxy_log.close()


if __name__ == '__main__':
    main()
