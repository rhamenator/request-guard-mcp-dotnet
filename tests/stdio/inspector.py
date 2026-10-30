#!/usr/bin/env python3
"""Qualify the exact MCP Inspector CLI against a running stdio server."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess


INSPECTOR_VERSION = '2.13.0'
INSPECTOR_PACKAGE = f'@modelcontextprotocol/inspector@{INSPECTOR_VERSION}'
INSPECTOR_INTEGRITY = 'sha512-k+eCnlMFWTVRops5+vGljX74f+eb7XsvTCB3ODWuEKXhIwKL2cLcnwsO3vOfXIp0gczjc1kzl8lS5+cwvGPYdA=='
EXPECTED_TOOLS = {
    'abuse_pattern_match', 'batch_classify', 'calibration_report', 'canary_eval',
    'classify', 'config_snapshot', 'drift_report', 'enrich_asn', 'enrich_ip',
    'enrich_ua', 'explain', 'feature_flags', 'feedback', 'health', 'model_info',
    'queue_status', 'redact_preview', 'replay_decision', 'score_breakdown',
    'self_test', 'threat_lookup', 'validate_payload', 'warmup',
}


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


def require_node():
    result = subprocess.run(['node', '--version'], check=True, capture_output=True, text=True)
    match = re.fullmatch(r'v(\d+)\.(\d+)\.(\d+)\s*', result.stdout)
    if not match or tuple(map(int, match.groups())) < (22, 19, 0):
        raise RuntimeError(f'Inspector {INSPECTOR_VERSION} requires Node >=22.19.0; found {result.stdout.strip()}')


def verify_package_integrity():
    result = subprocess.run(
        ['npm', 'view', INSPECTOR_PACKAGE, 'dist.integrity', '--json'],
        capture_output=True, text=True, timeout=30)
    if result.returncode:
        raise RuntimeError(
            f'could not resolve {INSPECTOR_PACKAGE}: stdout={result.stdout!r}; '
            f'stderr={result.stderr!r}')
    assert json.loads(result.stdout) == INSPECTOR_INTEGRITY, result.stdout


def inspect(server_command, *arguments):
    result = subprocess.run(
        ['npx', '--yes', INSPECTOR_PACKAGE, '--cli', *server_command, *arguments,
         '--format', 'json'],
        capture_output=True, text=True, timeout=90, env=clean_environment())
    if result.returncode:
        raise RuntimeError(
            f'Inspector failed with status {result.returncode}: stdout={result.stdout!r}; '
            f'stderr={result.stderr!r}')
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError as error:
        raise AssertionError(f'Inspector emitted non-JSON stdout: {result.stdout!r}; stderr={result.stderr!r}') from error


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument('--dotnet-dll', type=Path)
    target.add_argument('--binary', type=Path)
    args = parser.parse_args()
    executable = (args.binary or args.dotnet_dll).resolve()
    if not executable.is_file():
        parser.error(f'build the host first; executable not found: {executable}')
    server_command = ([str(executable), '--stdio'] if args.binary else
                      ['dotnet', str(executable), '--stdio'])

    require_node()
    verify_package_integrity()

    listed = inspect(server_command, '--method', 'tools/list')
    tools = listed['result']['tools']
    names = {tool['name'] for tool in tools}
    assert names == EXPECTED_TOOLS and len(tools) == 23, names
    assert all(tool.get('description') and tool.get('inputSchema', {}).get('type') == 'object'
               for tool in tools), tools

    health = inspect(server_command, '--method', 'tools/call', '--tool-name', 'health')
    health_result = health['result']
    assert health_result['isError'] is False, health_result
    assert health_result['structuredContent']['status'] == 'healthy', health_result
    assert json.loads(health_result['content'][0]['text']) == health_result['structuredContent']

    classified = inspect(
        server_command, '--method', 'tools/call', '--tool-name', 'classify',
        '--tool-arg', 'user_agent=GPTBot/1.0', '--tool-arg', 'request_id=inspector-smoke')
    classify_result = classified['result']
    structured = classify_result['structuredContent']
    assert classify_result['isError'] is False, classify_result
    assert structured['request_id'] == 'inspector-smoke', structured
    assert structured['verdict'] == 'block' and structured['threat_category'] == 'ai_scraping', structured
    assert 0 <= structured['score'] <= 1, structured
    assert any(signal['name'] == 'ua_ai_bot' for signal in structured['signals']), structured
    assert json.loads(classify_result['content'][0]['text']) == structured

    print(f'PASS MCP Inspector {INSPECTOR_VERSION}: 23 tools, health and structured classify over stdio')


if __name__ == '__main__':
    main()
