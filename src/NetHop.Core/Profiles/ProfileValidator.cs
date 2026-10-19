using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NetHop.Core.Networking;

namespace NetHop.Core.Profiles;

public static partial class ProfileValidator
{
    public const int MaxNameLength = 64;

    public static IReadOnlyList<Issue> Validate(Profile profile)
    {
        var issues = new List<Issue>();

        if (profile.Schema < 1 || profile.Schema > NetHopConstants.CurrentSchemaVersion)
            issues.Add(Issue.Error("schema", $"Unsupported schema version {profile.Schema} (this NetHop understands up to {NetHopConstants.CurrentSchemaVersion})."));

        if (string.IsNullOrWhiteSpace(profile.Name))
            issues.Add(Issue.Error("name", "The profile needs a name."));
        else if (profile.Name.Length > MaxNameLength)
            issues.Add(Issue.Error("name", $"The name is longer than {MaxNameLength} characters."));

        if (profile.Id == Guid.Empty)
            issues.Add(Issue.Error("id", "The profile id must not be empty."));

        if (profile.Color is not null && !ColorRegex().IsMatch(profile.Color))
            issues.Add(Issue.Error("color", "Use the #RRGGBB format."));

        if (profile.Adapters.Count == 0)
            issues.Add(Issue.Error("adapters", "The profile does not configure any adapter."));

        for (var i = 0; i < profile.Adapters.Count; i++)
            ValidateAdapter(profile.Adapters[i], $"adapters[{i}]", issues);

        ValidateNoDuplicateMatches(profile, issues);
        return issues;
    }

    private static void ValidateAdapter(AdapterConfig adapter, string path, List<Issue> issues)
    {
        var match = adapter.Match;
        if (match.IsEmpty)
            issues.Add(Issue.Error($"{path}.match", "Set at least one of guid, mac or name."));
        if (match.Guid is not null && !Guid.TryParse(match.Guid, out _))
            issues.Add(Issue.Error($"{path}.match.guid", $"'{match.Guid}' is not a valid GUID."));
        if (match.Mac is not null && !MacAddress.TryNormalize(match.Mac, out _))
            issues.Add(Issue.Error($"{path}.match.mac", $"'{match.Mac}' is not a valid MAC address."));

        var nothingToDo = adapter.State == DesiredAdapterState.Unchanged && adapter.Ipv4 is null && adapter.Ipv6 is null;
        if (nothingToDo)
            issues.Add(Issue.Warning(path, "This adapter entry changes nothing."));

        if (adapter.State == DesiredAdapterState.Disabled && (adapter.Ipv4 is not null || adapter.Ipv6 is not null))
            issues.Add(Issue.Warning(path, "The adapter is disabled, so its IP settings will be ignored."));

        if (adapter.Ipv4 is not null)
            ValidateFamily(adapter.Ipv4, AddressFamily.InterNetwork, $"{path}.ipv4", issues);
        if (adapter.Ipv6 is not null)
            ValidateFamily(adapter.Ipv6, AddressFamily.InterNetworkV6, $"{path}.ipv6", issues);
    }

    private static void ValidateFamily(IpConfig config, AddressFamily family, string path, List<Issue> issues)
    {
        var familyName = family == AddressFamily.InterNetwork ? "IPv4" : "IPv6";

        var addresses = new List<IpCidr>();
        for (var i = 0; i < config.Addresses.Count; i++)
        {
            var itemPath = $"{path}.addresses[{i}]";
            if (!IpCidr.TryParse(config.Addresses[i], out var cidr))
            {
                issues.Add(Issue.Error(itemPath, $"'{config.Addresses[i]}' is not an address in CIDR notation (e.g. {(family == AddressFamily.InterNetwork ? "192.168.1.10/24" : "2001:db8::10/64")})."));
                continue;
            }
            if (cidr.Family != family)
            {
                issues.Add(Issue.Error(itemPath, $"'{cidr}' is not an {familyName} address."));
                continue;
            }
            if (cidr.IsNetworkOrBroadcastAddress())
                issues.Add(Issue.Warning(itemPath, $"'{cidr}' is the network or broadcast address of its subnet."));
            if (family == AddressFamily.InterNetworkV6 && cidr.Address.IsIPv6LinkLocal)
                issues.Add(Issue.Warning(itemPath, "Link-local addresses are assigned by Windows automatically."));
            if (addresses.Contains(cidr))
                issues.Add(Issue.Error(itemPath, $"'{cidr}' is listed twice."));
            addresses.Add(cidr);
        }

        var gateways = ParseAddresses(config.Gateways, family, familyName, $"{path}.gateways", issues);

        switch (config.Mode)
        {
            case IpMode.Static when config.Addresses.Count == 0:
                issues.Add(Issue.Error($"{path}.addresses", "Static mode needs at least one address."));
                break;
            case IpMode.Dhcp or IpMode.Unchanged when config.Addresses.Count > 0 || config.Gateways.Count > 0:
                issues.Add(Issue.Error(path, $"Addresses and gateways can only be set in static mode (mode is '{config.Mode.ToString().ToLowerInvariant()}')."));
                break;
        }

        if (family == AddressFamily.InterNetwork && addresses.Count > 0)
        {
            for (var i = 0; i < gateways.Count; i++)
            {
                if (!addresses.Any(a => a.Contains(gateways[i])))
                    issues.Add(Issue.Warning($"{path}.gateways[{i}]", $"Gateway {gateways[i]} is outside every configured subnet."));
            }
        }

        if (config.Dns is { } dns)
        {
            ParseAddresses(dns.Servers, family, familyName, $"{path}.dns.servers", issues);
            if (dns.Mode == DnsMode.Static && dns.Servers.Count == 0)
                issues.Add(Issue.Error($"{path}.dns", "Static DNS needs at least one server."));
            if (dns.Mode != DnsMode.Static && dns.Servers.Count > 0)
                issues.Add(Issue.Error($"{path}.dns", "DNS servers can only be listed in static mode."));
            if (dns.Mode == DnsMode.Dhcp && config.Mode == IpMode.Static && family == AddressFamily.InterNetwork)
                issues.Add(Issue.Warning($"{path}.dns", "With a static IPv4 address Windows gets no DNS servers from DHCP."));
        }

        if (config.Mode == IpMode.Unchanged && config.Dns is null)
            issues.Add(Issue.Warning(path, $"This {familyName} block changes nothing."));
    }

    private static List<IPAddress> ParseAddresses(List<string> values, AddressFamily family, string familyName, string path, List<Issue> issues)
    {
        var result = new List<IPAddress>();
        for (var i = 0; i < values.Count; i++)
        {
            if (!IPAddress.TryParse(values[i], out var address))
                issues.Add(Issue.Error($"{path}[{i}]", $"'{values[i]}' is not a valid IP address."));
            else if (address.AddressFamily != family)
                issues.Add(Issue.Error($"{path}[{i}]", $"'{values[i]}' is not an {familyName} address."));
            else if (result.Contains(address))
                issues.Add(Issue.Error($"{path}[{i}]", $"'{values[i]}' is listed twice."));
            else
                result.Add(address);
        }
        return result;
    }

    private static void ValidateNoDuplicateMatches(Profile profile, List<Issue> issues)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < profile.Adapters.Count; i++)
        {
            var match = profile.Adapters[i].Match;
            string? key = Guid.TryParse(match.Guid, out var g) ? "guid:" + g
                : MacAddress.TryNormalize(match.Mac, out var m) ? "mac:" + m
                : string.IsNullOrWhiteSpace(match.Name) ? null : "name:" + match.Name.Trim();
            if (key is null)
                continue;
            if (seen.TryGetValue(key, out var first))
                issues.Add(Issue.Error($"adapters[{i}].match", $"Same adapter as adapters[{first}]."));
            else
                seen[key] = i;
        }
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorRegex();
}
