# Integrations

NetHop can be driven from four places. All of them go through the NetHop service, which is **off by default**: turn it on in NetHop's **Settings › NetHop service** first (without it they report that the service is not running, exit code 3). The same rule then applies everywhere: **only administrator accounts** can switch profiles. Other accounts can only read (`nethop status`, `list`, `apply --dry-run`); changes exit with code 4.

| From | How | Needs |
|---|---|---|
| Any script or tool | `nethop.exe` (CLI) with `--json` | NetHop installed |
| PowerShell, Power Automate Desktop "Run PowerShell script" | `NetHop` PowerShell module | NetHop installed (the MSI adds the module) |
| Power Automate Desktop actions pane | Custom actions module `NetHop.cab` | Signed `.cab` uploaded to your environment |
| Logitech MX Creative Console, Actions Ring, Loupedeck | Logi Actions plugin `NetHop.lplug4` | Logi Options+ |

Links (`nethop://apply/Office`) and `.nethop` files also work from anything that can open a file or a URL: shortcuts, browser bookmarks, mouse buttons.

## Command line

```powershell
nethop apply Office --json     # {"success":true,"profileName":"Office","steps":[...],...}
nethop status --json
nethop list --json
```

Exit codes are stable: `0` ok, `1` failed, `2` usage error, `3` service not running, `4` access denied.

## PowerShell module

Installed to `%ProgramFiles%\WindowsPowerShell\Modules\NetHop`, so it loads in both Windows PowerShell 5.1 (the one Power Automate Desktop uses) and PowerShell 7.

| Command | Does |
|---|---|
| `Get-NetHopProfile [name]` | Lists profiles (`isActive` marks the active one) |
| `Get-NetHopStatus` | Active profile, drift, automation pause |
| `Set-NetHopProfile <name>` | Applies a profile; `-WhatIf` only shows the steps |
| `Get-NetHopAdapter [name]` | Adapters with their live addresses |
| `Suspend-NetHopAutomation [-Minutes 60]`, `Resume-NetHopAutomation` | Pause/resume NetHop's own rules |

Errors are thrown as terminating errors, so a Power Automate "Run PowerShell script" step fails visibly.

## Power Automate Desktop custom actions

`src/NetHop.PowerAutomate` builds `Modules.NetHop.dll`: a **NetHop** group in the actions pane.

| Action | Inputs | Outputs |
|---|---|---|
| Apply network profile | Profile, Preview only | Changes, Warnings |
| Get network profile status | | Active profile, Changed by hand, Automations paused |
| Get network profiles | | Profiles (one per line), Count |
| If network profile is active | Profile | (condition) |
| Pause network automations | Minutes (0 resumes) | |

Every action reports `Failed`, `ServiceUnavailable`, `AccessDenied` or `NetHopNotInstalled` errors, which you can handle in the action's *On error* tab. The actions call `nethop.exe`, so the module has no dependencies to sign besides itself.

### Publishing the module

Power Automate refuses unsigned custom actions. The DLL and the `.cab` must be signed with a code-signing certificate that every machine running the flows trusts (Trusted Root Certification Authorities).

1. Get a code-signing certificate (from your organization's CA; a self-signed one is enough for a lab, if you install it as trusted on the machines).
2. On Windows, with the Windows SDK installed:
   ```powershell
   .\scripts\Build-PowerAutomateModule.ps1 -CertificatePath .\codesign.pfx
   ```
   The result is `out\power-automate\NetHop.cab`.
3. In the Power Automate portal, open **Custom actions** > **Upload custom action** and upload the `.cab` (this needs the *Desktop Flow Module Developer* role).
4. In Power Automate Desktop, open **Assets library** in a flow and add **NetHop**.

CI builds an **unsigned** `.cab` (artifact `NetHop-PowerAutomate-unsigned`) only to prove that it builds.

### Triggers

Power Automate Desktop has no "network changed" trigger. Use NetHop rules instead: a rule with a `runPowerAutomateFlow` action starts a flow by its run URL when, for example, you join a Wi-Fi network. See [profile-format.md](profile-format.md#rules-rulesnethopyml).

## Logi Actions plugin

`src/NetHop.LogiPlugin` is a plugin for Logi Plugin Service (part of Logi Options+), written with the [Logi Actions SDK](https://logitech.github.io/actions-sdk-docs/).

| Action | On the key |
|---|---|
| **Apply profile**: pick the profile from a list in Options+ | Profile name on the profile color; a bar marks the active profile; "applying..." while it runs |
| **Pause automations** | "Auto on" / "Auto paused" |

The plugin connects to the NetHop service and reconnects by itself. Keys redraw when anything changes, including changes made from the tray, the CLI or a rule. When the service is unreachable, Options+ shows the plugin status as an error with the reason.

### Building and installing

The SDK is `PluginApi.dll`, installed with Logi Options+, so the plugin builds only on a PC that has Options+. It is not part of `NetHop.slnx` or CI.

```powershell
cd src\NetHop.LogiPlugin
dotnet build -c Debug        # builds and loads the plugin into Logi Plugin Service (hot reload)
```

To produce an installable package:

```powershell
dotnet tool install -g Loupedeck.PluginTool
dotnet build -c Release
logiplugintool pack .\bin\Release .\NetHop.lplug4
```

Double-click `NetHop.lplug4` to install it. Then, in Logi Options+, open the customization of the MX Creative Console or the Actions Ring and look for **NetHop** under *Installed plugins*.

If Options+ is installed somewhere else, pass `-p:PluginApiDir=<folder containing PluginApi.dll>`.

### Mouse buttons

Logi Options+ can show the Actions Ring from an MX mouse button, and the ring's slots can hold the NetHop actions. A button can also open a `.nethop` file or a `nethop://` link directly.

### To verify on real hardware

- The plugin targets .NET 8 like Logitech's samples. Check that StreamJsonRpc and its dependencies load next to Logi Plugin Service's own assemblies.
- The plugin code was compiled against a stub of the SDK's API (taken from Logitech's docs and samples). The first build against the real `PluginApi.dll` may need small signature fixes.
