#!/usr/bin/env python3
"""Prove native locked restore rejects a changed dependency before publication."""
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
for path in (ROOT / 'src').glob('*/packages.release.lock.json'):
    release = json.loads(path.read_text())['dependencies']
    normal = json.loads(path.with_name('packages.lock.json').read_text())['dependencies']['net10.0']
    assert set(release) == {'net10.0', 'net10.0/linux-x64', 'net10.0/win-x64', 'net10.0/osx-x64', 'net10.0/osx-arm64'}
    packages = dict(release['net10.0'])
    linker = packages.pop('Microsoft.NET.ILLink.Tasks')
    assert linker['resolved'] and linker['contentHash']
    assert packages == normal, f'release/normal package graphs drifted: {path}'
print('PASS native lock inventory: four RIDs; existing packages/hashes unchanged; single-file task pinned')
with tempfile.TemporaryDirectory(prefix='native-lock-negative-') as directory:
    work = Path(directory)
    archive = work / 'tracked.tar'
    subprocess.run(['git', 'archive', '--output', str(archive), 'HEAD'], cwd=ROOT, check=True)
    checkout = work / 'source'
    checkout.mkdir()
    with tarfile.open(archive) as contents:
        contents.extractall(checkout, filter='data')
    path = checkout / 'Directory.Packages.props'
    document = ET.parse(path)
    selected = [item for item in document.iter('PackageVersion') if item.get('Include') == 'OpenTelemetry.Extensions.Hosting']
    assert len(selected) == 1
    selected[0].set('Version', '0.0.1')
    document.write(path, encoding='unicode')
    result = subprocess.run(['dotnet', 'restore', 'src/RequestGuardMcp.Host/RequestGuardMcp.Host.csproj',
                             '-p:ReleaseBundle=true', '--locked-mode', '--disable-parallel'],
                            cwd=checkout, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=90)
    assert result.returncode != 0 and 'NU1004' in result.stdout, result.stdout
    print('PASS negative control: native locked restore rejects dependency drift (NU1004)')
