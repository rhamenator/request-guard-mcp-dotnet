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
    args = parser.parse_args()
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
        if os.name != 'nt':
            executable.chmod(0o755)
        environment = {**os.environ, 'DOTNET_ROOT': str(target / 'no-runtime'), 'DOTNET_ROOT_X64': str(target / 'no-runtime'),
                       'DOTNET_ROOT_ARM64': str(target / 'no-runtime'), 'DOTNET_MULTILEVEL_LOOKUP': '0'}
        checks = {}
        for name, runner, extra in [('http_ws_sdk', 'protocol', []),
                                    ('configured_origin_sdk', 'protocol', ['--allowed-origin', 'https://client.test']),
                                    ('stdio_sdk_process', 'stdio', [])]:
            completed = subprocess.run([sys.executable, str(ROOT / 'tests' / runner / 'run.py'), '--binary', str(executable), *extra],
                                       env=environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=90)
            print(completed.stdout, end='')
            assert completed.returncode == 0, (name, completed.stdout)
            checks[name] = 'passed'
        report = {'schema': 1, 'artifact': archive.name, 'sha256': checksum, 'source_sha': metadata['source_sha'],
                  'rid': metadata['rid'], 'sdk_version': metadata['sdk_version'], 'checks': checks,
                  'scope': 'fresh extraction, process-local stdio and authenticated JSON HTTP/custom WebSocket; no external backends'}
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + '\n')
        print('PASS native bundle: checksums/provenance, fresh extraction, HTTP/WS and stdio official SDK')


if __name__ == '__main__':
    main()
