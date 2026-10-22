using System.Net;

namespace NetHop.Core.Networking;

/// <summary>Static description of a network adapter, as reported by Windows.</summary>
public sealed record AdapterInfo
{
    public required Guid Id { get; init; }

    /// <summary>The user-visible (renamable) name, e.g. "Ethernet 2".</summary>
    public required string Name { get; init; }

    /// <summary>Driver description, e.g. "Intel(R) Ethernet Connection I219-V".</summary>
    public string Description { get; init; } = "";

    /// <summary>Permanent MAC address in <c>00-1A-2B-3C-4D-5E</c> form, empty if unknown.</summary>
    public string Mac { get; init; } = "";

    public uint InterfaceIndex { get; init; }

    public AdapterKind Kind { get; init; } = AdapterKind.Other;

    /// <summary>True for Hyper-V, VPN, WSL and other software adapters.</summary>
    public bool IsVirtual { get; init; }
}

public enum AdapterKind
{
    Other,
    Ethernet,
    WiFi,
    Cellular,
    Bluetooth,
    Vpn,
}

/// <summary>Live state of one adapter.</summary>
public sealed record AdapterSnapshot
{
    public required AdapterInfo Info { get; init; }

    /// <summary>False when the adapter is disabled in Windows.</summary>
    public bool Enabled { get; init; }

    /// <summary>Media connected (cable plugged in / Wi-Fi associated).</summary>
    public bool Connected { get; init; }

    /// <summary>SSID of the connected Wi-Fi network, when known.</summary>
    public string? Ssid { get; init; }

    /// <summary>Null when unknown, e.g. the adapter is disabled and has no IP interface.</summary>
    public FamilySnapshot? Ipv4 { get; init; }

    public FamilySnapshot? Ipv6 { get; init; }
}

/// <summary>Configuration of one address family (IPv4 or IPv6) on an adapter.</summary>
public sealed record FamilySnapshot
{
    /// <summary>Automatic configuration (DHCP / SLAAC) is on.</summary>
    public bool Dhcp { get; init; }

    /// <summary>Addresses configured manually. DHCP, link-local and SLAAC addresses are excluded.</summary>
    public IReadOnlyList<IpCidr> ManualAddresses { get; init; } = [];

    /// <summary>Manually configured default gateways.</summary>
    public IReadOnlyList<IPAddress> ManualGateways { get; init; } = [];

    /// <summary>Statically configured DNS servers; empty means DNS comes from DHCP.</summary>
    public IReadOnlyList<IPAddress> StaticDnsServers { get; init; } = [];

    /// <summary>Interface metric set by hand; null when Windows chooses it (automatic metric).</summary>
    public int? ManualMetric { get; init; }

    /// <summary>The metric in use, automatic or not, for display only.</summary>
    public int InterfaceMetric { get; init; }

    /// <summary>Every address currently on the interface, for display only.</summary>
    public IReadOnlyList<IpCidr> AllAddresses { get; init; } = [];

    /// <summary>Every DNS server in use (static or from DHCP), for display only.</summary>
    public IReadOnlyList<IPAddress> EffectiveDnsServers { get; init; } = [];
}

/// <summary>
/// Internet connection sharing, machine-wide. The mobile hotspot uses Internet Connection Sharing too: while it is on,
/// <see cref="SharedFrom"/> is the connection it shares and <see cref="SharedTo"/> its own virtual Wi-Fi adapter.
/// </summary>
public sealed record SharingSnapshot
{
    public bool HotspotOn { get; init; }

    /// <summary>The adapter whose internet is shared (ICS "public" connection), if any.</summary>
    public Guid? SharedFrom { get; init; }

    /// <summary>The adapter that receives it (ICS "private" connection), if any.</summary>
    public Guid? SharedTo { get; init; }
}

/// <summary>The hosts file as NetHop sees it: the mappings in its own block.</summary>
public sealed record HostsSnapshot
{
    public IReadOnlyList<HostsMapping> Mappings { get; init; } = [];
}

public sealed record NetworkSnapshot
{
    public IReadOnlyList<AdapterSnapshot> Adapters { get; init; } = [];

    /// <summary>Null when it could not be read: plans then emit every sharing step.</summary>
    public SharingSnapshot? Sharing { get; init; }

    /// <summary>Null when the hosts file could not be read: plans then rewrite NetHop's block.</summary>
    public HostsSnapshot? Hosts { get; init; }

    public DateTimeOffset TakenAt { get; init; } = DateTimeOffset.Now;

    public AdapterSnapshot? Find(Guid adapterId) =>
        Adapters.FirstOrDefault(a => a.Info.Id == adapterId);
}
