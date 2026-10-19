# AGENTS.md

Guidance for AI coding assistants (and humans) working on NetHop. Read [README.md](README.md) for what NetHop is and [docs/architecture.md](docs/architecture.md) for why it is built this way.

## What NetHop is

A network profile switcher for Windows 11 (in the spirit of NetSetMan), with a PowerToys-style WinUI 3 UI. Profiles are YAML `.nethop` files describing the desired state of network adapters (IPv4/IPv6, DHCP or static, DNS, enabled state). A Windows service applies them; rules apply them automatically.

## Layout

| Path | What | Target |
|---|---|---|
| `src/NetHop.Core` | Profile and rule model, YAML (de)serialization, validation, planner, rule engine, `nethop://` URIs. No Windows APIs. | net8.0; net10.0 |
| `src/NetHop.Engine` | Windows backend: CIM (`root\StandardCimv2`), iphlpapi (DNS), wlanapi (SSID), snapshot and rollback | net10.0, Windows-only |
| `src/NetHop.Service` | Windows service (LocalSystem): named pipe server, network monitor, rules. `ReadOnlyService` serves non-admin accounts | net10.0, Windows-only |
| `src/NetHop.Ipc` | JSON-RPC contract (`INetHopService`) shared by every client, pipe helpers | net8.0; net10.0 |
| `src/NetHop.Cli` | `nethop.exe` (scripts, Power Automate). Stable exit codes, `--json` | net10.0 |
| `src/NetHop.App` | WinUI 3 app (Windows App SDK 2.x, unpackaged, self-contained): pages, tray, notifications | net10.0-windows10.0.22621.0 |
| `src/NetHop.ShellExtension` | C++ `IExplorerCommand` + sparse package manifest for the Windows 11 context menu | MSBuild/vcxproj, not in the .slnx |
| `src/NetHop.PowerAutomate` | Power Automate Desktop custom actions (call the CLI) | net472 |
| `src/NetHop.PowerShell` | PowerShell module (wraps the CLI) | |
| `src/NetHop.LogiPlugin` | Logi Actions SDK plugin. Needs `PluginApi.dll` from Logi Options+: not in the .slnx or CI | net8.0 |
| `installer/` | WiX 6 MSI (x64, ARM64) | |
| `tests/NetHop.Core.Tests` | xUnit tests for Core (and the Core string tables) | net10.0 |
| `docs/` | `architecture.md`, `profile-format.md`, `integrations.md`, `installer.md` | |
| `samples/` | Example profiles and rules, used by the CI smoke test | |
| `scripts/` | Install/uninstall without the MSI, Power Automate packing, context menu package, winget manifests | |

## Build and test

```powershell
dotnet build NetHop.slnx
dotnet test tests/NetHop.Core.Tests
```

- .NET 10 SDK (`global.json`). Central package versions in `Directory.Packages.props`.
- **On macOS/Linux** Core, Ipc, Engine, Service, CLI and the tests build (`EnableWindowsTargeting`); the WinUI app, the C++ shell extension and the MSI need Windows. Run the tests there; Windows-only code is verified by CI.
- **CI** (`.github/workflows/ci.yml`, `windows-latest`) is the reference: build, tests, publish x64 + ARM64, a resource check, smoke tests on the runner (service install, CIM read, profile import and dry run, app start, context menu package registration), MSIs, optional signing. It runs on pushes to `main`, on PRs, and on demand (`workflow_dispatch`).
- Publish exactly as CI does (see the "Publish" step): service, CLI and app go into **one folder** sharing one self-contained runtime.

## Architecture rules that are easy to break

- **Only the service changes the network.** The app, CLI, Logi plugin and Power Automate are clients of `INetHopService` over the pipe `NetHop.Service.v1`. Never call CIM/iphlpapi from a client.
- **Access**: administrator accounts get full access even without elevation (the token's deny-only Administrators group is read directly, see `ClientAuthorization`); every other account gets `ReadOnlyService`, whose writes fail with `NetHopErrors.ReadOnly`. A new write method on `INetHopService` must also be refused in `ReadOnlyService`.
- **Contract changes**: adding to `INetHopService` or its records is compatible; removing or changing a member is not (bump `NetHopPipe.ProtocolVersion`, and update every client: App, CLI, LogiPlugin, PowerAutomate via the CLI, PowerShell).
- **Nothing a rule launches runs as SYSTEM**: user-session actions (toasts, commands, URLs, flows) are sent to the app (`UserActionRequested`) and run there.
- **Profiles on disk** live in `%ProgramData%\NetHop` (Administrators and SYSTEM only). `%USERPROFILE%\NetHop Profiles` is a per-user copy the app keeps in sync; it is never the source of truth.
- **Profile format** changes go through `docs/profile-format.md`, `ProfileValidator`, tests and, if incompatible, `NetHopConstants.CurrentSchemaVersion`. YAML parsing is strict: unknown keys are errors.

## Conventions

- **C#**: latest language version, nullable enabled, implicit usings, file-scoped namespaces. Match the surrounding code; comments explain *why*, briefly, and only where the code does not say it.
- **Localization (English + Italian) is required for every user-visible string**:
  - App: `src/NetHop.App/Strings/en-US/Resources.resw` and `it-IT/Resources.resw`, the same keys in both. XAML uses `x:Uid` (`Key.Property` entries); code uses `Loc.Get("Key")` / `Loc.Format(...)`. Nothing tests the app's two files for parity: add every key to both.
  - Core messages: `src/NetHop.Core/Strings.resx` and `Strings.it.resx` via `Text.Get`; `LocalizationTests` checks keys and placeholders.
  - Use the same register as the existing Italian strings (informal "tu").
- **Docs**: a user-visible change updates the matching file in `docs/` and, when it changes what NetHop can do, the README feature list or roadmap.
- **Tests**: Core logic changes come with tests in `tests/NetHop.Core.Tests`.
- **Commits**: imperative subject line (≤ 72 characters), body explaining what and why.

## Gotchas learned the hard way

- The WinUI app crashes at start (`0xc000027b` in `Microsoft.UI.Xaml.dll`) without its `.pri` file: unpackaged builds need `EnableMsixTooling`. CI checks the `.pri` exists; `%LOCALAPPDATA%\NetHop\crash.log` records unhandled exceptions.
- Use the Windows App SDK pickers (`Microsoft.Windows.Storage.Pickers`), not `Windows.Storage.Pickers`: the latter fail when NetHop runs as administrator.
- `ImpersonateNamedPipeClient` fails until data has been read; the service reads the client process token instead.
- App notifications may not register for an unpackaged app; `UserActionRunner.Notify` falls back to the tray icon.
- In PowerShell strings write `"${arch}:"`, not `"$arch:"` (parsed as a drive-qualified variable).
- XML comments must not contain `--` (WiX and MSBuild files).
- The Windows 11 context menu needs a package: `NetHop.ContextMenu.msix` is a sparse package registered per user by the app (`ContextMenuPackage`). Its Publisher must equal the signing certificate's subject (`PACKAGE_PUBLISHER`), see `docs/installer.md`.
- WiX 6 on purpose (WiX 7 requires accepting a maintenance-fee EULA).

## Things that cannot be verified in CI

The interactive UI, notifications, file pickers, the context menu in Explorer, real network changes (CI only dry-runs), the Logi plugin and Power Automate. Say so in PRs that touch them, and ask for a test on a real Windows 11 machine.
