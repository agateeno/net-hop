# Profile format (schema 1)

A profile is a YAML document saved with the `.nethop` extension. Keys are camelCase; unknown keys are rejected so that a typo never silently does nothing. Anything left out is **not touched** when the profile is applied.

```yaml
schema: 1                      # required for forward compatibility
id: 3f2b6c1e-...               # generated when missing; identifies the profile across renames
name: Office                   # required, unique, max 64 characters
description: Docking station   # optional
icon: ""                 # optional: a Segoe Fluent Icons glyph, or a custom image as "data:image/png;base64,..." (PNG, max 48 KB; the app scales it to 64×64)
color: "#0078D4"               # optional accent color, any #RRGGBB
adapters: [...]                # at least one entry, unless the profile only sets sharing or hosts
sharing: { ... }               # optional: mobile hotspot / Internet Connection Sharing
hosts: { ... }                 # optional: entries of the Windows hosts file
```

## Adapter entries

```yaml
- match:
    guid: "{A1B2C3D4-...}"     # most precise; written when a profile is captured from this PC
    mac: 00-1A-2B-3C-4D-5E     # any of 00:1a:..., 00-1A-..., 001A2B...
    name: Ethernet             # case-insensitive
  state: enabled               # enabled | disabled | unchanged (default)
  ipv4: { ... }                # optional
  ipv6: { ... }                # optional
```

`match` is resolved in this order: **GUID**, then a **unique MAC**, then a **unique name**. A profile captured on one PC still works on another one through the MAC or name. When no adapter matches, the entry is skipped with a warning and the profile is not considered active.

IP settings of a disabled adapter are ignored (with a warning).

## IP configuration (`ipv4` / `ipv6`)

```yaml
ipv4:
  mode: static                 # dhcp | static | unchanged (default)
  addresses:                   # static only, CIDR notation, one or more
    - 192.168.10.50/24
    - 10.20.0.5/16
  gateways: [192.168.10.1]     # static only, optional
  dns:
    mode: static               # dhcp | static | unchanged (default)
    servers: [192.168.10.1, 1.1.1.1]   # order matters: the first is preferred
  metric: 10                   # optional: automatic, or 1-9999; omitted leaves it as it is
```

- `dhcp` means automatic configuration: DHCP for IPv4, router advertisements/DHCPv6 for IPv6.
- `metric` is the interface metric: when several adapters reach the same network, Windows uses the one with the lowest metric. `automatic` lets Windows choose it from the link speed. A block may set only the metric (no `mode`). Profiles captured from the current configuration include it, so a captured profile is active only when the metrics match too.
- When `dns` is omitted, `mode: dhcp` also makes DNS automatic, while `mode: static` leaves DNS as it is.
- IPv6 gateways may be link-local (`fe80::1`).
- Switching to `static` removes manual addresses and gateways that are not listed; switching to `dhcp` removes all of them.

## Internet sharing (`sharing`)

Shares one connection's internet with other devices. It is machine-wide (Windows shares one connection at a time), so it sits at the profile level, not in an adapter entry. When `sharing` is omitted, the hotspot and sharing are left as they are.

```yaml
sharing:
  mode: hotspot                # hotspot | adapter | disabled | unchanged (default)
  from:                        # the connection whose internet is shared; same keys as an adapter match
    name: CloudflareWARP
  to:                          # adapter mode only: the adapter that receives the connection
    name: Ethernet 2
```

| `mode` | What it does |
|---|---|
| `hotspot` | Turns on the Windows **mobile hotspot**, sharing `from`. Without `from`, it shares the connection Windows uses for the internet. The hotspot's name, password and band are the ones set in Settings › Network & internet › Mobile hotspot. |
| `adapter` | **Internet Connection Sharing** from `from` to `to`, like the Sharing tab of an adapter's properties: devices on `to` get the connection, and Windows gives `to` the address `192.168.137.1`. Turns the hotspot off, since it uses the same sharing slot. |
| `disabled` | Turns the hotspot off and stops Internet Connection Sharing. |

Example: share Cloudflare WARP through the mobile hotspot, in a profile that changes nothing else:

```yaml
schema: 1
name: WARP hotspot
sharing:
  mode: hotspot
  from: { name: CloudflareWARP }
```

- A profile is active only when sharing matches too: the hotspot is on and shares `from`, or the pair is `from` → `to`.
- Windows tethers some connections directly; for the others (most VPN adapters) NetHop starts the hotspot from the internet connection and then points sharing at `from`, as you would do by hand.
- Profiles captured from the current configuration do not include sharing.

## Hosts file (`hosts`)

Maps host names to addresses in the Windows hosts file. It is machine-wide, so it sits at the profile level, not in an adapter entry. When `hosts` is omitted, the file is not touched.

```yaml
hosts:
  mode: set                    # set | clear | unchanged (default)
  entries:                     # set only
    - ip: 192.168.10.20        # IPv4 or IPv6, without prefix length
      names: [intranet.corp.local, wiki.corp.local]
    - ip: 0.0.0.0              # also to block a site
      names: [ads.example.com]
```

| `mode` | What it does |
|---|---|
| `set` | NetHop's section of the hosts file holds exactly these entries: switching to another profile with `set` replaces them. |
| `clear` | Removes NetHop's section. |

NetHop writes only its own section, one name per line, and keeps the rest of the file (your lines, Docker's, Hyper-V's…) exactly as it is:

```
# BEGIN NetHop (managed by NetHop, do not edit this block)
192.168.10.20 intranet.corp.local
192.168.10.20 wiki.corp.local
# END NetHop
```

- The change takes effect at once: NetHop flushes the DNS cache after writing.
- A profile is active only when NetHop's section holds its entries (in any order).
- A copy of the file as it was before NetHop first changed it is kept in `%ProgramData%\NetHop\hosts.backup`.
- If the hosts file is read-only, NetHop does not change it and reports an error: remove the read-only attribute to let it.
- Profiles captured from the current configuration do not include the hosts file.

### Protected domains

Names under the domains listed in [`protected-domains.txt`](../src/NetHop.Core/Profiles/protected-domains.txt) are rejected, with all their subdomains: Microsoft and Windows, Google, Apple, certificate authorities, time servers, Amazon, Meta, GitHub, Cloudflare, Mozilla. Redirecting them would cut the PC off from updates, sign-in or certificate checks, and Microsoft Defender reports it as a threat. A domain covers its subdomains, not other domains: `google.com` covers `mail.google.com`, while `googleadservices.com` or `doubleclick.net` can still be blocked. Country domains such as `google.it` are not in the list.

### If Microsoft Defender reports the hosts file

Defender watches the hosts file for redirected well-known domains (`SettingsModifier:Win32/HostsFileHijack`). The protected domains keep the usual triggers out, but Defender's list is not public. If it reports the file anyway:

1. Open **Windows Security › Virus & threat protection › Protection history** and check which entry it found.
2. If it is one of yours and you want to keep it, choose **Actions › Allow on device**. Otherwise let Defender clean it, and remove the entry from the profile.
3. To report a false positive, submit it at <https://www.microsoft.com/wdsi/filesubmission>.

## Validation

| Severity | Examples |
|---|---|
| Error | missing name, address without prefix, wrong address family, static mode without addresses, addresses in DHCP mode, duplicate addresses, two entries for the same adapter, a metric that is not `automatic` or 1-9999, sharing `adapter` without `from` or `to` (or with the same adapter twice), `to` with the hotspot, `from`/`to` with `disabled`; hosts: an address that is not plain IPv4/IPv6, an invalid host name, an entry without names, a name under a protected domain, a name with two addresses of the same family, `set` without entries or `entries` without `set`, more than 500 names |
| Warning | gateway outside every configured subnet, network/broadcast address, IP settings on a disabled adapter, an entry that changes nothing, a host name listed twice |

Profiles with errors cannot be saved or applied. Check a file with `nethop validate file.nethop`.

## Rules (`rules.nethop.yml`)

```yaml
schema: 1
rules:
  - name: Arrive at the office
    priority: 10               # the highest matching rule wins when several apply a profile
    cooldownSeconds: 60
    trigger: { type: wifiConnected, ssid: CORP-WiFi }   # "Casa*" is a prefix match; SSIDs are case-sensitive
    conditions:
      - { type: timeBetween, from: "07:00", to: "20:00" }   # wraps past midnight when to < from
      - { type: daysOfWeek, days: [monday, tuesday, wednesday, thursday, friday] }
    actions:
      - { type: applyProfile, profile: Office }
      - { type: notify, message: Office profile applied }
      - { type: runPowerAutomateFlow, url: "ms-powerautomate:/console/flow/run?workflowId=..." }
```

| Triggers | Conditions | Actions |
|---|---|---|
| `wifiConnected`, `wifiDisconnected` (`ssid`) | `timeBetween` (`from`, `to`) | `applyProfile` (`profile`) |
| `adapterConnected`, `adapterDisconnected` (`adapter`) | `daysOfWeek` (`days`) | `enableAdapter`, `disableAdapter` (`adapter`) |
| `networkChanged` | `adapterConnected`, `adapterDisconnected` (`adapter`) | `notify` (`message`) |
| `startup`, `logon`¹, `resume`¹ | `wifiSsid` (`ssid`) | `runCommand` (`command`, `arguments`) |
| `schedule` (`at: "HH:mm"`, `days`) | `activeProfile`, `notActiveProfile` (`profile`) | `openUrl`, `runPowerAutomateFlow` (`url`) |
| | `onBattery`, `onAcPower` | `delay` (`seconds`) |

¹ Accepted in rules, but the service does not raise these events yet.

Notifications, commands, URLs and flows run in the signed-in user's session, never with the service's SYSTEM rights. Events within 10 seconds of a profile change made by NetHop are ignored, so rules cannot trigger each other in a loop.
