using NetHop.Core.Networking;
using NetHop.Core.Planning;
using NetHop.Core.Profiles;
using static NetHop.Core.Tests.TestData;

namespace NetHop.Core.Tests;

public class SharingTests
{
    private static readonly AdapterInfo Warp = new()
    {
        Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Name = "CloudflareWARP",
        Description = "Cloudflare WARP Interface Tunnel",
        InterfaceIndex = 20,
        Kind = AdapterKind.Vpn,
        IsVirtual = true,
    };

    // The hotspot's virtual Wi-Fi adapter: hidden, so it is not in the snapshot's adapters.
    private static readonly Guid HotspotAdapter = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static NetworkSnapshot Network(SharingSnapshot? sharing) =>
        Snapshot(Up(Ethernet), Up(WiFi), Up(Warp)) with { Sharing = sharing };

    private static readonly SharingSnapshot NothingShared = new();

    private static SharingSnapshot HotspotSharing(AdapterInfo source) =>
        new() { HotspotOn = true, SharedFrom = source.Id, SharedTo = HotspotAdapter };

    private static Profile Sharing(SharingMode mode, string? from = null, string? to = null) => new()
    {
        Name = "Sharing",
        Sharing = new SharingConfig
        {
            Mode = mode,
            From = from is null ? null : new AdapterMatch { Name = from },
            To = to is null ? null : new AdapterMatch { Name = to },
        },
    };

    [Fact]
    public void Hotspot_starts_sharing_the_requested_connection()
    {
        var plan = ProfilePlanner.Plan(Sharing(SharingMode.Hotspot, "CloudflareWARP"), Network(NothingShared));

        Assert.Equal(new PlanStep[] { new ShareThroughHotspotStep(Warp) }, plan.Steps);
    }

    [Fact]
    public void Hotspot_already_sharing_the_connection_is_active()
    {
        var profile = Sharing(SharingMode.Hotspot, "CloudflareWARP");

        Assert.True(ProfilePlanner.IsSatisfied(profile, Network(HotspotSharing(Warp))));
    }

    [Fact]
    public void Hotspot_sharing_another_connection_switches_source()
    {
        var plan = ProfilePlanner.Plan(Sharing(SharingMode.Hotspot, "CloudflareWARP"), Network(HotspotSharing(WiFi)));

        Assert.Equal(new PlanStep[] { new ShareThroughHotspotStep(Warp) }, plan.Steps);
    }

    [Fact]
    public void Hotspot_without_source_accepts_whatever_it_shares()
    {
        Assert.True(ProfilePlanner.IsSatisfied(Sharing(SharingMode.Hotspot), Network(HotspotSharing(WiFi))));
        Assert.Equal(new PlanStep[] { new ShareThroughHotspotStep(null) }, ProfilePlanner.Plan(Sharing(SharingMode.Hotspot), Network(NothingShared)).Steps);
    }

    [Fact]
    public void Disabled_stops_the_hotspot_and_sharing()
    {
        var plan = ProfilePlanner.Plan(Sharing(SharingMode.Disabled), Network(HotspotSharing(Warp)));

        Assert.Equal(new PlanStep[] { new StopHotspotStep(), new StopSharingStep() }, plan.Steps);
        Assert.True(ProfilePlanner.IsSatisfied(Sharing(SharingMode.Disabled), Network(NothingShared)));
    }

    [Fact]
    public void Sharing_with_an_adapter_first_stops_the_hotspot()
    {
        var plan = ProfilePlanner.Plan(Sharing(SharingMode.Adapter, "CloudflareWARP", "Ethernet"), Network(HotspotSharing(Warp)));

        Assert.Equal(new PlanStep[] { new StopHotspotStep(), new ShareWithAdapterStep(Warp, Ethernet) }, plan.Steps);
    }

    [Fact]
    public void Sharing_with_an_adapter_already_set_up_is_active()
    {
        var current = new SharingSnapshot { SharedFrom = Warp.Id, SharedTo = Ethernet.Id };

        Assert.True(ProfilePlanner.IsSatisfied(Sharing(SharingMode.Adapter, "CloudflareWARP", "Ethernet"), Network(current)));
    }

    [Fact]
    public void Unknown_sharing_state_emits_the_steps()
    {
        var profile = Sharing(SharingMode.Hotspot, "CloudflareWARP");

        Assert.Equal(new PlanStep[] { new ShareThroughHotspotStep(Warp) }, ProfilePlanner.Plan(profile, Network(null)).Steps);
        Assert.False(ProfilePlanner.IsSatisfied(profile, Network(null)));
    }

    [Fact]
    public void Missing_source_adapter_skips_sharing_with_a_warning()
    {
        var plan = ProfilePlanner.Plan(Sharing(SharingMode.Hotspot, "Proton VPN"), Network(NothingShared));

        Assert.Empty(plan.Steps);
        Assert.Equal(1, plan.MissingAdapters);
        Assert.Contains(plan.Issues, i => i.Path == "sharing.from" && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void Sharing_comes_after_ip_configuration_and_before_disabling_adapters()
    {
        var profile = Sharing(SharingMode.Hotspot, "CloudflareWARP");
        profile.Adapters =
        [
            new AdapterConfig { Match = new AdapterMatch { Name = "Wi-Fi" }, State = DesiredAdapterState.Disabled },
            new AdapterConfig { Match = new AdapterMatch { Name = "Ethernet" }, Ipv4 = new IpConfig { Mode = IpMode.Static, Addresses = ["192.168.1.10/24"] } },
        ];

        var steps = ProfilePlanner.Plan(profile, Network(NothingShared)).Steps;

        Assert.IsType<DisableAdapterStep>(steps[^1]);
        Assert.IsType<ShareThroughHotspotStep>(steps[^2]);
        Assert.IsType<AddAddressStep>(steps[^3]);
    }

    [Fact]
    public void A_failed_apply_restores_the_previous_sharing()
    {
        var backend = new FakeBackend(Network(NothingShared), Network(HotspotSharing(WiFi)))
        {
            FailWhen = s => s is ShareThroughHotspotStep,
        };

        var outcome = new ProfileApplier(backend).Apply(Sharing(SharingMode.Hotspot, "CloudflareWARP"), dryRun: false);

        Assert.False(outcome.Success);
        Assert.True(outcome.RolledBack);
        Assert.Equal(new PlanStep[] { new StopHotspotStep(), new StopSharingStep() }, backend.Executed);
    }

    [Fact]
    public void Snapshot_sharing_becomes_a_profile_section()
    {
        var hotspot = SnapshotProfile.CreateSharing(Network(HotspotSharing(Warp)));
        var adapter = SnapshotProfile.CreateSharing(Network(new SharingSnapshot { SharedFrom = Warp.Id, SharedTo = Ethernet.Id }));

        Assert.Equal(SharingMode.Hotspot, hotspot!.Mode);
        Assert.Equal("CloudflareWARP", hotspot.From!.Name);
        Assert.Equal(SharingMode.Adapter, adapter!.Mode);
        Assert.Equal("Ethernet", adapter.To!.Name);
        Assert.Equal(SharingMode.Disabled, SnapshotProfile.CreateSharing(Network(NothingShared))!.Mode);
        Assert.Null(SnapshotProfile.CreateSharing(Network(null)));
    }

    [Fact]
    public void Profile_that_only_shares_is_valid()
    {
        Assert.DoesNotContain(ProfileValidator.Validate(Sharing(SharingMode.Hotspot, "CloudflareWARP")), i => i.Severity == IssueSeverity.Error);
        Assert.Contains(ProfileValidator.Validate(Sharing(SharingMode.Unchanged)), i => i.Path == "adapters");
    }

    [Theory]
    [InlineData(SharingMode.Adapter, null, "Ethernet", "sharing.from")]
    [InlineData(SharingMode.Adapter, "CloudflareWARP", null, "sharing.to")]
    [InlineData(SharingMode.Adapter, "CloudflareWARP", "cloudflarewarp", "sharing.to")]
    [InlineData(SharingMode.Hotspot, "CloudflareWARP", "Ethernet", "sharing.to")]
    [InlineData(SharingMode.Disabled, "CloudflareWARP", null, "sharing")]
    public void Invalid_sharing_is_an_error(SharingMode mode, string? from, string? to, string path)
    {
        var errors = ProfileValidator.Validate(Sharing(mode, from, to)).Where(i => i.Severity == IssueSeverity.Error);

        Assert.Contains(errors, i => i.Path == path);
    }

    [Fact]
    public void Sharing_section_round_trips_through_yaml()
    {
        const string yaml = """
            schema: 1
            name: WARP hotspot
            sharing:
              mode: hotspot
              from:
                name: CloudflareWARP
            """;

        var profile = ProfileSerializer.Deserialize(yaml);
        var again = ProfileSerializer.Deserialize(ProfileSerializer.Serialize(profile));

        Assert.Equal(SharingMode.Hotspot, again.Sharing!.Mode);
        Assert.Equal("CloudflareWARP", again.Sharing.From!.Name);
        Assert.Null(again.Sharing.To);
    }

    private sealed class FakeBackend(params NetworkSnapshot[] snapshots) : INetworkBackend
    {
        private int _snapshotCalls;

        public List<PlanStep> Executed { get; } = [];

        public Func<PlanStep, bool> FailWhen { get; init; } = _ => false;

        public NetworkSnapshot GetSnapshot() => snapshots[Math.Min(_snapshotCalls++, snapshots.Length - 1)];

        public void Execute(PlanStep step, CancellationToken cancellationToken)
        {
            if (FailWhen(step))
                throw new InvalidOperationException("simulated failure");
            Executed.Add(step);
        }

        public void RenameAdapter(Guid adapterId, string newName) { }
    }
}
