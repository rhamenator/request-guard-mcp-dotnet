#!/usr/bin/env python3
"""Exercise clean install, launch, upgrade, uninstall, and residue checks."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]
PACKAGE = 'request-guard-mcp-dotnet'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def run(command, *, capture=False, **kwargs):
    print('+', ' '.join(map(str, command)), flush=True)
    return subprocess.run(list(map(str, command)), check=True, text=True,
                          stdout=subprocess.PIPE if capture else None, **kwargs)


def verify(directory, rid, version, source):
    metadata = json.loads((directory / 'installer-manifest.json').read_text())
    assert metadata['schema'] == 1 and metadata['rid'] == rid
    assert metadata['package_version'] == version and metadata['source_sha'] == source
    assert metadata['unsigned'] is True
    for name, expected in metadata['installers'].items():
        path = directory / name
        actual = digest(path)
        assert actual == expected
        assert path.with_suffix(path.suffix + '.sha256').read_text().strip() == f'{actual}  {name}'
    return metadata


def stdio(binary):
    run([sys.executable, ROOT / 'tests/stdio/run.py', '--binary', binary])


def verify_installed_payload(root):
    metadata = json.loads((root / 'bundle-manifest.json').read_text())
    files = {path.relative_to(root).as_posix(): digest(path) for path in root.rglob('*')
             if path.is_file() and path.name != 'bundle-manifest.json'}
    assert metadata['files'] == files, 'installed files differ from the qualified native bundle'


def qualify_deb(current_dir, prior_dir, version, prior):
    current = next(current_dir.glob('*.deb'))
    previous = next(prior_dir.glob('*.deb'))
    install_root = Path('/usr/lib/request-guard-mcp-dotnet')
    binary = Path('/usr/bin/request-guard-mcp-dotnet')
    assert not install_root.exists() and not binary.exists()
    try:
        run(['sudo', 'apt-get', 'update'])
        run(['sudo', 'apt-get', 'install', '-y', str(current)])
        assert run(['dpkg-query', '-W', '-f=${Version}', PACKAGE], capture=True).stdout == version
        verify_installed_payload(install_root)
        stdio(binary)
        run(['sudo', 'dpkg', '--purge', PACKAGE])
        assert not install_root.exists() and not binary.exists()

        run(['sudo', 'apt-get', 'install', '-y', str(previous)])
        assert run(['dpkg-query', '-W', '-f=${Version}', PACKAGE], capture=True).stdout == prior
        run(['sudo', 'apt-get', 'install', '-y', str(current)])
        assert run(['dpkg-query', '-W', '-f=${Version}', PACKAGE], capture=True).stdout == version
        verify_installed_payload(install_root)
        stdio(binary)
    finally:
        subprocess.run(['sudo', 'dpkg', '--purge', PACKAGE], check=False)
    assert not install_root.exists() and not binary.exists()


def qualify_rpm(current_dir, prior_dir, version, prior, image, executable_sha256):
    current = next(current_dir.glob('*.rpm')).resolve()
    previous = next(prior_dir.glob('*.rpm')).resolve()
    package_dir = current_dir.parent.resolve()
    current_inside = f'/packages/{current_dir.name}/{current.name}'
    prior_inside = f'/packages/{prior_dir.name}/{previous.name}'
    script = f'''set -euo pipefail
test ! -e /usr/bin/request-guard-mcp-dotnet
dnf -y install {current_inside}
test "$(rpm -q --qf '%{{VERSION}}' {PACKAGE})" = "{version}"
echo '{executable_sha256}  /usr/lib/request-guard-mcp-dotnet/request-guard-mcp-dotnet' | sha256sum -c
printf '{{"jsonrpc":"2.0","id":1,"method":"ping"}}\n' | timeout 20 /usr/bin/request-guard-mcp-dotnet --stdio | grep -F '"id":1'
dnf -y remove {PACKAGE}
test ! -e /usr/bin/request-guard-mcp-dotnet
test ! -e /usr/lib/request-guard-mcp-dotnet
! rpm -q {PACKAGE}

dnf -y install {prior_inside}
test "$(rpm -q --qf '%{{VERSION}}' {PACKAGE})" = "{prior}"
dnf -y install {current_inside}
test "$(rpm -q --qf '%{{VERSION}}' {PACKAGE})" = "{version}"
echo '{executable_sha256}  /usr/lib/request-guard-mcp-dotnet/request-guard-mcp-dotnet' | sha256sum -c
printf '{{"jsonrpc":"2.0","id":1,"method":"ping"}}\n' | timeout 20 /usr/bin/request-guard-mcp-dotnet --stdio | grep -F '"id":1'
dnf -y remove {PACKAGE}
test ! -e /usr/bin/request-guard-mcp-dotnet
test ! -e /usr/lib/request-guard-mcp-dotnet
! rpm -q {PACKAGE}
'''
    run(['docker', 'run', '--rm', '--cpus', '2', '--memory', '3g',
         '-v', f'{package_dir}:/packages:ro', image, 'bash', '-c', script])


def windows_installed_version():
    import winreg
    paths = (r'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
             r'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')
    for hive in (winreg.HKEY_LOCAL_MACHINE,):
        for path in paths:
            try:
                with winreg.OpenKey(hive, path) as root:
                    for index in range(winreg.QueryInfoKey(root)[0]):
                        with winreg.OpenKey(root, winreg.EnumKey(root, index)) as key:
                            try:
                                if winreg.QueryValueEx(key, 'DisplayName')[0] == 'Request Guard MCP .NET':
                                    return winreg.QueryValueEx(key, 'DisplayVersion')[0]
                            except FileNotFoundError:
                                pass
            except FileNotFoundError:
                pass
    return None


def qualify_msi(current_dir, prior_dir, version, prior):
    current = next(current_dir.glob('*.msi')).resolve()
    previous = next(prior_dir.glob('*.msi')).resolve()
    install_root = Path(os.environ['ProgramFiles']) / 'Request Guard MCP .NET'
    binary = install_root / 'request-guard-mcp-dotnet.exe'
    assert not install_root.exists() and windows_installed_version() is None
    log = current_dir / 'msi-install.log'
    try:
        run(['msiexec.exe', '/i', current, '/qn', '/norestart', '/L*v', log])
        assert windows_installed_version() == version
        verify_installed_payload(install_root)
        stdio(binary)
        run(['msiexec.exe', '/x', current, '/qn', '/norestart', '/L*v', log])
        assert not install_root.exists() and windows_installed_version() is None

        run(['msiexec.exe', '/i', previous, '/qn', '/norestart', '/L*v', log])
        assert windows_installed_version() == prior
        run(['msiexec.exe', '/i', current, '/qn', '/norestart', '/L*v', log])
        assert windows_installed_version() == version
        verify_installed_payload(install_root)
        stdio(binary)
    finally:
        subprocess.run(['msiexec.exe', '/x', current, '/qn', '/norestart', '/L*v', log], check=False)
    assert not install_root.exists() and windows_installed_version() is None


def pkg_version():
    result = subprocess.run(['/usr/sbin/pkgutil', '--pkg-info-plist', 'com.rhamenator.request-guard-mcp-dotnet'],
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    if result.returncode:
        return None
    import plistlib
    return plistlib.loads(result.stdout.encode())['pkg-version']


def qualify_pkg(current_dir, prior_dir, version, prior):
    current = next(current_dir.glob('*.pkg')).resolve()
    previous = next(prior_dir.glob('*.pkg')).resolve()
    install_root = Path('/usr/local/lib/request-guard-mcp-dotnet')
    binary = Path('/usr/local/bin/request-guard-mcp-dotnet')
    uninstaller = Path('/usr/local/share/request-guard-mcp-dotnet/uninstall.sh')
    assert not install_root.exists() and not binary.exists() and pkg_version() is None
    try:
        run(['sudo', '/usr/sbin/installer', '-pkg', current, '-target', '/'])
        assert pkg_version() == version
        verify_installed_payload(install_root)
        stdio(binary)
        run(['sudo', uninstaller])
        assert not install_root.exists() and not binary.exists() and not uninstaller.exists() and pkg_version() is None

        run(['sudo', '/usr/sbin/installer', '-pkg', previous, '-target', '/'])
        assert pkg_version() == prior
        run(['sudo', '/usr/sbin/installer', '-pkg', current, '-target', '/'])
        assert pkg_version() == version
        verify_installed_payload(install_root)
        stdio(binary)
    finally:
        if uninstaller.exists():
            subprocess.run(['sudo', uninstaller], check=False)
    assert not install_root.exists() and not binary.exists() and not uninstaller.exists() and pkg_version() is None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--installer-dir', type=Path, required=True)
    parser.add_argument('--prior-dir', type=Path, required=True)
    parser.add_argument('--rid', required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--prior-version', required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--rpm-image', default='fedora:42')
    args = parser.parse_args()
    current_dir = args.installer_dir.resolve()
    prior_dir = args.prior_dir.resolve()
    metadata = verify(current_dir, args.rid, args.version, args.source)
    verify(prior_dir, args.rid, args.prior_version, args.source)
    checks = {'manifest_and_checksums': 'passed', 'installed_payload': 'passed', 'clean_install': 'passed', 'launch': 'passed',
              'upgrade': 'passed', 'uninstall': 'passed', 'residue': 'passed'}
    if args.rid == 'linux-x64':
        assert platform.system() == 'Linux'
        qualify_deb(current_dir, prior_dir, args.version, args.prior_version)
        qualify_rpm(current_dir, prior_dir, args.version, args.prior_version, args.rpm_image,
                    metadata['payload_files']['request-guard-mcp-dotnet'])
        checks['deb'] = checks['rpm'] = 'passed'
    elif args.rid == 'win-x64':
        assert os.name == 'nt'
        qualify_msi(current_dir, prior_dir, args.version, args.prior_version)
        checks['msi'] = 'passed'
    else:
        assert platform.system() == 'Darwin'
        qualify_pkg(current_dir, prior_dir, args.version, args.prior_version)
        checks['pkg'] = 'passed'
    report = {'schema': 1, 'source_sha': args.source, 'rid': args.rid,
              'version': args.version, 'prior_version': args.prior_version,
              'unsigned': True, 'installers': metadata['installers'], 'checks': checks,
              'scope': 'clean candidate install/launch/uninstall/residue, then separate synthetic prior-version upgrade/uninstall/residue'}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n')
    print('PASS installer lifecycle:', args.rid)


if __name__ == '__main__':
    main()
