using NetHop.Core.Networking;
using NetHop.Core.Profiles;

namespace NetHop.Core.Planning;

/// <summary>
/// Captures the current state of some adapters as a profile. Used for rollback after a failed apply
/// and for "create a profile from the current configuration" in the UI.
/// </summary>
public static class SnapshotProfile
{
    public static Profile Create(NetworkSnapshot snapshot, IEnumerable<Guid> adapterIds, string name)
    {
        var ids = adapterIds.ToHashSet();
        var profile = new Profile { Name = name };

        foreach (var adapter in snapshot.Adapters.Where(a => ids.Contains(a.Info.Id)))
        {
            profile.Adapters.Add(new AdapterConfig
            {
                Match = AdapterMatcher.CreateMatch(adapter.Info),
                State = adapter.Enabled ? DesiredAdapterState.Enabled : DesiredAdapterState.Disabled,
                Ipv4 = adapter.Enabled ? FromFamily(adapter.Ipv4) : null,
                Ipv6 = adapter.Enabled ? FromFamily(adapter.Ipv6) : null,
            });
        }

        return profile;
    }

    /// <summary>NetHop's block of the hosts file as a profile section, so a failed apply can restore it. Null when unknown.</summary>
    public static HostsConfig? CreateHosts(NetworkSnapshot snapshot)
    {
        if (snapshot.Hosts is not { } hosts)
            return null;
        if (hosts.Mappings.Count == 0)
            return new HostsConfig { Mode = HostsMode.Clear };
        return new HostsConfig
        {
            Mode = HostsMode.Set,
            Entries = hosts.Mappings
                .GroupBy(m => m.Address)
                .Select(g => new HostsEntry { Ip = g.Key.ToString(), Names = g.Select(m => m.Name).ToList() })
                .ToList(),
        };
    }

    private static IpConfig? FromFamily(FamilySnapshot? family)
    {
        if (family is null)
            return null;

        return new IpConfig
        {
            Mode = family.Dhcp ? IpMode.Dhcp : IpMode.Static,
            Addresses = family.Dhcp ? [] : family.ManualAddresses.Select(a => a.ToString()).ToList(),
            Gateways = family.Dhcp ? [] : family.ManualGateways.Select(g => g.ToString()).ToList(),
            Dns = family.StaticDnsServers.Count > 0
                ? new DnsConfig { Mode = DnsMode.Static, Servers = family.StaticDnsServers.Select(s => s.ToString()).ToList() }
                : new DnsConfig { Mode = DnsMode.Dhcp },
            Metric = family.ManualMetric?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? IpConfig.AutomaticMetric,
        };
    }
}
