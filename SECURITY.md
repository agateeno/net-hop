# Security policy

NetHop installs a Windows service that runs as **LocalSystem** and changes network settings, so security reports are taken seriously. Thank you for helping keep its users safe.

## Supported versions

NetHop is in preview. Only the **latest release** receives security fixes; older versions are not patched. Please check that an issue still exists in the latest release (or on `main`) before reporting it.

| Version | Supported |
|---|---|
| Latest release (currently 0.2.x) | Yes |
| Older releases, CI builds (0.1.x) | No |

## Reporting a vulnerability

**Please do not open a public issue, pull request or discussion with the details of a vulnerability.**

Report it privately through GitHub: on the repository page, open **Security › Report a vulnerability** ([private vulnerability reporting](https://docs.github.com/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)). Only the maintainers can see the report.

If that option is not available, open an issue titled *"Security contact request"* **without any detail**, and a maintainer will reply with a private way to send the report.

A useful report includes:

- the NetHop version (Settings › About) and the Windows version and edition;
- what an attacker needs (local standard user, another user's session, a crafted `.nethop` file, a web page, network position…) and what they gain;
- steps or a proof of concept that reproduce it;
- any workaround you know of.

## What to expect

NetHop is maintained in spare time, so these are goals rather than guarantees:

- an acknowledgement within **7 days**;
- an initial assessment within **14 days**;
- a fix in a new release as soon as practical for confirmed issues, coordinated with you. You will be credited in the release notes unless you prefer otherwise.

Please give us reasonable time to release a fix before disclosing the issue publicly.

## Scope

The security model, its trust boundaries and the protections in place are described in [docs/architecture.md › Security model](docs/architecture.md#security-model). Reports are especially welcome for:

- privilege escalation through the NetHop service (the `NetHop.Service.v1` pipe, the JSON-RPC API, the read-only mode for standard users);
- tampering with `%ProgramData%\NetHop`, the install folder or the service binaries;
- code execution through profiles, rules, `.nethop` files, `nethop://` links or the Windows 11 context menu handler;
- a client being tricked into talking to something other than the genuine NetHop service;
- the installer (MSI) and the uninstall steps.

The following are **known and documented limits**, not vulnerabilities on their own (see the same section):

- administrator accounts have full access without elevation, and rule actions run in every administrator session that runs NetHop;
- a `nethop://apply/<profile>` link can switch between existing profiles after the browser's confirmation;
- standard users can read profile contents through the read-only mode;
- preview builds are not code-signed yet.

Out of scope: issues that need an attacker who is already an administrator or SYSTEM on the machine, physical access to an unlocked session, or changes to Windows' own settings; vulnerabilities in third-party components without a NetHop-specific impact (please report those upstream); and automated scanner output without a demonstrated impact.
