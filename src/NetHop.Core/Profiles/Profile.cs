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

    /// <summary>
    /// Shown in the UI and the tray flyout: a Segoe Fluent Icons glyph, or a custom image as a PNG data URI
    /// (<c>data:image/png;base64,...</c>, see <see cref="ImageIconPrefix"/>) so it travels with exported files.
    /// </summary>
    public string? Icon { get; set; }

    public const string ImageIconPrefix = "data:image/png;base64,";

    /// <summary>Largest custom icon accepted, in bytes of PNG data. The app stores 64×64 images, well below it.</summary>
    public const int MaxImageIconBytes = 48 * 1024;

    public static bool IsImageIcon(string? icon) => icon?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Accent color as <c>#RRGGBB</c>.</summary>
    public string? Color { get; set; }

    public List<AdapterConfig> Adapters { get; set; } = [];

    /// <summary>Internet connection sharing and the mobile hotspot. Null leaves them as they are.</summary>
    public SharingConfig? Sharing { get; set; }
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

/// <summary>
/// Shares one connection's internet: through the Windows mobile hotspot, or with another adapter (Internet Connection
/// Sharing). Machine-wide, so it belongs to the profile rather than to an adapter: Windows shares one connection at a time.
/// </summary>
public sealed class SharingConfig
{
    public SharingMode Mode { get; set; } = SharingMode.Unchanged;

    /// <summary>The connection whose internet is shared, e.g. a VPN adapter. Optional for the hotspot: Windows' choice.</summary>
    public AdapterMatch? From { get; set; }

    /// <summary>The adapter that receives the connection; <see cref="SharingMode.Adapter"/> only.</summary>
    public AdapterMatch? To { get; set; }
}

public enum SharingMode
{
    Unchanged,

    /// <summary>Mobile hotspot off and no Internet Connection Sharing.</summary>
    Disabled,

    /// <summary>The Windows mobile hotspot on, sharing <see cref="SharingConfig.From"/>.</summary>
    Hotspot,

    /// <summary>Internet Connection Sharing from <see cref="SharingConfig.From"/> to <see cref="SharingConfig.To"/>.</summary>
    Adapter,
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

    /// <summary>
    /// Interface metric: <see cref="AutomaticMetric"/>, or a number from 1 to <see cref="MaxMetric"/>. When several
    /// adapters reach the same network, Windows prefers the lowest metric. Null leaves it as it is.
    /// </summary>
    public string? Metric { get; set; }

    public const string AutomaticMetric = "automatic";

    public const int MaxMetric = 9999;

    /// <summary>Reads a <see cref="Metric"/> value: <paramref name="metric"/> is null for automatic.</summary>
    public static bool TryParseMetric(string? value, out int? metric)
    {
        metric = null;
        if (string.Equals(value?.Trim(), AutomaticMetric, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
            || number is < 1 or > MaxMetric)
            return false;
        metric = number;
        return true;
    }
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
