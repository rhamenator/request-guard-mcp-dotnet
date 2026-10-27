#!/usr/bin/env python3
"""Build a locked native development bundle from exactly the tracked Git revision."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parents[1]
RIDS = ('linux-x64', 'win-x64', 'osx-x64', 'osx-arm64')
DOCS = ('runbook.md', 'security-model.md', 'VERIFICATION.md', 'KNOWN-LIMITATIONS.md')


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid', choices=RIDS, required=True)
    parser.add_argument('--version', default='0.1.0-dev')
    parser.add_argument('--output-dir', type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?', args.version):
        parser.error('version must be a three-part version with an optional prerelease suffix')
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    name = f'request-guard-mcp-dotnet-{args.version}-{args.rid}'
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    archive = output / f'{name}.tar.gz'
    if archive.exists():
        parser.error(f'refusing to overwrite existing bundle: {archive}')
    with tempfile.TemporaryDirectory(prefix='request-guard-bundle-') as directory:
        work = Path(directory)
        tracked = work / 'source.tar'
        subprocess.run(['git', 'archive', '--format=tar', '--output', str(tracked), source], cwd=ROOT, check=True)
        checkout = work / 'source'
        checkout.mkdir()
        with tarfile.open(tracked) as contents:
            contents.extractall(checkout, filter='data')
        bundle = work / name
        environment = {**os.environ, 'DOTNET_NOLOGO': 'true', 'DOTNET_CLI_TELEMETRY_OPTOUT': 'true'}
        subprocess.run(['dotnet', 'publish', 'src/RequestGuardMcp.Host/RequestGuardMcp.Host.csproj',
                        '--configuration', 'Release', '--runtime', args.rid, '--output', str(bundle),
                        '-p:ReleaseBundle=true', '-p:RestoreLockedMode=true', f'-p:Version={args.version}', '-m:2'],
                       cwd=checkout, env=environment, check=True)
        executable = bundle / ('request-guard-mcp-dotnet.exe' if args.rid == 'win-x64' else 'request-guard-mcp-dotnet')
        if not executable.is_file():
            raise AssertionError('native executable missing')
        for forbidden in ['appsettings.Development.json', 'packages.release.lock.json']:
            if (bundle / forbidden).exists():
                raise AssertionError(f'development artifact in bundle: {forbidden}')
        if list(bundle.glob('*.pdb')):
            raise AssertionError('debug symbols in native bundle')
        for file in ['.env.example', 'README.md', 'LICENSE', 'SECURITY.md']:
            shutil.copyfile(checkout / file, bundle / file)
        (bundle / 'docs').mkdir()
        for file in DOCS:
            shutil.copyfile(checkout / 'docs' / file, bundle / 'docs' / file)
        files = {path.relative_to(bundle).as_posix(): digest(path) for path in sorted(bundle.rglob('*')) if path.is_file()}
        metadata = {'schema': 1, 'source_sha': source, 'version': args.version, 'rid': args.rid,
                    'sdk_version': json.loads((checkout / 'global.json').read_text())['sdk']['version'], 'files': files}
        (bundle / 'bundle-manifest.json').write_text(json.dumps(metadata, indent=2) + '\n')
        with tarfile.open(archive, 'w:gz') as contents:
            contents.add(bundle, arcname=name)
    checksum = digest(archive)
    archive.with_suffix(archive.suffix + '.sha256').write_text(f'{checksum}  {archive.name}\n')
    print(json.dumps({'bundle': archive.name, 'sha256': checksum, 'source_sha': source, 'rid': args.rid}))


if __name__ == '__main__':
    main()
