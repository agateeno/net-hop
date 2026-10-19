using System.Net;
using System.Net.Sockets;
using NetHop.Core.Networking;

namespace NetHop.Core.Planning;

/// <summary>One atomic change to the network configuration. Executed by the engine in plan order.</summary>
public abstract record PlanStep(AdapterInfo Adapter)
{
    public abstract string Describe();

    protected static string FamilyName(AddressFamily family) =>
        family == AddressFamily.InterNetwork ? "IPv4" : "IPv6";
}

public sealed record EnableAdapterStep(AdapterInfo Adapter) : PlanStep(Adapter)
{
    public override string Describe() => $"Enable adapter '{Adapter.Name}'";
}

/// <summary>Waits until a just-enabled adapter has its IP interfaces available.</summary>
public sealed record WaitForAdapterStep(AdapterInfo Adapter) : PlanStep(Adapter)
{
    public override string Describe() => $"Wait for adapter '{Adapter.Name}' to come up";
}

public sealed record DisableAdapterStep(AdapterInfo Adapter) : PlanStep(Adapter)
{
    public override string Describe() => $"Disable adapter '{Adapter.Name}'";
}

public sealed record SetDhcpStep(AdapterInfo Adapter, AddressFamily Family, bool Enabled) : PlanStep(Adapter)
{
    public override string Describe() =>
        $"{(Enabled ? "Enable" : "Disable")} automatic {FamilyName(Family)} configuration on '{Adapter.Name}'";
}

public sealed record AddAddressStep(AdapterInfo Adapter, IpCidr Address) : PlanStep(Adapter)
{
    public override string Describe() => $"Add address {Address} to '{Adapter.Name}'";
}

public sealed record RemoveAddressStep(AdapterInfo Adapter, IpCidr Address) : PlanStep(Adapter)
{
    public override string Describe() => $"Remove address {Address} from '{Adapter.Name}'";
}

public sealed record AddGatewayStep(AdapterInfo Adapter, IPAddress Gateway) : PlanStep(Adapter)
{
    public override string Describe() => $"Add default gateway {Gateway} to '{Adapter.Name}'";
}

public sealed record RemoveGatewayStep(AdapterInfo Adapter, IPAddress Gateway) : PlanStep(Adapter)
{
    public override string Describe() => $"Remove default gateway {Gateway} from '{Adapter.Name}'";
}

/// <summary>Sets static DNS servers; an empty list returns DNS to automatic (DHCP).</summary>
public sealed record SetDnsStep(AdapterInfo Adapter, AddressFamily Family, IReadOnlyList<IPAddress> Servers) : PlanStep(Adapter)
{
    public override string Describe() => Servers.Count == 0
        ? $"Use automatic {FamilyName(Family)} DNS on '{Adapter.Name}'"
        : $"Set {FamilyName(Family)} DNS on '{Adapter.Name}' to {string.Join(", ", Servers)}";
}

public sealed record ApplyPlan(IReadOnlyList<PlanStep> Steps, IReadOnlyList<Issue> Issues, int MissingAdapters = 0)
{
    /// <summary>The network already matches the profile.</summary>
    public bool IsNoOp => Steps.Count == 0;

    public bool HasErrors => Issues.HasErrors();

    /// <summary>Adapters touched by at least one step.</summary>
    public IReadOnlyList<AdapterInfo> AffectedAdapters =>
        Steps.Select(s => s.Adapter).DistinctBy(a => a.Id).ToList();
}
