# NetHop

**Hop between network configurations in one click.** NetHop is an open-source network profile switcher for Windows 11, in the spirit of NetSetMan, with a PowerToys-style Fluent UI.

> Status: early development (v0.1). The core engine and the CLI are in place; the UI is a first scaffold.

## Features

- **Profiles** enable/disable adapters and set IPv4 **and** IPv6: DHCP or static, with multiple addresses, gateways and DNS servers.
- **Minimal changes**: NetHop compares the profile with the live configuration and only changes what differs. A failed step rolls back the adapters it touched.
- **Plain files**: profiles are YAML documents with the `.nethop` extension. Double-click one to import it.
- **Tray flyout**: left-click the notification-area icon to see the active profile and switch with one click.
- **Built-in automations**: rules such as "when I join the Wi-Fi `CORP-WiFi` on a weekday, apply *Office*", with no extra software. Loop protection, cooldowns and a pause switch are included.
- **Power Automate**: drive NetHop from flows through the CLI (`nethop apply Office --json`), or start flows from NetHop rules.
- **Logitech**: a Logi Actions SDK plugin (planned); meanwhile any mouse button can open a `.nethop` file or a `nethop://` link.
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

Changing IP settings needs administrator rights, so all changes are made by a Windows service and the UI never asks for elevation. Only **administrator accounts** may connect to the service. See [docs/architecture.md](docs/architecture.md).

| Project | Target | Role |
|---|---|---|
| `NetHop.Core` | net10.0 | Profile/rule models, YAML, validation, planner, rule engine. Pure logic, fully unit-tested |
| `NetHop.Ipc` | net10.0 | The service contract (`INetHopService`) and the pipe client |
| `NetHop.Engine` | net10.0, Windows | Reads and changes the network through CIM (`root\StandardCimv2`), iphlpapi and wlanapi |
| `NetHop.Service` | net10.0, Windows | Windows service hosting everything above |
| `NetHop.Cli` | net10.0 | `nethop.exe` |
| `NetHop.App` | net10.0-windows10.0.22621 | WinUI 3 app (Windows App SDK 2.5) |

## Profile format

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
nethop status [--json]           nethop next
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

Every push builds on GitHub Actions; the **NetHop-win-x64** artifact contains the service, CLI and app. To try it on a Windows machine, extract the artifact and run from an elevated PowerShell:

```powershell
.\Install-NetHop.ps1      # copies to Program Files, registers and starts the service
.\Uninstall-NetHop.ps1    # removes it (add -RemoveData to delete profiles and rules)
```

## Roadmap

- [x] Profile model (IPv4 + IPv6), YAML, validation, diff planner, rollback
- [x] Rule engine with loop protection; network change detection
- [x] Windows service, admin-only named pipe, CLI
- [ ] Verify the CIM backend on real hardware (see the spike list in [docs/architecture.md](docs/architecture.md#open-questions-spikes))
- [x] WinUI scaffold: dashboard, YAML editors, adapters page, tray flyout
- [ ] Form-based profile and rule editors
- [ ] Installer (WiX) with service registration and Windows 11 context menu
- [ ] Logi Actions SDK plugin
- [ ] Power Automate Desktop custom actions
- [ ] Localization (Italian, English)

## License

[MIT](LICENSE)
