# Privacy policy

*Effective date: 22 October 2026*

This policy covers NetHop: the app, the Windows service, the `nethop` command line, the PowerShell module, the Power Automate actions, the Logi Actions plugin and the installer. NetHop is an open-source project maintained by [agateeno](https://github.com/agateeno); its source code is at <https://github.com/agateeno/net-hop>, where anyone can check what this policy says.

## In short

- NetHop collects **no personal data** and sends **no telemetry, analytics or crash reports**.
- It has **no accounts**, no advertising and no third-party trackers.
- Your profiles, rules and settings **stay on your PC**.
- It contacts the internet only to **check for updates** (which you can turn off) and when **you** ask it to.

## Network connections

NetHop connects to the internet only in these cases:

| When | To | What is sent |
|---|---|---|
| Checking for updates: about 30 seconds after the app starts, then once a day, or when you press **Check for updates** | `api.github.com` (GitHub) | An HTTPS request for the list of NetHop releases. The only information it carries is NetHop's version number, in the User-Agent header. |
| You open a link: **Release notes**, **Download**, GitHub in Settings › About | `github.com`, in your browser | Whatever your browser sends when it opens a web page. |
| A rule **you** created opens a web page or starts a Power Automate flow | The address you entered in that rule | Whatever your browser or Power Automate sends. |

Like any web server, GitHub sees the IP address that a request comes from. GitHub's handling of that data is described in the [GitHub General Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement). NetHop itself does not receive, store or share it.

To turn off the automatic update check: **Settings › Updates › Check for updates automatically**. NetHop then contacts GitHub only when you press **Check for updates**.

Changing your network configuration (IP addresses, gateways, DNS servers, adapters) is done locally through Windows, by the NetHop app or, when it is turned on, the NetHop service. It does not involve any NetHop server; there is none.

## Data stored on your PC

NetHop stores these files and settings on your computer only:

| What | Where |
|---|---|
| Profiles and automation rules | `%ProgramData%\NetHop` (readable and writable by administrators and the system only) |
| A copy of your profiles, for Explorer | `%USERPROFILE%\NetHop Profiles` |
| App settings (language, theme, update check, the last version you were notified about) | The registry, `HKEY_CURRENT_USER\Software\NetHop` |
| Start with Windows, when turned on | The registry, `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` with the NetHop service; without it, a scheduled task named `NetHop Startup` followed by your account's identifier |
| Details of app crashes, to help you report a problem | `%LOCALAPPDATA%\NetHop\crash.log` |
| Messages and errors of the service (or of the app, without the service) | The Windows Application event log |

Profiles can contain network details such as IP addresses, DNS servers, adapter names and MAC addresses, and Wi-Fi network names used in rules. None of this leaves your PC unless you export or share a profile file yourself.

The crash log is never sent anywhere. If you open an issue on GitHub, you choose what to attach.

Uninstalling NetHop removes its program files and asks whether to delete `%USERPROFILE%\NetHop Profiles`; answering **Yes** also removes your app settings. The profiles and rules in `%ProgramData%\NetHop` are kept, so that a new installation finds them: delete that folder as an administrator to remove them (for an installation made with the script, `Uninstall-NetHop.ps1 -RemoveData` does it). You can delete any of the locations above yourself at any time.

## Other components

- **Logi Actions plugin**: runs inside Logi Options+ and talks only to the NetHop service on your PC. Logitech's own software is covered by Logitech's privacy policy.
- **Power Automate actions and PowerShell module**: run the `nethop` command line on your PC. Microsoft Power Automate is covered by Microsoft's privacy statement.
- **Downloads and installation** from GitHub Releases, winget or the Microsoft Store are handled by those services under their own privacy policies.

## Children

NetHop does not collect data from anyone, including children.

## Changes to this policy

Changes are published in this file, in the project's repository, with a new effective date. The history of every change is visible on GitHub.

## Contact

For questions about this policy, open an issue at <https://github.com/agateeno/net-hop/issues>. To report a security problem, follow [SECURITY.md](SECURITY.md) instead, without public details.
