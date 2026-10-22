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

- **Administrators change, everyone else reads.** The pipe ACL lets SYSTEM, Administrators and interactive users open the pipe. For each connection, the service finds the client process (`GetNamedPipeClientProcessId`) and reads its token groups. It does not impersonate: `ImpersonateNamedPipeClient` fails until data has been read from the pipe. Under UAC an administrator's normal token lists *Administrators* as a deny-only group; `WindowsPrincipal.IsInRole` ignores such groups, so the token is read directly. As a result the tray app works **without elevation** for administrator accounts. Standard users get a read-only connection (`ReadOnlyService`): they can list profiles, rules and adapters and preview a profile (dry run), while every change fails with the JSON-RPC error `NetHopErrors.ReadOnly` and rule actions are not sent to them. `ServiceInfo.ReadOnly` tells the client up front; the app then shows a *Read-only* bar with **Restart as administrator** (UAC asks for an administrator's credentials) and disables editing.
- **Sandboxed clients are refused.** A client below medium integrity (a browser renderer, a low-integrity app) is refused even though, in an administrator's session, its token lists *Administrators*. The pipe's default integrity label already keeps it out; the service does not rely on that alone.
- **Connections are capped** at 32 at a time, since any interactive user may connect.
- **Pipe squatting.** Pipe names are global: while the service is stopped or restarting, any local user could create `NetHop.Service.v1` and pose as NetHop, sending fake data and rule actions that an administrator's app would run. The server creates the first instance with `FirstPipeInstance` and stops if the name is taken; other instances need `FILE_CREATE_PIPE_INSTANCE`, which the pipe ACL does not grant. Every client also checks, right after connecting, that the pipe server's process (`GetNamedPipeServerProcessId`) is the running `NetHop` service as the Service Control Manager reports it (`PipeServerCheck`); otherwise it refuses with `UntrustedPipeServerException`. Only administrators can register or replace a service.
- **No code runs as SYSTEM on a rule's behalf.** `runCommand`, `openUrl`, `runPowerAutomateFlow` and `notify` are forwarded to the users' apps through `UserActionRequested`, and run with their rights. `openUrl` only accepts `http`/`https` and flows only `ms-powerautomate:`, checked when rules are saved and again by the app: the shell would run whatever a `file://` URL points to.
- **Storage.** `%ProgramData%\NetHop` gets an explicit, protected ACL (SYSTEM and Administrators only) with Administrators as owner. Standard users may create folders in `%ProgramData%`, so at every start the service takes over an existing folder: a link is removed, a folder not owned by SYSTEM or Administrators is moved aside (`NetHop.untrusted-<time>`), and inside a trusted folder links are deleted and every entry gets the folder's owner and inherited permissions.
- **Install folder.** The MSI installs into Program Files and offers no folder choice. `Install-NetHop.ps1` gives any other destination the same permissions (only administrators may change it), because the service runs as SYSTEM from there.
- Profiles saved through the service are validated first. The service only accepts typed operations, never shell commands; YAML is read into fixed types (no polymorphic deserialization) and JSON-RPC uses System.Text.Json without type names.
- **Update check.** Only the app contacts GitHub, never the service. It reads the list of releases over HTTPS and, when one is newer, opens its page in the browser: the page address is built from the tag, not taken from the response, and nothing is downloaded or run.
- **Hosts file.** Hosts entries can redirect a site, like a DNS server can, so they get the same protections (only administrators change profiles, unknown files are reviewed first) and more:
  - NetHop writes only between its own markers and keeps every other byte of the file;
  - addresses and host names are validated strictly (letters, digits, hyphens, dots), so an entry can never add a line of its own;
  - names under the protected domains of `protected-domains.txt` (Microsoft, Google, Apple, certificate authorities, time servers and other major services) are rejected, in the editor and again by the service;
  - the file is replaced atomically with `ReplaceFile`, keeping its owner, permissions and attributes; a link or a read-only file is refused;
  - a copy of the original file is kept in `%ProgramData%\NetHop\hosts.backup`;
  - every change shows in the history, the notification and the Application event log.
- **Untrusted profile files.** *Apply with NetHop* applies a file directly only if it is one of the saved profiles, and then it applies the saved version. Any other file (a downloaded profile could point DNS at an attacker's server) opens in the editor to be reviewed and saved first.

Known limits, by design:

- Microsoft Defender may report a hosts file that redirects well-known domains as a threat (`SettingsModifier:Win32/HostsFileHijack`). The protected domain list keeps the usual triggers out; Defender's own list is not public, so an entry can still be reported (see `docs/profile-format.md`).

- Administrator accounts have full access without elevation, so any program running as an administrator (non-elevated) can change the network and the rules, including rules that run commands. Rule actions go to every administrator session that runs NetHop, so a rule saved by one administrator runs in the others' sessions too. Administrators can already control the machine, but this skips the UAC prompt.
- `nethop://apply/<profile>` applies an existing profile by name. A web page can open such a link; browsers ask before opening it, and it can only switch between the user's own profiles.
- Read-only connections can read the profiles' contents (addresses, DNS servers).
- StreamJsonRpc includes exception stack traces in error replies, also to read-only clients.

## Applying a profile

1. **Snapshot**: `CimNetworkBackend.GetSnapshot()` reads adapters (`MSFT_NetAdapter`), IP interfaces (`MSFT_NetIPInterface`), addresses (`MSFT_NetIPAddress`), default routes (`MSFT_NetRoute`), static DNS (`GetInterfaceDnsSettings`), the Wi-Fi SSID (`WlanQueryInterface`), Internet Connection Sharing (the `HNetCfg.HNetShare` COM API behind an adapter's Sharing tab), whether the mobile hotspot is on (WinRT `NetworkOperatorTetheringManager`) and NetHop's block of the hosts file.
2. **Plan**: `ProfilePlanner` diffs the profile against the snapshot and emits only the steps needed. Order: enable adapters and wait for them, then IP configuration, then sharing and the hosts file, then disable adapters. When gateways are removed or added, the planner orders them around their addresses: Windows rejects a gateway whose subnet is not on the interface yet.
3. **Execute**: `ProfileApplier` runs the steps one by one. Steps are idempotent: adding an existing address or deleting a missing one is not an error.
4. **Rollback**: if a step fails, the pre-apply snapshot is turned into a profile (`SnapshotProfile`) limited to the adapters the plan touched, plus the sharing state and NetHop's hosts block when the plan changed them, and it is applied best-effort.

**Sharing** is machine-wide: Windows shares one connection at a time, and the mobile hotspot uses that same pair (its source as the "public" connection, its own hidden virtual Wi-Fi adapter as the "private" one). To share a connection that Windows does not tether directly, such as most VPN adapters, the engine starts the hotspot from the internet connection, then points Internet Connection Sharing at the requested adapter, as is done by hand in the adapter's Sharing tab. The service targets the Windows SDK (`net10.0-windows10.0.22621.0`, like the app, since they share one publish folder) for the WinRT tethering API.
5. **Active profile**: a profile is active when planning it against the current snapshot yields no steps. The last applied profile wins over other matching ones. When it no longer matches, the UI reports *drift*.

Everything except step 1 and the execution of individual steps is pure code in `NetHop.Core` and is unit-tested on any OS.

## Automations

`NetworkMonitor` listens to `NetworkChange` notifications. It debounces them (3 s), takes a new snapshot and derives events with `NetworkEventDetector`: Wi-Fi connected/disconnected with SSID, adapter connected/disconnected, network changed. It also raises `startup` and a `schedule` tick every minute. `RuleEngine` evaluates the rules in priority order and applies these guards:

- events within 10 s of a profile change made by NetHop are ignored (no loops);
- per-rule cooldown;
- when several matching rules apply a profile, only the highest-priority one does;
- a global pause, set from the tray, the dashboard or `nethop pause`.

## Update check

`UpdateCheck` (app) reads `https://api.github.com/repos/agateeno/net-hop/releases` 30 seconds after the app starts, then once a day (an hourly timer also retries a failed check), or when **Check for updates** is pressed in Settings. `ReleaseFeed` (Core, unit-tested) skips drafts and tags other than `vX.Y.Z`, picks the highest version, with or without pre-releases, and compares it with the app's version.

- A newer release shows in Settings with a **Download** button that opens its page on github.com, and a notification once per version; clicking it opens Settings.
- Settings: **Check for updates automatically** (`HKCU\Software\NetHop\CheckForUpdates`, on by default) and **Include pre-releases** (`IncludePrereleases`, on by default while every release is a pre-release). The last notified tag is kept in `UpdateNotified`.
- Only CI release builds (*Run workflow* with a version, see `docs/installer.md`) are releases (`NetHopBuildKind=release`, an assembly metadata value). Other builds (`0.1.<run>`, local builds) show the latest release but are never offered it, since their version number says nothing about how recent they are.
- The request carries only a `NetHop/<version>` User-Agent. Without authentication GitHub allows 60 requests an hour per IP address.
- Nothing is downloaded or installed: the builds are not code-signed yet, so there is nothing to verify a download against. Once they are, *Download and install* can verify the MSI's Authenticode signature before running it.

## Localization

Languages: English (default), German, Spanish, French, Italian and Portuguese (Brazil).

- **App**: `Strings/<culture>/Resources.resw` (`en-US`, `de-DE`, `es-ES`, `fr-FR`, `it-IT`, `pt-BR`). XAML uses `x:Uid`, so the English text in the XAML is also the fallback. Code uses `Loc.Get("Key")`.
- **Core messages** (validation, plan steps): `Strings.resx` (English) and `Strings.<language>.resx` (`de`, `es`, `fr`, `it`, `pt`) in NetHop.Core, picked by the current UI culture. Neutral languages, so `fr-CA` or `pt-PT` users get French or Portuguese too.
- **Tests** (`LocalizationTests`) check that every language has every key of the English files, with the same placeholders, for both the app and Core.
- **Windows 11 context menu**: the C++ handler has its own short table (`Title()` in `ApplyCommand.cpp`).
- **Language choice**: Windows' display language by default; Settings can force one of the languages above (`HKCU\Software\NetHop\Language`). It is applied at startup through `ApplicationLanguages.PrimaryLanguageOverride` and the .NET UI culture.
- **Theme**: light or dark like Windows' app mode by default; Settings can force Light or Dark (`HKCU\Software\NetHop\Theme`). `AppTheme` applies it live to every window: the root element's `RequestedTheme` (the Mica/acrylic backdrop follows it) and the title bar's `PreferredTheme`.
- Messages created by the service (apply steps, errors) use the system language, because the service runs as SYSTEM. The CLI stays in English so scripts can rely on it.

To add a language: copy `Strings/en-US/Resources.resw` and `Strings.resx` to the new culture and translate them, add the tag to `AppLanguage.Supported` and the Settings language list, add the context menu title to `ApplyCommand.cpp`, the culture to `SatelliteResourceLanguages` in `Directory.Build.props`, and a line to each test in `LocalizationTests`.

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
| Windows 11 context menu from a sparse package, registered by the app | Only packaged `IExplorerCommand` handlers reach the first level of the menu. The package carries no app code (its content is the install folder), so the app stays unpackaged; packages are per user, so the app registers it at startup instead of the per-machine MSI |
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
