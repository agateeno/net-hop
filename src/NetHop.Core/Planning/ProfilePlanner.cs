using System.Net;
using System.Net.Sockets;
using NetHop.Core.Networking;
using NetHop.Core.Profiles;

namespace NetHop.Core.Planning;

/// <summary>
/// Turns a profile into the minimal list of steps that moves the current network state to the desired one.
/// Pure logic, no Windows calls: the engine executes the plan.
/// </summary>
public static class ProfilePlanner
{
    /// <remarks>
    /// Order: enable adapters (and wait for them), then IP configuration, then disable adapters.
    /// This way the PC is never left without the adapters the profile wants up.
    /// </remarks>
    public static ApplyPlan Plan(Profile profile, NetworkSnapshot snapshot)
    {
        var issues = new List<Issue>();
        var enables = new List<PlanStep>();
        var ipSteps = new List<PlanStep>();
        var disables = new List<PlanStep>();
        var adapters = snapshot.Adapters.Select(a => a.Info).ToList();
        var resolved = new HashSet<Guid>();
        var missing = 0;

        for (var i = 0; i < profile.Adapters.Count; i++)
        {
            var config = profile.Adapters[i];
            var path = $"adapters[{i}]";

            var match = AdapterMatcher.Resolve(config.Match, adapters);
            if (!match.Found)
            {
                issues.Add(Issue.Warning(path, $"{match.Problem} Skipped."));
                missing++;
                continue;
            }
            if (!resolved.Add(match.Adapter!.Id))
            {
                issues.Add(Issue.Error(path, $"Adapter '{match.Adapter.Name}' is configured more than once."));
                continue;
            }

            var current = snapshot.Find(match.Adapter.Id)!;
            var adapter = current.Info;

            // IP state of an adapter that is enabled by this plan is unknown until it comes up.
            var willBeEnabled = config.State switch
            {
                DesiredAdapterState.Enabled => true,
                DesiredAdapterState.Disabled => false,
                _ => current.Enabled,
            };
            var ipStateKnown = current.Enabled;

            if (config.State == DesiredAdapterState.Enabled && !current.Enabled)
            {
                enables.Add(new EnableAdapterStep(adapter));
                enables.Add(new WaitForAdapterStep(adapter));
            }

            if (config.State == DesiredAdapterState.Disabled && current.Enabled)
                disables.Add(new DisableAdapterStep(adapter));

            if (!willBeEnabled)
            {
                if (config.Ipv4 is not null || config.Ipv6 is not null)
                    issues.Add(Issue.Warning(path, $"Adapter '{adapter.Name}' is disabled, IP settings skipped."));
                continue;
            }

            if (config.Ipv4 is not null)
                PlanFamily(adapter, config.Ipv4, AddressFamily.InterNetwork, ipStateKnown ? current.Ipv4 : null, ipSteps);
            if (config.Ipv6 is not null)
                PlanFamily(adapter, config.Ipv6, AddressFamily.InterNetworkV6, ipStateKnown ? current.Ipv6 : null, ipSteps);
        }

        return new ApplyPlan([.. enables, .. ipSteps, .. disables], issues, missing);
    }

    /// <param name="current">Null when the current state is unknown: every step is emitted (the engine treats them as idempotent).</param>
    private static void PlanFamily(AdapterInfo adapter, IpConfig desired, AddressFamily family, FamilySnapshot? current, List<PlanStep> steps)
    {
        var currentAddresses = current?.ManualAddresses ?? [];
        var currentGateways = current?.ManualGateways ?? [];

        switch (desired.Mode)
        {
            case IpMode.Static:
            {
                if (current is null || current.Dhcp)
                    steps.Add(new SetDhcpStep(adapter, family, Enabled: false));

                var wantedAddresses = desired.Addresses.Select(IpCidr.Parse).ToList();
                var wantedGateways = desired.Gateways.Select(IPAddress.Parse).ToList();

                // Gateways go first on removal and last on addition: Windows rejects a gateway
                // whose subnet is not configured on the interface yet.
                steps.AddRange(currentGateways.Except(wantedGateways).Select(g => new RemoveGatewayStep(adapter, g)));
                steps.AddRange(currentAddresses.Except(wantedAddresses).Select(a => new RemoveAddressStep(adapter, a)));
                steps.AddRange(wantedAddresses.Except(currentAddresses).Select(a => new AddAddressStep(adapter, a)));
                steps.AddRange(wantedGateways.Except(currentGateways).Select(g => new AddGatewayStep(adapter, g)));
                break;
            }
            case IpMode.Dhcp:
                steps.AddRange(currentGateways.Select(g => new RemoveGatewayStep(adapter, g)));
                steps.AddRange(currentAddresses.Select(a => new RemoveAddressStep(adapter, a)));
                if (current is null || !current.Dhcp)
                    steps.Add(new SetDhcpStep(adapter, family, Enabled: true));
                break;
        }

        var dnsMode = desired.Dns?.Mode ?? (desired.Mode == IpMode.Dhcp ? DnsMode.Dhcp : DnsMode.Unchanged);
        var currentDns = current?.StaticDnsServers ?? [];
        switch (dnsMode)
        {
            case DnsMode.Static:
            {
                var wanted = desired.Dns!.Servers.Select(IPAddress.Parse).ToList();
                // Order matters for DNS: the first server is the preferred one.
                if (current is null || !currentDns.SequenceEqual(wanted))
                    steps.Add(new SetDnsStep(adapter, family, wanted));
                break;
            }
            case DnsMode.Dhcp when current is null || currentDns.Count > 0:
                steps.Add(new SetDnsStep(adapter, family, []));
                break;
        }
    }

    /// <summary>True when applying the profile would change nothing, i.e. it is the active one.</summary>
    public static bool IsSatisfied(Profile profile, NetworkSnapshot snapshot)
    {
        var plan = Plan(profile, snapshot);
        return plan.IsNoOp && !plan.HasErrors && plan.MissingAdapters == 0;
    }
}
