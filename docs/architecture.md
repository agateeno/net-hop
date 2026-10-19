# Architecture

## Processes

| Process | Account | Responsibility |
|---|---|---|
| `NetHop.Service.exe` | LocalSystem | Owns profiles and rules, reads and changes the network, watches for changes, runs rules |
| `NetHop.exe` (WinUI) | signed-in user | Settings window, tray icon and flyout, toasts, runs user-session rule actions |
| `nethop.exe` (CLI) | caller | Scripting and Power Automate |
| Logi Actions plugin | signed-in user, inside Logi Plugin Service | MX Creative Console / Actions Ring buttons |
| Power Automate Desktop actions | signed-in user, inside PAD | Call `nethop.exe --json` |

All clients talk to the service through the `NetHop.Service.v1` named pipe using JSON-RPC ([StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc), System.Text.Json formatter). The contract is `NetHop.Ipc.INetHopService`. It includes two events, `StatusChanged` and `UserActionRequested`, that the service pushes to every connected client.

## Security model

- **Only administrator accounts.** The pipe ACL lets SYSTEM, Administrators and interactive users open the pipe. For each connection, the service impersonates the client (identification level only) and checks its token groups. Under UAC an administrator's normal token lists *Administrators* as a deny-only group; `WindowsPrincipal.IsInRole` ignores such groups, so the token is read directly. As a result the tray app works **without elevation**, while standard users are refused.
- **Pipe squatting.** The server creates the first instance with `FirstPipeInstance` and stops if the name is already taken. *TODO:* clients should also verify that the server process is the NetHop service (`GetNamedPipeServerProcessId`).
- **No code runs as SYSTEM on a rule's behalf.** `runCommand`, `openUrl`, `runPowerAutomateFlow` and `notify` are forwarded to the user's app through `UserActionRequested`.
- **Storage.** `%ProgramData%\NetHop` is created with an explicit ACL: SYSTEM and Administrators get full control, nobody else has access.
- Profiles saved through the service are validated first. The service only accepts typed operations, never shell commands.

## Applying a profile

1. **Snapshot**: `CimNetworkBackend.GetSnapshot()` reads adapters (`MSFT_NetAdapter`), IP interfaces (`MSFT_NetIPInterface`), addresses (`MSFT_NetIPAddress`), default routes (`MSFT_NetRoute`), static DNS (`GetInterfaceDnsSettings`) and the Wi-Fi SSID (`WlanQueryInterface`).
2. **Plan**: `ProfilePlanner` diffs the profile against the snapshot and emits only the steps needed. Order: enable adapters and wait for them, then IP configuration, then disable adapters. When gateways are removed or added, the planner orders them around their addresses: Windows rejects a gateway whose subnet is not on the interface yet.
3. **Execute**: `ProfileApplier` runs the steps one by one. Steps are idempotent: adding an existing address or deleting a missing one is not an error.
4. **Rollback**: if a step fails, the pre-apply snapshot is turned into a profile (`SnapshotProfile`) limited to the adapters the plan touched, and it is applied best-effort.
5. **Active profile**: a profile is active when planning it against the current snapshot yields no steps. The last applied profile wins over other matching ones. When it no longer matches, the UI reports *drift*.

Everything except step 1 and the execution of individual steps is pure code in `NetHop.Core` and is unit-tested on any OS.

## Automations

`NetworkMonitor` listens to `NetworkChange` notifications. It debounces them (3 s), takes a new snapshot and derives events with `NetworkEventDetector`: Wi-Fi connected/disconnected with SSID, adapter connected/disconnected, network changed. It also raises `startup` and a `schedule` tick every minute. `RuleEngine` evaluates the rules in priority order and applies these guards:

- events within 10 s of a profile change made by NetHop are ignored (no loops);
- per-rule cooldown;
- when several matching rules apply a profile, only the highest-priority one does;
- a global pause, set from the tray, the dashboard or `nethop pause`.

## Decisions

| Decision | Why |
|---|---|
| CIM (`root\StandardCimv2`) instead of `netsh` | Locale-independent, structured errors, same classes as the PowerShell NetTCPIP module |
| `SetInterfaceDnsSettings` for DNS | Clean "back to DHCP" (empty list) and IPv6 support without registry tweaks |
| Core and Ipc target net8.0 and net10.0 | Logi Plugin Service loads plugins on .NET 8, and the plugin reuses the same pipe client |
| PAD actions call the CLI instead of the pipe | PAD loads .NET Framework 4.7.2 modules, and every DLL shipped with them must be signed: calling `nethop.exe` keeps the module to a single DLL |
| Plain `net10.0` for Engine/Service/CLI | The `-windows` TFM makes StreamJsonRpc's VS.Threading dependency pull in WPF. The assemblies are marked `[SupportedOSPlatform("windows")]` instead |
| Windows App SDK 2.5 | Every API NetHop uses exists unchanged in 2.x (checked against the 2.x metadata). CommunityToolkit and H.NotifyIcon restore fine on it. 2.x brings `SystemBackdropElement`, `ApplicationData.GetForUnpackaged()` and the new storage pickers, and it is the line that gets new features |
| Unpackaged app + `ActivationRegistrationManager` | Registers `.nethop` and `nethop://` per user without MSIX; an installer can switch to a sparse package for the Windows 11 context menu |
| YAML profiles | Readable, diff-friendly, easy to share; strict parsing catches typos |

## Open questions (spikes)

These need a real Windows machine with physical adapters (USB Ethernet + Wi-Fi):

1. **Persistence**: do changes made through both `ActiveStore` and `PersistentStore` survive a reboot exactly like `New-NetIPAddress` / `Set-NetIPInterface`?
2. **DHCP → static**: when DHCP is disabled, does Windows drop the leased address and gateway, or must the plan remove them explicitly?
3. **IPv6 static**: is turning off `Dhcp` on the IPv6 interface enough, or should router discovery be disabled too, so that SLAAC addresses do not linger?
4. **SSID from SYSTEM**: on Windows 11 24H2+, can the service read SSIDs, or does location consent force SSID reading into the user-session app?
5. **Timing**: how long do enabling an adapter and applying a typical profile take?
6. **Logi Actions SDK**: can plugin actions be assigned directly to MX Master buttons, or only to the Actions Ring and the Creative Console?
7. **Rename**: does `MSFT_NetAdapter.Rename` behave on Wi-Fi adapters, and are Wi-Fi profiles affected?
8. **Windows App SDK 2.x at runtime**: H.NotifyIcon and the CommunityToolkit were built against 1.6. They restore and should bind fine, but check the tray icon, the context menu and SettingsCard on a real machine.
9. **Install size**: the `Microsoft.WindowsAppSDK` metapackage also brings AI, ML, Search and Widgets. Measure the self-contained output and, if it is large, reference only `Microsoft.WindowsAppSDK.WinUI` (which pulls Foundation and InteractiveExperiences).
