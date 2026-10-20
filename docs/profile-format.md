# Profile format (schema 1)

A profile is a YAML document saved with the `.nethop` extension. Keys are camelCase; unknown keys are rejected so that a typo never silently does nothing. Anything left out is **not touched** when the profile is applied.

```yaml
schema: 1                      # required for forward compatibility
id: 3f2b6c1e-...               # generated when missing; identifies the profile across renames
name: Office                   # required, unique, max 64 characters
description: Docking station   # optional
icon: ""                 # optional: a Segoe Fluent Icons glyph, or a custom image as "data:image/png;base64,..." (PNG, max 48 KB; the app scales it to 64×64)
color: "#0078D4"               # optional accent color, any #RRGGBB
adapters: [...]                # at least one entry
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

## Validation

| Severity | Examples |
|---|---|
| Error | missing name, address without prefix, wrong address family, static mode without addresses, addresses in DHCP mode, duplicate addresses, two entries for the same adapter, a metric that is not `automatic` or 1-9999 |
| Warning | gateway outside every configured subnet, network/broadcast address, IP settings on a disabled adapter, an entry that changes nothing |

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
