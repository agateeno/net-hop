using System.Net;
using System.Net.Sockets;
using NetHop.Core.Networking;
using NetHop.Core.Planning;
using NetHop.Core.Profiles;
using static NetHop.Core.Tests.TestData;

namespace NetHop.Core.Tests;

public class ProfilePlannerTests
{
    private static Profile StaticEthernet(params string[] addresses) => new()
    {
        Name = "Static",
        Adapters =
        [
            new AdapterConfig
            {
                Match = new AdapterMatch { Name = "Ethernet" },
                Ipv4 = new IpConfig
                {
                    Mode = IpMode.Static,
                    Addresses = [.. addresses],
                    Gateways = ["192.168.1.1"],
                    Dns = new DnsConfig { Mode = DnsMode.Static, Servers = ["192.168.1.1", "1.1.1.1"] },
                },
            },
        ],
    };

    [Fact]
    public void Dhcp_to_static_disables_dhcp_then_adds_addresses_gateway_and_dns()
    {
        var plan = ProfilePlanner.Plan(StaticEthernet("192.168.1.10/24", "192.168.1.11/24"), Snapshot(Up(Ethernet)));

        Assert.Collection(plan.Steps,
            s => Assert.Equal(new SetDhcpStep(Ethernet, AddressFamily.InterNetwork, false), s),
            s => Assert.Equal(IpCidr.Parse("192.168.1.10/24"), Assert.IsType<AddAddressStep>(s).Address),
            s => Assert.Equal(IpCidr.Parse("192.168.1.11/24"), Assert.IsType<AddAddressStep>(s).Address),
            s => Assert.Equal(IPAddress.Parse("192.168.1.1"), Assert.IsType<AddGatewayStep>(s).Gateway),
            s => Assert.Equal(2, Assert.IsType<SetDnsStep>(s).Servers.Count));
        Assert.Empty(plan.Issues);
    }

    [Fact]
    public void Static_to_static_only_changes_the_difference()
    {
        var current = Snapshot(Up(Ethernet, Static(["192.168.1.10/24", "192.168.1.99/24"], ["192.168.1.1"], ["192.168.1.1", "1.1.1.1"])));

        var plan = ProfilePlanner.Plan(StaticEthernet("192.168.1.10/24", "192.168.1.11/24"), current);

        Assert.Collection(plan.Steps,
            s => Assert.Equal(IpCidr.Parse("192.168.1.99/24"), Assert.IsType<RemoveAddressStep>(s).Address),
            s => Assert.Equal(IpCidr.Parse("192.168.1.11/24"), Assert.IsType<AddAddressStep>(s).Address));
    }

    [Fact]
    public void Matching_state_is_a_no_op_and_profile_is_active()
    {
        var profile = StaticEthernet("192.168.1.10/24");
        var current = Snapshot(Up(Ethernet, Static(["192.168.1.10/24"], ["192.168.1.1"], ["192.168.1.1", "1.1.1.1"])));

        Assert.True(ProfilePlanner.Plan(profile, current).IsNoOp);
        Assert.True(ProfilePlanner.IsSatisfied(profile, current));
    }

    [Fact]
    public void Dns_order_matters()
    {
        var current = Snapshot(Up(Ethernet, Static(["192.168.1.10/24"], ["192.168.1.1"], ["1.1.1.1", "192.168.1.1"])));

        var plan = ProfilePlanner.Plan(StaticEthernet("192.168.1.10/24"), current);

        Assert.IsType<SetDnsStep>(Assert.Single(plan.Steps));
    }

    [Fact]
    public void Static_to_dhcp_removes_manual_config_and_resets_dns()
    {
        var profile = new Profile
        {
            Name = "Dhcp",
            Adapters = [new AdapterConfig { Match = new AdapterMatch { Name = "Ethernet" }, Ipv4 = new IpConfig { Mode = IpMode.Dhcp } }],
        };
        var current = Snapshot(Up(Ethernet, Static(["192.168.1.10/24"], ["192.168.1.1"], ["1.1.1.1"])));

        var plan = ProfilePlanner.Plan(profile, current);

        Assert.Collection(plan.Steps,
            s => Assert.IsType<RemoveGatewayStep>(s),
            s => Assert.IsType<RemoveAddressStep>(s),
            s => Assert.Equal(new SetDhcpStep(Ethernet, AddressFamily.InterNetwork, true), s),
            s => Assert.Empty(Assert.IsType<SetDnsStep>(s).Servers));
    }

    [Fact]
    public void Enables_come_first_and_disables_last()
    {
        var profile = new Profile
        {
            Name = "Swap",
            Adapters =
            [
                new AdapterConfig { Match = new AdapterMatch { Name = "Wi-Fi" }, State = DesiredAdapterState.Disabled },
                new AdapterConfig
                {
                    Match = new AdapterMatch { Name = "Ethernet" },
                    State = DesiredAdapterState.Enabled,
                    Ipv4 = new IpConfig { Mode = IpMode.Dhcp },
                },
            ],
        };

        var plan = ProfilePlanner.Plan(profile, Snapshot(Up(WiFi), Disabled(Ethernet)));

        Assert.IsType<EnableAdapterStep>(plan.Steps[0]);
        Assert.IsType<WaitForAdapterStep>(plan.Steps[1]);
        // IP state of a just-enabled adapter is unknown, so the full config is emitted.
        Assert.Contains(plan.Steps, s => s is SetDhcpStep { Enabled: true });
        Assert.Contains(plan.Steps, s => s is SetDnsStep { Servers.Count: 0 });
        Assert.Equal(new DisableAdapterStep(WiFi), plan.Steps[^1]);
    }

    [Fact]
    public void Ipv6_static_is_planned_independently_of_ipv4()
    {
        var profile = new Profile
        {
            Name = "v6",
            Adapters =
            [
                new AdapterConfig
                {
                    Match = new AdapterMatch { Name = "Ethernet" },
                    Ipv6 = new IpConfig { Mode = IpMode.Static, Addresses = ["2001:db8::10/64"], Gateways = ["fe80::1"] },
                },
            ],
        };

        var plan = ProfilePlanner.Plan(profile, Snapshot(Up(Ethernet)));

        Assert.All(plan.Steps, s => Assert.True(s is not SetDhcpStep d || d.Family == AddressFamily.InterNetworkV6));
        Assert.Contains(plan.Steps, s => s is AddAddressStep a && a.Address.IsIPv6);
        Assert.Contains(plan.Steps, s => s is AddGatewayStep g && g.Gateway.Equals(IPAddress.Parse("fe80::1")));
        Assert.DoesNotContain(plan.Steps, s => s is SetDnsStep); // DNS not mentioned, static mode: unchanged
    }

    [Fact]
    public void Missing_adapter_is_a_warning_and_profile_is_not_active()
    {
        var profile = new Profile
        {
            Name = "Missing",
            Adapters = [new AdapterConfig { Match = new AdapterMatch { Name = "USB Ethernet" }, State = DesiredAdapterState.Enabled }],
        };
        var snapshot = Snapshot(Up(Ethernet));

        var plan = ProfilePlanner.Plan(profile, snapshot);

        Assert.True(plan.IsNoOp);
        Assert.Equal(1, plan.MissingAdapters);
        Assert.Equal(IssueSeverity.Warning, Assert.Single(plan.Issues).Severity);
        Assert.False(ProfilePlanner.IsSatisfied(profile, snapshot));
    }

    [Fact]
    public void Ip_settings_on_a_disabled_adapter_are_skipped()
    {
        var profile = new Profile
        {
            Name = "Off",
            Adapters = [new AdapterConfig { Match = new AdapterMatch { Name = "Ethernet" }, Ipv4 = new IpConfig { Mode = IpMode.Dhcp } }],
        };

        var plan = ProfilePlanner.Plan(profile, Snapshot(Disabled(Ethernet)));

        Assert.True(plan.IsNoOp);
        Assert.Single(plan.Issues);
    }

    [Fact]
    public void Snapshot_profile_restores_the_previous_state()
    {
        var before = Snapshot(Up(Ethernet, Static(["192.168.1.10/24"], ["192.168.1.1"], ["1.1.1.1"])), Up(WiFi));
        var rollback = SnapshotProfile.Create(before, [Ethernet.Id, WiFi.Id], "Rollback");

        Assert.True(ProfilePlanner.IsSatisfied(rollback, before));

        // After a profile switched Ethernet to DHCP and disabled Wi-Fi, the rollback undoes both.
        var after = Snapshot(Up(Ethernet), Disabled(WiFi));
        var plan = ProfilePlanner.Plan(rollback, after);
        Assert.Contains(plan.Steps, s => s is EnableAdapterStep e && e.Adapter == WiFi);
        Assert.Contains(plan.Steps, s => s is AddAddressStep a && a.Address == IpCidr.Parse("192.168.1.10/24"));
        Assert.Contains(plan.Steps, s => s is SetDnsStep d && d.Adapter == Ethernet && d.Servers.Count == 1);
    }

    private static Profile EthernetMetric(string metric) => new()
    {
        Name = "Metric",
        Adapters = [new AdapterConfig { Match = new AdapterMatch { Name = "Ethernet" }, Ipv4 = new IpConfig { Metric = metric } }],
    };

    [Fact]
    public void Metric_is_set_only_when_it_differs()
    {
        var automatic = Snapshot(Up(Ethernet));
        var manual = Snapshot(Up(Ethernet, Dhcp() with { ManualMetric = 25 }));

        Assert.Equal(new SetMetricStep(Ethernet, AddressFamily.InterNetwork, 25), Assert.Single(ProfilePlanner.Plan(EthernetMetric("25"), automatic).Steps));
        Assert.True(ProfilePlanner.IsSatisfied(EthernetMetric("25"), manual));
        Assert.Equal(new SetMetricStep(Ethernet, AddressFamily.InterNetwork, null), Assert.Single(ProfilePlanner.Plan(EthernetMetric("automatic"), manual).Steps));
        Assert.True(ProfilePlanner.IsSatisfied(EthernetMetric("automatic"), automatic));
    }

    [Fact]
    public void Snapshot_profile_restores_the_metric()
    {
        var before = Snapshot(Up(Ethernet, Dhcp() with { ManualMetric = 5 }));
        var rollback = SnapshotProfile.Create(before, [Ethernet.Id], "Rollback");

        var plan = ProfilePlanner.Plan(rollback, Snapshot(Up(Ethernet)));

        Assert.Contains(plan.Steps, s => s is SetMetricStep { Metric: 5, Family: AddressFamily.InterNetwork });
    }

    [Fact]
    public void Office_sample_plans_cleanly_against_a_dhcp_machine()
    {
        var profile = ProfileSerializer.Load(SamplePath("Office.nethop"));

        var plan = ProfilePlanner.Plan(profile, Snapshot(Up(Ethernet), Up(WiFi)));

        Assert.False(plan.HasErrors);
        Assert.Equal(new DisableAdapterStep(WiFi), plan.Steps[^1]);
        Assert.Equal(2, plan.Steps.Count(s => s is AddAddressStep a && a.Address.IsIPv4));
        Assert.Single(plan.Steps, s => s is AddAddressStep a && a.Address.IsIPv6);
    }
}
