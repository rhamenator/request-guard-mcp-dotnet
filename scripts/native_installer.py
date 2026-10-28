#!/usr/bin/env python3
"""Build a native OS installer from an already-qualified native bundle."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tarfile
import tempfile
import uuid
import xml.etree.ElementTree as ET


RIDS = ('linux-x64', 'win-x64', 'osx-x64', 'osx-arm64')
PACKAGE = 'request-guard-mcp-dotnet'
INSTALL_ROOT = 'request-guard-mcp-dotnet'
WIX_NAMESPACE = 'http://wixtoolset.org/schemas/v4/wxs'
UPGRADE_CODE = '9DB660E8-42B5-4AED-A52B-F8DCDE6AC0D2'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def run(command, **kwargs):
    print('+', ' '.join(map(str, command)))
    subprocess.run(list(map(str, command)), check=True, **kwargs)


def extract_verified(archive, rid, bundle_version, target):
    checksum = digest(archive)
    sidecar = archive.with_suffix(archive.suffix + '.sha256')
    if sidecar.read_text().strip() != f'{checksum}  {archive.name}':
        raise ValueError('native bundle checksum sidecar does not match')
    with tarfile.open(archive) as contents:
        members = contents.getmembers()
        top = set()
        for member in members:
            path = PurePosixPath(member.name)
            if (not path.parts or path.is_absolute() or '..' in path.parts or '\\' in member.name
                    or not (member.isfile() or member.isdir())):
                raise ValueError(f'unsafe archive member: {member.name}')
            top.add(path.parts[0])
        if len(top) != 1:
            raise ValueError('native bundle must have exactly one top-level directory')
        contents.extractall(target, filter='data')
    bundle = target / top.pop()
    metadata = json.loads((bundle / 'bundle-manifest.json').read_text())
    if metadata.get('schema') != 1 or metadata.get('rid') != rid or metadata.get('version') != bundle_version:
        raise ValueError('native bundle metadata does not match requested RID and bundle version')
    files = {path.relative_to(bundle).as_posix(): digest(path) for path in bundle.rglob('*')
             if path.is_file() and path.name != 'bundle-manifest.json'}
    if files != metadata.get('files'):
        raise ValueError('native bundle contents do not match its manifest')
    return bundle, metadata, checksum


def install_payload(bundle, root, prefix):
    destination = root / prefix.relative_to('/')
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(bundle, destination)
    executable = destination / ('request-guard-mcp-dotnet.exe' if os.name == 'nt' else 'request-guard-mcp-dotnet')
    if executable.exists() and os.name != 'nt':
        executable.chmod(0o755)
    return destination


def build_deb(bundle, version, output):
    with tempfile.TemporaryDirectory(prefix='request-guard-deb-') as directory:
        root = Path(directory) / 'root'
        install_payload(bundle, root, Path('/usr/lib') / INSTALL_ROOT)
        binary = root / 'usr/bin/request-guard-mcp-dotnet'
        binary.parent.mkdir(parents=True)
        binary.symlink_to(f'../lib/{INSTALL_ROOT}/request-guard-mcp-dotnet')
        control = root / 'DEBIAN/control'
        control.parent.mkdir(parents=True)
        control.write_text(
            f'Package: {PACKAGE}\nVersion: {version}\nArchitecture: amd64\n'
            'Maintainer: Request Guard MCP maintainers\n'
            'Section: utils\nPriority: optional\n'
            'Depends: libc6, libgcc-s1, libstdc++6, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67, '
            'libssl3t64 | libssl3, zlib1g, ca-certificates, tzdata\n'
            'Description: Policy decision service for MCP clients and agents\n')
        run(['dpkg-deb', '--root-owner-group', '--build', root, output])


def build_rpm(bundle, version, output):
    with tempfile.TemporaryDirectory(prefix='request-guard-rpm-') as directory:
        top = Path(directory)
        for name in ('BUILD', 'BUILDROOT', 'RPMS', 'SOURCES', 'SPECS', 'SRPMS'):
            (top / name).mkdir()
        payload = top / 'payload'
        install_payload(bundle, payload, Path('/usr/lib') / INSTALL_ROOT)
        binary = payload / 'usr/bin/request-guard-mcp-dotnet'
        binary.parent.mkdir(parents=True)
        binary.symlink_to(f'../lib/{INSTALL_ROOT}/request-guard-mcp-dotnet')
        source = top / 'SOURCES/payload.tar.gz'
        with tarfile.open(source, 'w:gz') as archive:
            archive.add(payload, arcname='payload')
        spec = top / 'SPECS/request-guard-mcp-dotnet.spec'
        spec.write_text(f'''%global debug_package %{{nil}}
%global __strip /bin/true
Name: {PACKAGE}
Version: {version}
Release: 1%{{?dist}}
Summary: Policy decision service for MCP clients and agents
License: MIT
Source0: payload.tar.gz
BuildArch: x86_64
Requires: glibc, libgcc, libstdc++, libicu, openssl-libs, zlib, ca-certificates, tzdata

%description
Request Guard MCP .NET is a policy decision service for MCP clients and agents.

%prep
%setup -q -c -T
tar -xzf %{{SOURCE0}}

%build

%install
mkdir -p %{{buildroot}}
cp -a payload/. %{{buildroot}}/

%files
/usr/bin/request-guard-mcp-dotnet
/usr/lib/{INSTALL_ROOT}
''')
        run(['rpmbuild', '--define', f'_topdir {top}', '-bb', spec])
        built = list((top / 'RPMS').rglob('*.rpm'))
        if len(built) != 1:
            raise RuntimeError(f'expected one RPM, found {built}')
        shutil.copyfile(built[0], output)


def build_msi(bundle, version, output, wix):
    ET.register_namespace('', WIX_NAMESPACE)
    q = lambda name: f'{{{WIX_NAMESPACE}}}{name}'
    wix_root = ET.Element(q('Wix'))
    package = ET.SubElement(wix_root, q('Package'), {
        'Name': 'Request Guard MCP .NET', 'Manufacturer': 'Request Guard MCP maintainers',
        'Version': version, 'UpgradeCode': UPGRADE_CODE, 'Scope': 'perMachine'})
    ET.SubElement(package, q('MajorUpgrade'), {
        'DowngradeErrorMessage': 'A newer version of Request Guard MCP .NET is already installed.'})
    ET.SubElement(package, q('MediaTemplate'), {'EmbedCab': 'yes'})
    standard = ET.SubElement(package, q('StandardDirectory'), {'Id': 'ProgramFiles64Folder'})
    install = ET.SubElement(standard, q('Directory'), {'Id': 'INSTALLFOLDER', 'Name': 'Request Guard MCP .NET'})
    directories = {Path('.'): install}
    references = []
    for index, path in enumerate(sorted(p for p in bundle.rglob('*') if p.is_file()), 1):
        relative = path.relative_to(bundle)
        parent = Path('.')
        node = install
        for part in relative.parent.parts:
            parent /= part
            if parent not in directories:
                directory_id = f'Dir_{uuid.uuid5(uuid.NAMESPACE_URL, parent.as_posix()).hex}'
                directories[parent] = ET.SubElement(node, q('Directory'), {'Id': directory_id, 'Name': part})
            node = directories[parent]
        component_id = f'Cmp_{index}'
        component = ET.SubElement(node, q('Component'), {'Id': component_id, 'Guid': '*'})
        ET.SubElement(component, q('File'), {
            'Id': f'File_{index}', 'Source': str(path.resolve()), 'Name': relative.name, 'KeyPath': 'yes'})
        references.append(component_id)
    feature = ET.SubElement(package, q('Feature'), {'Id': 'MainFeature', 'Title': 'Request Guard MCP .NET', 'Level': '1'})
    for component_id in references:
        ET.SubElement(feature, q('ComponentRef'), {'Id': component_id})
    source = output.with_suffix('.wxs')
    ET.indent(wix_root)
    ET.ElementTree(wix_root).write(source, encoding='utf-8', xml_declaration=True)
    run([wix, 'build', '-arch', 'x64', '-out', output, source])
    source.unlink()
    wix_symbols = output.with_suffix('.wixpdb')
    if wix_symbols.exists():
        wix_symbols.unlink()


def build_pkg(bundle, version, output):
    with tempfile.TemporaryDirectory(prefix='request-guard-pkg-') as directory:
        base = Path(directory)
        root = base / 'root'
        install_payload(bundle, root, Path('/usr/local/lib') / INSTALL_ROOT)
        binary = root / 'usr/local/bin/request-guard-mcp-dotnet'
        binary.parent.mkdir(parents=True)
        binary.symlink_to(f'/usr/local/lib/{INSTALL_ROOT}/request-guard-mcp-dotnet')
        uninstaller = root / f'usr/local/share/{INSTALL_ROOT}/uninstall.sh'
        uninstaller.parent.mkdir(parents=True)
        uninstaller.write_text(f'''#!/bin/sh
set -eu
rm -f /usr/local/bin/request-guard-mcp-dotnet
rm -rf /usr/local/lib/{INSTALL_ROOT}
rm -rf /usr/local/share/{INSTALL_ROOT}
/usr/sbin/pkgutil --forget com.rhamenator.request-guard-mcp-dotnet >/dev/null 2>&1 || true
''')
        uninstaller.chmod(0o755)
        run(['pkgbuild', '--root', root, '--identifier', 'com.rhamenator.request-guard-mcp-dotnet',
             '--version', version, '--install-location', '/', '--ownership', 'recommended', output])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--rid', choices=RIDS, required=True)
    parser.add_argument('--bundle-version', required=True)
    parser.add_argument('--package-version', required=True)
    parser.add_argument('--output-dir', type=Path, required=True)
    parser.add_argument('--wix', default='wix')
    args = parser.parse_args()
    version_pattern = r'\d+\.\d+\.\d+'
    if not re.fullmatch(version_pattern, args.bundle_version) or not re.fullmatch(version_pattern, args.package_version):
        parser.error('bundle and package versions must be numeric three-part versions')
    archive = args.archive.resolve()
    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    extension = {'linux-x64': None, 'win-x64': '.msi', 'osx-x64': '.pkg', 'osx-arm64': '.pkg'}[args.rid]
    with tempfile.TemporaryDirectory(prefix='request-guard-installer-') as directory:
        bundle, metadata, bundle_sha256 = extract_verified(
            archive, args.rid, args.bundle_version, Path(directory))
        outputs = []
        base = f'{PACKAGE}-{args.package_version}-{args.rid}'
        if args.rid == 'linux-x64':
            deb = output_dir / f'{base}.deb'
            rpm = output_dir / f'{base}.rpm'
            build_deb(bundle, args.package_version, deb)
            build_rpm(bundle, args.package_version, rpm)
            outputs.extend((deb, rpm))
        else:
            output = output_dir / f'{base}{extension}'
            if args.rid == 'win-x64':
                build_msi(bundle, args.package_version, output, args.wix)
            else:
                build_pkg(bundle, args.package_version, output)
            outputs.append(output)
    manifest = {
        'schema': 1, 'source_sha': metadata['source_sha'], 'rid': args.rid,
        'bundle_version': args.bundle_version, 'package_version': args.package_version,
        'bundle_artifact': archive.name, 'bundle_sha256': bundle_sha256,
        'payload_files': metadata['files'], 'unsigned': True,
        'installers': {path.name: digest(path) for path in outputs}}
    manifest_path = output_dir / 'installer-manifest.json'
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    for path in outputs:
        path.with_suffix(path.suffix + '.sha256').write_text(f'{digest(path)}  {path.name}\n')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
