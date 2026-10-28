# Native Installers

Official release candidates attach unsigned native installers built from the same checksum-verified
bundle that passed the matching-platform protocol suite. They do not register an operating-system
service or create a network listener automatically. Configure environment secrets and launch the
installed executable directly, normally with `--stdio` for a subprocess MCP client.

## Linux x64

The release includes both a DEB and an RPM. They install the payload under
`/usr/lib/request-guard-mcp-dotnet` and the launcher at `/usr/bin/request-guard-mcp-dotnet`.
Use the package manager, rather than `dpkg -i` or `rpm -i`, so ICU, OpenSSL, zlib, time-zone data
and the other native runtime dependencies are resolved:

```bash
sudo apt install ./request-guard-mcp-dotnet-VERSION-linux-x64.deb
sudo dnf install ./request-guard-mcp-dotnet-VERSION-linux-x64.rpm
request-guard-mcp-dotnet --stdio
```

Uninstall with `sudo apt purge request-guard-mcp-dotnet` or
`sudo dnf remove request-guard-mcp-dotnet`. The supported Linux target is glibc x64; Alpine/musl
and arm64 are not currently built.

## Windows x64

Run `request-guard-mcp-dotnet-VERSION-win-x64.msi` interactively, or install silently from an
elevated terminal:

```powershell
msiexec.exe /i .\request-guard-mcp-dotnet-VERSION-win-x64.msi /qn /norestart
& "$env:ProgramFiles\Request Guard MCP .NET\request-guard-mcp-dotnet.exe" --stdio
```

Remove it through Installed Apps or with `msiexec.exe /x` and the same MSI. The installer does not
modify `PATH`; configure clients with the absolute executable path.

## macOS Intel and Apple silicon

Choose the `osx-x64.pkg` build for Intel or `osx-arm64.pkg` for Apple silicon:

```bash
sudo installer -pkg request-guard-mcp-dotnet-VERSION-osx-ARCH.pkg -target /
/usr/local/bin/request-guard-mcp-dotnet --stdio
```

The payload is under `/usr/local/lib/request-guard-mcp-dotnet`. Uninstall with the installed
`/usr/local/share/request-guard-mcp-dotnet/uninstall.sh` script using `sudo`. These packages are not
Apple-notarized, so macOS security policy can require an explicit local exception.

## Integrity and qualification

Every installer has a sibling SHA-256 file and a machine-readable manifest linking its hash to the
source revision and qualified native-bundle hash. The matching hosted runner checks a clean install,
installed payload integrity, and the installed executable through the pinned official Python MCP SDK
after both clean install and a synthetic older-package upgrade, followed by uninstall and residue.
Linux runs the official-SDK check through the DEB and a raw stdio RPM check in a digest-pinned Fedora
42 container. Synthetic older packages are test fixtures and are never release assets.

The packages are currently unsigned because commercial signing/notarization is deferred. SHA-256
records detect accidental changes but are not a substitute for publisher identity or platform code
signing. Confirm the release tag, source revision, workflow result and published checksum before use.
