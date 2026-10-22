# AGENTS.md

Guidance for AI coding assistants (and humans) working on NetHop. Read [README.md](README.md) for what NetHop is and [docs/architecture.md](docs/architecture.md) for why it is built this way.

## What NetHop is

A network profile switcher for Windows 11 (in the spirit of NetSetMan), with a PowerToys-style WinUI 3 UI. Profiles are YAML `.nethop` files describing the desired state of network adapters (IPv4/IPv6, DHCP or static, DNS, enabled state). By default the app applies them itself, running elevated; the optional Windows service (turned on in Settings) applies them for every client and runs the rules that apply them automatically.

## Layout

| Path | What | Target |
|---|---|---|
| `src/NetHop.Core` | Profile and rule model, YAML (de)serialization, validation, planner, rule engine, `nethop://` URIs. No Windows APIs. | net8.0; net10.0 |
| `src/NetHop.Engine` | Windows backend: CIM (`root\StandardCimv2`), iphlpapi (DNS), wlanapi (SSID), snapshot and rollback | net10.0, Windows-only |
| `src/NetHop.Host` | The engine behind `INetHopService`: `NetHopController`, profile/rule/state/history stores, `DataPaths` (`%ProgramData%\NetHop`). Hosted by the service, or by the app without it | net10.0-windows, Windows-only |
| `src/NetHop.Service` | Optional Windows service (LocalSystem, off by default): named pipe server, network monitor, rules. `ReadOnlyService` serves non-admin accounts | net10.0-windows, Windows-only |
| `src/NetHop.Ipc` | JSON-RPC contract (`INetHopService`) shared by every client, pipe helpers | net8.0; net10.0 |
| `src/NetHop.Cli` | `nethop.exe` (scripts, Power Automate). Stable exit codes, `--json` | net10.0 |
| `src/NetHop.App` | WinUI 3 app (Windows App SDK 2.x, unpackaged, self-contained): pages, tray, notifications. Without the service it runs the engine itself (`LocalService`) and turns the service on/off (`ServiceSetup`) | net10.0-windows10.0.22621.0 |
| `src/NetHop.ShellExtension` | C++ `IExplorerCommand` + sparse package manifest for the Windows 11 context menu (its title has its own language table) | MSBuild/vcxproj, not in the .slnx |
| `src/NetHop.PowerAutomate` | Power Automate Desktop custom actions (call the CLI) | net472 |
| `src/NetHop.PowerShell` | PowerShell module (wraps the CLI) | |
| `src/NetHop.LogiPlugin` | Logi Actions SDK plugin. Needs `PluginApi.dll` from Logi Options+: not in the .slnx or CI | net8.0 |
| `installer/` | WiX 6 MSI (x64, ARM64) | |
| `tests/NetHop.Core.Tests` | xUnit tests for Core (and the Core string tables) | net10.0 |
| `docs/` | `architecture.md`, `profile-format.md`, `integrations.md`, `installer.md` | |
| `samples/` | Example profiles and rules, used by the CI smoke test | |
| `scripts/` | Local MSI build (`Build-Msi.ps1`), install/uninstall without the MSI, Power Automate packing, context menu package, winget manifests | |

## Build and test

```powershell
dotnet build NetHop.slnx
dotnet test tests/NetHop.Core.Tests
```

- .NET 10 SDK (`global.json`). Central package versions in `Directory.Packages.props`.
- **On macOS/Linux** Core, Ipc, Engine, Service, CLI and the tests build (`EnableWindowsTargeting`); the WinUI app, the C++ shell extension and the MSI need Windows. Run the tests there; Windows-only code is verified by CI.
- **CI** (`.github/workflows/ci.yml`, `windows-latest`) is the reference: build, tests, publish x64 + ARM64, a resource check, smoke tests on the runner (service install, CIM read, profile import and dry run, app start, context menu package registration), MSIs, optional signing. It runs on pushes to `main`, on PRs, and on demand (`workflow_dispatch`); run on demand with a version, it builds that release and drafts it with the files attached (releases are immutable once published, see `docs/installer.md`). Publishing a release starts `.github/workflows/winget.yml`, which submits it to winget.
- Publish exactly as CI does (see the "Publish" step): service, CLI and app go into **one folder** sharing one self-contained runtime.

## Architecture rules that are easy to break

- **Only the engine changes the network**: `NetHopController` (`NetHop.Host`), in the service or, when the service is off, in the elevated app (`LocalService`). The two never run together: the app uses the service whenever it is registered. The CLI, Logi plugin and Power Automate are clients of `INetHopService` over the pipe `NetHop.Service.v1` and need the service. Never call CIM/iphlpapi anywhere else.
- **Automations need the service.** Without it nothing evaluates rules; the UI says so (Automations page, dashboard, tray). Keep it that way rather than running rules in the app.
- **Access**: administrator accounts get full access even without elevation (the token's deny-only Administrators group is read directly, see `ClientAuthorization`); every other account gets `ReadOnlyService`, whose writes fail with `NetHopErrors.ReadOnly`. A new write method on `INetHopService` must also be refused in `ReadOnlyService` (and forwarded in the app's `LocalService`).
- **Contract changes**: adding to `INetHopService` or its records is compatible; removing or changing a member is not (bump `NetHopPipe.ProtocolVersion`, and update every client: App, CLI, LogiPlugin, PowerAutomate via the CLI, PowerShell).
- **Nothing a rule launches runs as SYSTEM**: user-session actions (toasts, commands, URLs, flows) are sent to the app (`UserActionRequested`) and run there.
- **Profiles on disk** live in `%ProgramData%\NetHop` (Administrators and SYSTEM only), with or without the service. `%USERPROFILE%\NetHop Profiles` is a per-user copy the app keeps in sync; it is never the source of truth.
- **Profile format** changes go through `docs/profile-format.md`, `ProfileValidator`, tests and, if incompatible, `NetHopConstants.CurrentSchemaVersion`. YAML parsing is strict: unknown keys are errors.

## Conventions

- **C#**: latest language version, nullable enabled, implicit usings, file-scoped namespaces. Match the surrounding code; comments explain *why*, briefly, and only where the code does not say it.
- **Every user-visible string exists in all six languages** (English, German, Spanish, French, Italian, Portuguese (Brazil)):
  - App: `src/NetHop.App/Strings/<culture>/Resources.resw` (`en-US`, `de-DE`, `es-ES`, `fr-FR`, `it-IT`, `pt-BR`). XAML uses `x:Uid` (`Key.Property` entries); code uses `Loc.Get("Key")` / `Loc.Format(...)`.
  - Core messages: `src/NetHop.Core/Strings.resx` and `Strings.<de|es|fr|it|pt>.resx` via `Text.Get`.
  - `LocalizationTests` fails when a language misses a key or a placeholder, so add new keys to every file. Translate them properly; do not leave English text in other languages.
  - Follow Windows' own terminology in each language and the register already used: "tu" in Italian, "vous" in French, "Sie" in German, "tú" in Spanish, "você" in Portuguese.
  - Adding a language: see the end of the Localization section in `docs/architecture.md`.
- **Page layout**: every page's root is a `Grid` with `Style="{StaticResource PageGridStyle}"` and one `ColumnDefinition MaxWidth="{StaticResource PageMaxWidth}"`, so all pages share the same left edge and maximum width (as in Windows Settings). Do not put `MaxWidth` on the content itself: it centers it in wide windows.
- **Docs**: a user-visible change updates the matching file in `docs/` and, when it changes what NetHop can do, the README feature list or roadmap.
- **Tests**: Core logic changes come with tests in `tests/NetHop.Core.Tests`.
- **Commits**: imperative subject line (≤ 72 characters), body explaining what and why.

## Gotchas learned the hard way

- The WinUI app crashes at start (`0xc000027b` in `Microsoft.UI.Xaml.dll`) without its `.pri` file: unpackaged builds need `EnableMsixTooling`. CI checks the `.pri` exists; `%LOCALAPPDATA%\NetHop\crash.log` records unhandled exceptions.
- Use the Windows App SDK pickers (`Microsoft.Windows.Storage.Pickers`), not `Windows.Storage.Pickers`: the latter fail when NetHop runs as administrator.
- `ImpersonateNamedPipeClient` fails until data has been read; the service reads the client process token instead.
- Security model and its limits: `docs/architecture.md` › Security model (vulnerability reports: `SECURITY.md`, keep its scope list in step with that section). Keep these: clients verify the pipe server is the NetHop service (`PipeServerCheck`); the engine takes over `%ProgramData%\NetHop` (`DataPaths`); the app registers the service only from a folder that only administrators can change (`ServiceSetup`); `openUrl` stays http/https only; the update check runs in the app, opens only the release page built from the tag and downloads nothing; unknown `.nethop` files are reviewed, never applied blind.
- App notifications may not register for an unpackaged app; `UserActionRunner.Notify` falls back to the tray icon.
- In PowerShell strings write `"${arch}:"`, not `"$arch:"` (parsed as a drive-qualified variable).
- XML comments must not contain `--` (WiX and MSBuild files).
- The Windows 11 context menu needs a package: `NetHop.ContextMenu.msix` is a sparse package registered per user by the app (`ContextMenuPackage`). Its Publisher must equal the signing certificate's subject (`PACKAGE_PUBLISHER`), see `docs/installer.md`.
- WiX 6 on purpose (WiX 7 requires accepting a maintenance-fee EULA).

## Things that cannot be verified in CI

The interactive UI, notifications, file pickers, the context menu in Explorer, real network changes (CI only dry-runs), UAC prompts and the start-up scheduled task without the service, turning the service on and off from Settings, MSI upgrades and uninstall, the Logi plugin and Power Automate. Say so in PRs that touch them, and ask for a test on a real Windows 11 machine.
