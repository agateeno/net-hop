namespace NetHop.Core.Profiles;

/// <summary>
/// A network profile: the desired state of one or more network adapters.
/// Anything left out (or set to <c>unchanged</c>) is not touched when the profile is applied.
/// </summary>
public sealed class Profile
{
    public int Schema { get; set; } = NetHopConstants.CurrentSchemaVersion;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Segoe Fluent Icons glyph shown in the UI, tray flyout and Logi plugin.</summary>
    public string? Icon { get; set; }

    /// <summary>Accent color as <c>#RRGGBB</c>.</summary>
    public string? Color { get; set; }

    public List<AdapterConfig> Adapters { get; set; } = [];
}

public sealed class AdapterConfig
{
    public AdapterMatch Match { get; set; } = new();

    public DesiredAdapterState State { get; set; } = DesiredAdapterState.Unchanged;

    public IpConfig? Ipv4 { get; set; }

    public IpConfig? Ipv6 { get; set; }
}

/// <summary>
/// How a profile finds its adapter. Resolution order is GUID, then MAC, then name,
/// so a profile copied to another PC still works through the MAC or name fallback.
/// </summary>
public sealed class AdapterMatch
{
    public string? Guid { get; set; }

    public string? Mac { get; set; }

    public string? Name { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Guid) && string.IsNullOrWhiteSpace(Mac) && string.IsNullOrWhiteSpace(Name);

    public override string ToString() =>
        Name ?? Mac ?? Guid ?? "(empty match)";
}

public enum DesiredAdapterState
{
    Unchanged,
    Enabled,
    Disabled,
}

public sealed class IpConfig
{
    public IpMode Mode { get; set; } = IpMode.Unchanged;

    /// <summary>Static addresses in CIDR notation, e.g. <c>192.168.1.10/24</c> or <c>2001:db8::10/64</c>.</summary>
    public List<string> Addresses { get; set; } = [];

    public List<string> Gateways { get; set; } = [];

    /// <summary>
    /// DNS servers. When omitted, switching to <see cref="IpMode.Dhcp"/> also makes DNS automatic,
    /// while <see cref="IpMode.Static"/> leaves DNS as it is.
    /// </summary>
    public DnsConfig? Dns { get; set; }
}

public enum IpMode
{
    Unchanged,

    /// <summary>Automatic configuration: DHCP for IPv4, SLAAC/DHCPv6 for IPv6.</summary>
    Dhcp,

    Static,
}

public sealed class DnsConfig
{
    public DnsMode Mode { get; set; } = DnsMode.Unchanged;

    public List<string> Servers { get; set; } = [];
}

public enum DnsMode
{
    Unchanged,
    Dhcp,
    Static,
}
