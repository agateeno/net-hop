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
            issues.Add(Issue.Error("schema", Text.Format("Profile_SchemaUnsupported", profile.Schema, NetHopConstants.CurrentSchemaVersion)));

        if (string.IsNullOrWhiteSpace(profile.Name))
            issues.Add(Issue.Error("name", Text.Get("Profile_NameRequired")));
        else if (profile.Name.Length > MaxNameLength)
            issues.Add(Issue.Error("name", Text.Format("Profile_NameTooLong", MaxNameLength)));

        if (profile.Id == Guid.Empty)
            issues.Add(Issue.Error("id", Text.Get("Profile_IdEmpty")));

        if (profile.Color is not null && !ColorRegex().IsMatch(profile.Color))
            issues.Add(Issue.Error("color", Text.Get("Profile_ColorFormat")));

        if (Profile.IsImageIcon(profile.Icon) && !IsValidImageIcon(profile.Icon!))
            issues.Add(Issue.Error("icon", Text.Format("Profile_IconFormat", Profile.MaxImageIconBytes / 1024)));

        if (profile.Adapters.Count == 0)
            issues.Add(Issue.Error("adapters", Text.Get("Profile_NoAdapters")));

        for (var i = 0; i < profile.Adapters.Count; i++)
            ValidateAdapter(profile.Adapters[i], $"adapters[{i}]", issues);

        ValidateNoDuplicateMatches(profile, issues);
        return issues;
    }

    private static void ValidateAdapter(AdapterConfig adapter, string path, List<Issue> issues)
    {
        var match = adapter.Match;
        if (match.IsEmpty)
            issues.Add(Issue.Error($"{path}.match", Text.Get("Profile_MatchEmpty")));
        if (match.Guid is not null && !Guid.TryParse(match.Guid, out _))
            issues.Add(Issue.Error($"{path}.match.guid", Text.Format("Profile_GuidInvalid", match.Guid)));
        if (match.Mac is not null && !MacAddress.TryNormalize(match.Mac, out _))
            issues.Add(Issue.Error($"{path}.match.mac", Text.Format("Profile_MacInvalid", match.Mac)));

        var nothingToDo = adapter.State == DesiredAdapterState.Unchanged && adapter.Ipv4 is null && adapter.Ipv6 is null;
        if (nothingToDo)
            issues.Add(Issue.Warning(path, Text.Get("Profile_AdapterChangesNothing")));

        if (adapter.State == DesiredAdapterState.Disabled && (adapter.Ipv4 is not null || adapter.Ipv6 is not null))
            issues.Add(Issue.Warning(path, Text.Get("Profile_DisabledIgnoresIp")));

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
                issues.Add(Issue.Error(itemPath, Text.Format("Profile_CidrInvalid", config.Addresses[i], family == AddressFamily.InterNetwork ? "192.168.1.10/24" : "2001:db8::10/64")));
                continue;
            }
            if (cidr.Family != family)
            {
                issues.Add(Issue.Error(itemPath, Text.Format("Profile_WrongFamily", cidr, familyName)));
                continue;
            }
            if (cidr.IsNetworkOrBroadcastAddress())
                issues.Add(Issue.Warning(itemPath, Text.Format("Profile_NetworkOrBroadcast", cidr)));
            if (family == AddressFamily.InterNetworkV6 && cidr.Address.IsIPv6LinkLocal)
                issues.Add(Issue.Warning(itemPath, Text.Get("Profile_LinkLocal")));
            if (addresses.Contains(cidr))
                issues.Add(Issue.Error(itemPath, Text.Format("Profile_Duplicate", cidr)));
            addresses.Add(cidr);
        }

        var gateways = ParseAddresses(config.Gateways, family, familyName, $"{path}.gateways", issues);

        switch (config.Mode)
        {
            case IpMode.Static when config.Addresses.Count == 0:
                issues.Add(Issue.Error($"{path}.addresses", Text.Get("Profile_StaticNeedsAddress")));
                break;
            case IpMode.Dhcp or IpMode.Unchanged when config.Addresses.Count > 0 || config.Gateways.Count > 0:
                issues.Add(Issue.Error(path, Text.Format("Profile_AddressesOnlyStatic", config.Mode.ToString().ToLowerInvariant())));
                break;
        }

        if (family == AddressFamily.InterNetwork && addresses.Count > 0)
        {
            for (var i = 0; i < gateways.Count; i++)
            {
                if (!addresses.Any(a => a.Contains(gateways[i])))
                    issues.Add(Issue.Warning($"{path}.gateways[{i}]", Text.Format("Profile_GatewayOutsideSubnet", gateways[i])));
            }
        }

        if (config.Dns is { } dns)
        {
            ParseAddresses(dns.Servers, family, familyName, $"{path}.dns.servers", issues);
            if (dns.Mode == DnsMode.Static && dns.Servers.Count == 0)
                issues.Add(Issue.Error($"{path}.dns", Text.Get("Profile_StaticDnsNeedsServer")));
            if (dns.Mode != DnsMode.Static && dns.Servers.Count > 0)
                issues.Add(Issue.Error($"{path}.dns", Text.Get("Profile_DnsServersOnlyStatic")));
            if (dns.Mode == DnsMode.Dhcp && config.Mode == IpMode.Static && family == AddressFamily.InterNetwork)
                issues.Add(Issue.Warning($"{path}.dns", Text.Get("Profile_StaticIpv4NoDhcpDns")));
        }

        if (config.Metric is not null && !IpConfig.TryParseMetric(config.Metric, out _))
            issues.Add(Issue.Error($"{path}.metric", Text.Format("Profile_MetricInvalid", config.Metric, IpConfig.MaxMetric)));

        if (config.Mode == IpMode.Unchanged && config.Dns is null && config.Metric is null)
            issues.Add(Issue.Warning(path, Text.Format("Profile_FamilyChangesNothing", familyName)));
    }

    private static List<IPAddress> ParseAddresses(List<string> values, AddressFamily family, string familyName, string path, List<Issue> issues)
    {
        var result = new List<IPAddress>();
        for (var i = 0; i < values.Count; i++)
        {
            if (!IPAddress.TryParse(values[i], out var address))
                issues.Add(Issue.Error($"{path}[{i}]", Text.Format("Profile_IpInvalid", values[i])));
            else if (address.AddressFamily != family)
                issues.Add(Issue.Error($"{path}[{i}]", Text.Format("Profile_WrongFamily", values[i], familyName)));
            else if (result.Contains(address))
                issues.Add(Issue.Error($"{path}[{i}]", Text.Format("Profile_Duplicate", values[i])));
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
                issues.Add(Issue.Error($"adapters[{i}].match", Text.Format("Profile_SameAdapter", first)));
            else
                seen[key] = i;
        }
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static bool IsValidImageIcon(string icon)
    {
        if (!icon.StartsWith(Profile.ImageIconPrefix, StringComparison.Ordinal))
            return false;
        var data = icon.AsSpan(Profile.ImageIconPrefix.Length);
        if (data.Length > (Profile.MaxImageIconBytes + 2) / 3 * 4)
            return false;
        var bytes = new byte[data.Length];
        return Convert.TryFromBase64Chars(data, bytes, out var length)
            && length >= PngSignature.Length
            && bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature);
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorRegex();
}
