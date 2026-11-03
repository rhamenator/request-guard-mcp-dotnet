#!/usr/bin/env python3
"""Validate and test an extracted native bundle, retaining exact artifact provenance."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import sys
import tarfile
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--expected-rid', required=True)
    parser.add_argument('--expected-source', required=True)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--endurance-seconds', type=int, default=120)
    args = parser.parse_args()
    if args.endurance_seconds < 1:
        parser.error('--endurance-seconds must be positive')
    archive = args.archive.resolve()
    checksum = digest(archive)
    assert archive.with_suffix(archive.suffix + '.sha256').read_text().strip() == f'{checksum}  {archive.name}'
    with tempfile.TemporaryDirectory(prefix='request-guard-install-') as directory:
        target = Path(directory)
        with tarfile.open(archive) as contents:
            members = contents.getmembers()
            top = set()
            for member in members:
                path = PurePosixPath(member.name)
                assert path.parts and not path.is_absolute() and '..' not in path.parts and '\\' not in member.name, member.name
                assert member.isfile() or member.isdir(), member.name
                top.add(path.parts[0])
            assert len(top) == 1, top
            contents.extractall(target, filter='data')
        bundle = target / top.pop()
        metadata = json.loads((bundle / 'bundle-manifest.json').read_text())
        assert metadata['schema'] == 1 and metadata['source_sha'] == args.expected_source and metadata['rid'] == args.expected_rid, metadata
        files = {path.relative_to(bundle).as_posix(): digest(path) for path in bundle.rglob('*')
                 if path.is_file() and path.name != 'bundle-manifest.json'}
        assert metadata['files'] == files, 'bundle manifest/file checksums differ'
        executable = bundle / ('request-guard-mcp-dotnet.exe' if args.expected_rid == 'win-x64' else 'request-guard-mcp-dotnet')
        if args.expected_rid == 'win-x64':
            aspnet_core = ET.parse(bundle / 'web.config').find('./location/system.webServer/aspNetCore')
            assert aspnet_core is not None, 'IIS web.config lacks the ASP.NET Core handler configuration'
            assert aspnet_core.get('hostingModel', '').lower() == 'outofprocess', aspnet_core.attrib
            assert aspnet_core.get('processPath') == r'.\request-guard-mcp-dotnet.exe', aspnet_core.attrib
        if os.name != 'nt':
            executable.chmod(0o755)
        environment = {**os.environ, 'DOTNET_ROOT': str(target / 'no-runtime'), 'DOTNET_ROOT_X64': str(target / 'no-runtime'),
                       'DOTNET_ROOT_ARM64': str(target / 'no-runtime'), 'DOTNET_MULTILEVEL_LOOKUP': '0'}
        checks = {}
        runners = [
            ('http_ws_sdk', 'protocol', [], 90),
            ('configured_origin_sdk', 'protocol',
             ['--allowed-origin', 'https://client.test', '--ws-deadline-seconds', '1'], 90),
            ('telemetry_resource_endurance', 'protocol',
             ['--telemetry-endurance-seconds', str(args.endurance_seconds)],
             args.endurance_seconds + 60),
            ('stdio_sdk_process', 'stdio', [], 90),
        ]
        for name, runner, extra, timeout in runners:
            completed = subprocess.run([sys.executable, str(ROOT / 'tests' / runner / 'run.py'), '--binary', str(executable), *extra],
                                       env=environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
            print(completed.stdout, end='')
            assert completed.returncode == 0, (name, completed.stdout)
            checks[name] = 'passed'
        if args.expected_rid == 'win-x64':
            checks['iis_out_of_process_configuration'] = 'passed'
        report = {'schema': 1, 'artifact': archive.name, 'sha256': checksum, 'source_sha': metadata['source_sha'],
                  'rid': metadata['rid'], 'sdk_version': metadata['sdk_version'],
                  'telemetry_resource_endurance_seconds': args.endurance_seconds, 'checks': checks,
                  'scope': 'fresh extraction, process-local stdio, authenticated JSON HTTP/custom WebSocket and bounded telemetry/resource endurance; no external backends'}
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + '\n')
        print('PASS native bundle: checksums/provenance, fresh extraction, HTTP/WS, stdio official SDK '
              f'and {args.endurance_seconds}s telemetry/resource endurance')


if __name__ == '__main__':
    main()
