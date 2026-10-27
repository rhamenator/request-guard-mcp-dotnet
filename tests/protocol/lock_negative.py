#!/usr/bin/env python3
"""Prove native locked restore rejects a changed dependency before publication."""
from pathlib import Path
import subprocess
import tarfile
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
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
