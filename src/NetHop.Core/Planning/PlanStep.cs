using System.Net;
using System.Net.Sockets;
using NetHop.Core.Networking;

namespace NetHop.Core.Planning;

/// <summary>One atomic change to the network configuration. Executed by the engine in plan order.</summary>
public abstract record PlanStep
{
    public abstract string Describe();

    protected static string FamilyName(AddressFamily family) =>
        family == AddressFamily.InterNetwork ? "IPv4" : "IPv6";
}

/// <summary>A change to one adapter. A failed apply restores the adapters these steps touched.</summary>
public abstract record AdapterStep(AdapterInfo Adapter) : PlanStep;

public sealed record EnableAdapterStep(AdapterInfo Adapter) : AdapterStep(Adapter)
{
    public override string Describe() => Text.Format("Step_Enable", Adapter.Name);
}

/// <summary>Waits until a just-enabled adapter has its IP interfaces available.</summary>
public sealed record WaitForAdapterStep(AdapterInfo Adapter) : AdapterStep(Adapter)
{
    public override string Describe() => Text.Format("Step_Wait", Adapter.Name);
}

public sealed record DisableAdapterStep(AdapterInfo Adapter) : AdapterStep(Adapter)
{
    public override string Describe() => Text.Format("Step_Disable", Adapter.Name);
}

public sealed record SetDhcpStep(AdapterInfo Adapter, AddressFamily Family, bool Enabled) : AdapterStep(Adapter)
{
    public override string Describe() =>
        Text.Format(Enabled ? "Step_DhcpOn" : "Step_DhcpOff", FamilyName(Family), Adapter.Name);
}

public sealed record AddAddressStep(AdapterInfo Adapter, IpCidr Address) : AdapterStep(Adapter)
{
    public override string Describe() => Text.Format("Step_AddAddress", Address, Adapter.Name);
}

public sealed record RemoveAddressStep(AdapterInfo Adapter, IpCidr Address) : AdapterStep(Adapter)
{
    public override string Describe() => Text.Format("Step_RemoveAddress", Address, Adapter.Name);
}

public sealed record AddGatewayStep(AdapterInfo Adapter, IPAddress Gateway) : AdapterStep(Adapter)
{
    public override string Describe() => Text.Format("Step_AddGateway", Gateway, Adapter.Name);
}

public sealed record RemoveGatewayStep(AdapterInfo Adapter, IPAddress Gateway) : AdapterStep(Adapter)
{
    public override string Describe() => Text.Format("Step_RemoveGateway", Gateway, Adapter.Name);
}

/// <summary>Sets static DNS servers; an empty list returns DNS to automatic (DHCP).</summary>
public sealed record SetDnsStep(AdapterInfo Adapter, AddressFamily Family, IReadOnlyList<IPAddress> Servers) : AdapterStep(Adapter)
{
    public override string Describe() => Servers.Count == 0
        ? Text.Format("Step_DnsAuto", FamilyName(Family), Adapter.Name)
        : Text.Format("Step_DnsSet", FamilyName(Family), Adapter.Name, string.Join(", ", Servers));
}

/// <summary>Sets the interface metric; null returns it to automatic.</summary>
public sealed record SetMetricStep(AdapterInfo Adapter, AddressFamily Family, int? Metric) : AdapterStep(Adapter)
{
    public override string Describe() => Metric is { } metric
        ? Text.Format("Step_MetricSet", FamilyName(Family), Adapter.Name, metric)
        : Text.Format("Step_MetricAuto", FamilyName(Family), Adapter.Name);
}

/// <summary>
/// Turns the mobile hotspot on, sharing <paramref name="Source"/> (null: the connection Windows uses for the internet).
/// When the hotspot is already on with another source, it switches to this one.
/// </summary>
public sealed record ShareThroughHotspotStep(AdapterInfo? Source) : PlanStep
{
    public override string Describe() => Source is null
        ? Text.Get("Step_HotspotOn")
        : Text.Format("Step_HotspotShare", Source.Name);
}

public sealed record StopHotspotStep : PlanStep
{
    public override string Describe() => Text.Get("Step_HotspotOff");
}

/// <summary>Internet Connection Sharing from <paramref name="Source"/> to <paramref name="Target"/>; any other sharing stops.</summary>
public sealed record ShareWithAdapterStep(AdapterInfo Source, AdapterInfo Target) : PlanStep
{
    public override string Describe() => Text.Format("Step_ShareAdapter", Source.Name, Target.Name);
}

/// <summary>Turns Internet Connection Sharing off on every adapter.</summary>
public sealed record StopSharingStep : PlanStep
{
    public override string Describe() => Text.Get("Step_SharingOff");
}

public sealed record ApplyPlan(IReadOnlyList<PlanStep> Steps, IReadOnlyList<Issue> Issues, int MissingAdapters = 0)
{
    /// <summary>The network already matches the profile.</summary>
    public bool IsNoOp => Steps.Count == 0;

    public bool HasErrors => Issues.HasErrors();

    /// <summary>Adapters touched by at least one step.</summary>
    public IReadOnlyList<AdapterInfo> AffectedAdapters =>
        Steps.OfType<AdapterStep>().Select(s => s.Adapter).DistinctBy(a => a.Id).ToList();

    /// <summary>The plan changes the mobile hotspot or Internet Connection Sharing.</summary>
    public bool ChangesSharing =>
        Steps.Any(s => s is ShareThroughHotspotStep or StopHotspotStep or ShareWithAdapterStep or StopSharingStep);
}
