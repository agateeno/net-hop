# Installer

`installer/` builds a per-machine MSI with [WiX Toolset](https://wixtoolset.org) 6.

## What it installs

| Item | Where |
|---|---|
| App, service, CLI and a self-contained .NET runtime | `%ProgramFiles%\NetHop` (one folder, one runtime) |
| NetHop Windows service | Runs as LocalSystem, starts automatically, restarts after a crash (5 s) |
| `nethop.exe` on the system `PATH` | `%ProgramFiles%\NetHop` |
| `.nethop` file type and `nethop://` protocol | `HKLM\Software\Classes`, opened by `NetHop.App.exe` |
| Start menu shortcut | All users |
| PowerShell module (optional feature) | `%ProgramFiles%\WindowsPowerShell\Modules\NetHop` |

The app starts with Windows from the first time a user opens it (a per-user setting, which can be changed in Settings).

Profiles and rules live in `%ProgramData%\NetHop` and are **kept** on uninstall and upgrade.

Upgrades: every build has a higher version, and `MajorUpgrade` replaces the previous one. During the upgrade the tray app is closed, and the service is stopped and restarted.

## Building

The MSI is built on Windows by CI (artifact **NetHop-msi**). To build it locally:

```powershell
dotnet publish src\NetHop.Service -c Release -r win-x64 --self-contained -o out\bin
dotnet publish src\NetHop.Cli     -c Release -r win-x64 --self-contained -o out\bin
dotnet publish src\NetHop.App     -c Release -r win-x64 -p:Platform=x64 --self-contained -o out\bin
dotnet build installer\NetHop.Installer.wixproj -c Release -p:ProductVersion=0.1.0
```

## Why WiX 6 and not 7

WiX is open source, but from v6 organizations with more than USD 10,000 in yearly revenue must sponsor the project ([Open Source Maintenance Fee](https://docs.firegiant.com/wix/osmf/)). WiX 7 enforces this: every build must declare `AcceptEula=wix7`. Whether to accept that is the project owner's decision, so NetHop stays on WiX 6.0.2 for now. Moving to 7 means:

1. deciding whether the fee applies (or paying it),
2. setting `<AcceptEula>wix7</AcceptEula>` in `NetHop.Installer.wixproj` and bumping the SDK and extension versions.

## Code signing

The MSI and the executables are not signed yet, so SmartScreen will warn on download. Before a public release, sign them (for example with Azure Trusted Signing). The same certificate can sign the Power Automate module.

## Not covered yet

- ARM64 MSI (publish with `-r win-arm64` and set `InstallerPlatform=arm64`)
- Windows 11 modern context menu entry ("Apply profile" on `.nethop` files): needs a sparse package
- Winget manifest
