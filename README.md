<p align="center">
  <img src="assets/icon/NetHop.svg" width="128" height="128" alt="NetHop icon">
</p>

<h1 align="center">NetHop</h1>

**Hop between network configurations in one click.** NetHop is an open-source network profile switcher for Windows 11, in the spirit of NetSetMan, with a PowerToys-style Fluent UI.

> Status: early development (v0.1). Not tested on real hardware yet.

## Features

- **Profiles** enable/disable adapters and set IPv4 **and** IPv6: DHCP or static, with multiple addresses, gateways and DNS servers.
- **Minimal changes**: NetHop compares the profile with the live configuration and only changes what differs. A failed step rolls back the adapters it touched.
- **Plain files**: profiles are YAML documents with the `.nethop` extension. Double-click one to import it.
- **Tray flyout**: left-click the notification-area icon to see the active profile and switch with one click.
- **Built-in automations**: rules such as "when I join the Wi-Fi `CORP-WiFi` on a weekday, apply *Office*", with no extra software. Loop protection, cooldowns and a pause switch are included.
- **Power Automate**: a *NetHop* group of custom actions in Power Automate Desktop, a PowerShell module, and a CLI with JSON output. NetHop rules can also start flows.
- **Logitech**: a Logi Actions plugin for the MX Creative Console and the Actions Ring, with keys that show the active profile.
- **Installer**: per-machine MSIs for x64 and ARM64 with the service, `PATH`, file types, an "Apply with NetHop" context menu entry and a Start menu entry. Tagged releases also produce winget manifests.
- **English and Italian**: the app follows the Windows display language, or the one picked in Settings.
- **Rename adapters** without breaking profiles: profiles find adapters by GUID, then MAC, then name.

## Architecture

```
┌───────────────────────────┐   named pipe + JSON-RPC   ┌─────────────────────────────┐
│ NetHop.App (WinUI 3)      │ ◄───────────────────────► │ NetHop.Service (SYSTEM)     │
│ settings · tray · flyout  │                           │ profile store · rule engine │
├───────────────────────────┤                           │ network monitor             │
│ nethop.exe (CLI)          │ ◄───────────────────────► │ NetHop.Engine (CIM, Win32)  │
│ Logi plugin · PA actions  │                           └─────────────────────────────┘
└───────────────────────────┘
```

Changing IP settings needs administrator rights, so all changes are made by a Windows service and the UI never asks for elevation. Only **administrator accounts** can make changes; other accounts can open NetHop read-only, with a button to restart it as administrator. See [docs/architecture.md](docs/architecture.md).

| Project | Target | Role |
|---|---|---|
| `NetHop.Core` | net10.0 | Profile/rule models, YAML, validation, planner, rule engine. Pure logic, fully unit-tested |
| `NetHop.Ipc` | net10.0 | The service contract (`INetHopService`) and the pipe client |
| `NetHop.Engine` | net10.0, Windows | Reads and changes the network through CIM (`root\StandardCimv2`), iphlpapi and wlanapi |
| `NetHop.Service` | net10.0, Windows | Windows service hosting everything above |
| `NetHop.Cli` | net10.0 | `nethop.exe` |
| `NetHop.App` | net10.0-windows10.0.22621 | WinUI 3 app (Windows App SDK 2.5) |
| `NetHop.PowerAutomate` | net472 | Power Automate Desktop custom actions (`Modules.NetHop.dll`) |
| `NetHop.PowerShell` | PowerShell 5.1+ | `NetHop` module |
| `NetHop.LogiPlugin` | net8.0 | Logi Actions plugin. Builds only where Logi Options+ is installed |
| `installer/` | WiX 6 | MSI |

Integrations: [docs/integrations.md](docs/integrations.md). Installer: [docs/installer.md](docs/installer.md).

## Profile format

<img src="assets/icon/NetHopFile.svg" width="64" height="64" align="right" alt=".nethop file icon">

Profiles are `.nethop` files. Double-click one to open it in NetHop, or right-click it and choose **Apply with NetHop**.

```yaml
schema: 1
name: Office
adapters:
  - match: { name: Ethernet, mac: 00-1A-2B-3C-4D-5E }
    state: enabled
    ipv4:
      mode: static
      addresses: [192.168.10.50/24, 10.20.0.5/16]
      gateways: [192.168.10.1]
      dns: { mode: static, servers: [192.168.10.1, 1.1.1.1] }
    ipv6:
      mode: dhcp
  - match: { name: Wi-Fi }
    state: disabled
```

Full reference: [docs/profile-format.md](docs/profile-format.md). More examples are in [samples/](samples/).

## Command line

```
nethop list                      nethop apply Office [--dry-run]
nethop status [--json]           nethop export Office out.nethop
nethop adapters                  nethop rename "Ethernet 2" "Dock"
nethop enable|disable <adapter>  nethop import Office.nethop
nethop validate Office.nethop    nethop pause 60 | resume
nethop nethop://apply/Office
```

Exit codes: `0` ok, `1` failed, `2` usage error, `3` service not running, `4` access denied.

## Building

Requirements: .NET 10 SDK. The WinUI app builds only on Windows (Visual Studio 2022 17.14+ or the .NET CLI); everything else also builds on macOS and Linux.

```powershell
dotnet build NetHop.slnx
dotnet test tests/NetHop.Core.Tests
```

The app icon is drawn in SVG under [assets/icon](assets/icon): `NetHop.svg` (large sizes), `NetHop-small.svg` (20–32 px) and `NetHop-16.svg` (16 px). After editing one, run `python assets/icon/build_icons.py` (needs [resvg](https://github.com/linebender/resvg)) to regenerate the `.ico` and `.png` files.

Every push builds on GitHub Actions and produces **NetHop-msi** (x64 and ARM64 installers), **NetHop-win-x64** / **NetHop-win-arm64** (the same binaries as folders) and the Power Automate module (unsigned unless signing is configured, see [docs/installer.md](docs/installer.md#code-signing)). Install the MSI, or, for a quick test without it, extract the folder and run from an elevated PowerShell:

```powershell
.\Install-NetHop.ps1      # copies to Program Files, registers and starts the service
.\Uninstall-NetHop.ps1    # removes it (add -RemoveData to delete profiles and rules)
```

## Roadmap

- [x] Profile model (IPv4 + IPv6), YAML, validation, diff planner, rollback
- [x] Rule engine with loop protection; network change detection
- [x] Windows service, named pipe (admins change, others read), CLI
- [ ] Verify the CIM backend on real hardware (see the spike list in [docs/architecture.md](docs/architecture.md#open-questions-spikes))
- [x] WinUI scaffold: dashboard, YAML editors, adapters page, tray flyout
- [x] Form-based profile and rule editors (with a YAML view kept in sync)
- [x] Installer (WiX) with service, PATH, file types and Start menu
- [x] Logi Actions SDK plugin (to verify with real hardware)
- [x] Power Automate Desktop custom actions and PowerShell module
- [x] ARM64 MSI, "Apply with NetHop" context menu, GitHub releases and winget manifests
- [x] Code signing in CI (Azure Trusted Signing, active once the secrets are set)
- [x] Localization: English and Italian
- [x] Modern Windows 11 context menu entry (sparse package; shown on every machine once releases are signed)
- [ ] Verify everything on real hardware (spikes in docs/architecture.md)

## Acknowledgements

Thanks to [NetSetMan](https://www.netsetman.com/) by Ilja Herlein, which has made switching network settings on Windows easy for many years and inspired NetHop's profile-based approach. NetHop is an independent project: it is not affiliated with or endorsed by NetSetMan, and it shares no code with it. If you need a mature, feature-rich tool, give NetSetMan a try.

## License

[MIT](LICENSE)
