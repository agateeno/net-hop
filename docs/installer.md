# Installer

`installer/` builds a per-machine MSI with [WiX Toolset](https://wixtoolset.org) 6.

## What it installs

| Item | Where |
|---|---|
| App, service, CLI and a self-contained .NET runtime | `%ProgramFiles%\NetHop` (one folder, one runtime) |
| NetHop Windows service | Runs as LocalSystem, starts automatically, restarts after a crash (5 s) |
| `nethop.exe` on the system `PATH` | `%ProgramFiles%\NetHop` |
| `.nethop` file type (with its own icon) and `nethop://` protocol | `HKLM\Software\Classes`, opened by `NetHop.App.exe` |
| **Apply with NetHop** on `.nethop` files | Explorer context menu (Windows 11: under *Show more options*) |
| Start menu shortcut | All users |
| PowerShell module (optional feature) | `%ProgramFiles%\WindowsPowerShell\Modules\NetHop` |

The last page of the installer offers **Launch NetHop** (checked). On its first launch for each user, the app:

- starts with Windows from then on (can be changed in Settings);
- creates **`%USERPROFILE%\NetHop`** with the NetHop icon, pins it to **Quick Access**, and keeps a copy of every profile there as a `.nethop` file (double-click to edit, right-click › *Apply with NetHop* to apply). The originals stay in `%ProgramData%\NetHop`; files you add to the folder yourself are never overwritten.

These are per-user, so they happen when each user first opens NetHop, not only for the user who installed it.

On **uninstall**, NetHop removes its start-with-Windows entry and asks whether to delete `%USERPROFILE%\NetHop` as well (No is the default). Yes deletes the folder with everything in it, removes it from Quick Access and clears NetHop's per-user settings. Silent uninstalls (`msiexec /qn`, `winget uninstall`) and upgrades never ask and keep the folder. The question is asked to the user running the uninstall; other users' folders stay.

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

MSIs are built for **x64 and ARM64** (`-p:InstallerPlatform=arm64` with binaries published for `win-arm64`).

## Code signing

Unsigned MSIs trigger SmartScreen warnings, and Power Automate refuses unsigned custom actions. CI signs everything with [Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/) as soon as these repository secrets exist (*Settings › Secrets and variables › Actions*):

| Secret | Value |
|---|---|
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` | An app registration with the *Trusted Signing Certificate Profile Signer* role |
| `AZURE_SIGNING_ENDPOINT` | The account's endpoint, e.g. `https://weu.codesigning.azure.net/` |
| `AZURE_SIGNING_ACCOUNT` | The Trusted Signing account name |
| `AZURE_SIGNING_PROFILE` | The certificate profile name |

Without them CI still builds everything, unsigned. Only NetHop's own files are signed (`NetHop*.exe/dll`, `nethop.exe`, `Modules.NetHop.dll`, the MSIs and `NetHop.cab`); the .NET and Windows App SDK files already carry Microsoft's signature.

Trusted Signing needs an Azure subscription and identity validation (individual developers included); it costs a few euros a month. Any other code-signing certificate works with `signtool` too, for example through `scripts/Build-PowerAutomateModule.ps1 -CertificatePath`.

## Releases and winget

Pushing a tag such as `v0.2.0` builds version 0.2.0, creates a GitHub release with both MSIs and `NetHop.cab`, and produces the **winget-manifests** artifact (`scripts/New-WingetManifest.ps1`). To publish on winget, submit that folder to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs), for example with `wingetcreate submit`. After the first approval, users install NetHop with:

```powershell
winget install agateeno.NetHop
```

## Windows 11 context menu

**Apply with NetHop** is a classic shell verb, so Windows 11 shows it under *Show more options*. A first-level entry in the new menu needs an `IExplorerCommand` handler declared in a signed sparse package: it will come once releases are signed.
