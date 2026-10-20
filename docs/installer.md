# Installer

`installer/` builds a per-machine MSI with [WiX Toolset](https://wixtoolset.org) 6.

## What it installs

| Item | Where |
|---|---|
| App, service, CLI and a self-contained .NET runtime | `%ProgramFiles%\NetHop` (one folder, one runtime) |
| NetHop Windows service | Runs as LocalSystem, starts automatically, restarts after a crash (5 s) |
| `nethop.exe` on the system `PATH` | `%ProgramFiles%\NetHop` |
| `.nethop` file type (with its own icon) and `nethop://` protocol | `HKLM\Software\Classes`, opened by `NetHop.App.exe` |
| **Apply with NetHop** on `.nethop` files | Explorer context menu: first level on Windows 11 (see below), and under *Show more options* |
| Start menu shortcut | All users |
| PowerShell module (optional feature) | `%ProgramFiles%\WindowsPowerShell\Modules\NetHop` |

The last page of the installer offers **Launch NetHop** (checked). On its first launch for each user, the app:

- starts with Windows from then on (can be changed in Settings);
- creates **`%USERPROFILE%\NetHop Profiles`** with the NetHop icon, pins it to **Quick Access**, and keeps a copy of every profile there as a `.nethop` file (double-click to edit, right-click › *Apply with NetHop* to apply). The originals stay in `%ProgramData%\NetHop`; files you add to the folder yourself are never overwritten. A `%USERPROFILE%\NetHop` folder left by version 0.1.8 or earlier is renamed to the new name, and its Quick Access pin moved with it.

These are per-user, so they happen when each user first opens NetHop, not only for the user who installed it.

On **uninstall**, NetHop removes its start-with-Windows entry and asks whether to delete `%USERPROFILE%\NetHop Profiles` as well (No is the default). Yes deletes the folder with everything in it, removes it from Quick Access and clears NetHop's per-user settings. Silent uninstalls (`msiexec /qn`, `winget uninstall`) and upgrades never ask and keep the folder. The question is asked to the user running the uninstall; other users' folders stay.

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

Releases are written by hand: on GitHub, *Releases › Draft a new release*, create the tag (`v0.2.0` builds version 0.2.0) on `main`, write the notes and publish. The tag starts CI, which attaches both MSIs and `NetHop.cab` to that release without touching its text or its pre-release flag (mark unsigned releases as pre-releases yourself), and produces the **winget-manifests** artifact (`scripts/New-WingetManifest.ps1`). To publish on winget, submit that folder to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs), for example with `wingetcreate submit`. After the first approval, users install NetHop with:

```powershell
winget install agateeno.NetHop
```

## Windows 11 context menu

Windows 11 shows first-level context menu entries only for handlers declared by a package. NetHop ships:

- `NetHop.ShellExtension.dll`, a small native `IExplorerCommand` handler (`src/NetHop.ShellExtension`). Explorer loads it in-process; it shows **Apply with NetHop** (*Applica con NetHop* in Italian) when one `.nethop` file is selected and starts `NetHop.App.exe --apply <file>`.
- `NetHop.ContextMenu.msix`, a sparse package (manifest and logos only) whose content is the install folder. It declares the handler for `.nethop` files.

Packages are registered per user, so NetHop registers it for the current user when it starts, and again after an upgrade (the package version is the product version). Uninstalling removes it for the user running the uninstall. Failures are written to `%LOCALAPPDATA%\NetHop\crash.log`. The classic verb stays under *Show more options* and works everywhere.

**Signing.** Windows installs the package only if it is signed by a certificate the machine trusts **and** its Publisher equals the certificate's subject. Set the repository variable `PACKAGE_PUBLISHER` (*Settings › Secrets and variables › Actions › Variables*) to the exact subject of the Trusted Signing certificate, for example `CN=Jane Doe, O=Jane Doe, L=Rome, S=Lazio, C=IT`; CI then builds and signs the package with it. Without it (or without signing), the package uses Windows' unsigned-package publisher (`OID.2.25.311729368913984317654407730594956997722=1`) and NetHop registers it with *AllowUnsigned*, which Windows accepts only where unsigned packages are allowed; elsewhere only the classic entry appears.

Build it locally with `scripts\Build-ContextMenuPackage.ps1` (needs Visual Studio with the C++ tools and the Windows SDK).

## Troubleshooting

If `NetHop.App.exe` closes right after starting, the reason is written to `%LOCALAPPDATA%\NetHop\crash.log` (Event Viewer only shows `0xc000027b` in `Microsoft.UI.Xaml.dll`). The service logs to the Application event log under the source *NetHop*.
