using NetHop.Core.Networking;
using NetHop.Core.Planning;
using NetHop.Core.Profiles;
using static NetHop.Core.Tests.TestData;

namespace NetHop.Core.Tests;

public class ProfileApplierTests
{
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

    private static readonly Profile ToDhcp = new()
    {
        Name = "Dhcp",
        Adapters = [new AdapterConfig { Match = new AdapterMatch { Name = "Ethernet" }, Ipv4 = new IpConfig { Mode = IpMode.Dhcp } }],
    };

    private static readonly NetworkSnapshot StaticState =
        Snapshot(Up(Ethernet, Static(["192.168.1.10/24"], ["192.168.1.1"], ["1.1.1.1"])));

    [Fact]
    public void Dry_run_executes_nothing()
    {
        var backend = new FakeBackend(StaticState);

        var outcome = new ProfileApplier(backend).Apply(ToDhcp, dryRun: true);

        Assert.True(outcome.Success);
        Assert.NotEmpty(outcome.Plan.Steps);
        Assert.Empty(backend.Executed);
    }

    [Fact]
    public void Successful_apply_executes_every_step()
    {
        var backend = new FakeBackend(StaticState);

        var outcome = new ProfileApplier(backend).Apply(ToDhcp, dryRun: false);

        Assert.True(outcome.Success);
        Assert.Equal(outcome.Plan.Steps, backend.Executed);
    }

    [Fact]
    public void Failure_rolls_back_the_touched_adapter()
    {
        // The failure happens after the static address was removed: the second snapshot shows it gone.
        var halfway = Snapshot(Up(Ethernet, Static([], [], ["1.1.1.1"])));
        var backend = new FakeBackend(StaticState, halfway) { FailWhen = s => s is SetDhcpStep { Enabled: true } };

        var outcome = new ProfileApplier(backend).Apply(ToDhcp, dryRun: false);

        Assert.False(outcome.Success);
        Assert.True(outcome.RolledBack);
        Assert.Contains("simulated failure", outcome.Error);
        Assert.Contains(backend.Executed, s => s is AddAddressStep a && a.Address == IpCidr.Parse("192.168.1.10/24"));
        Assert.Contains(backend.Executed, s => s is AddGatewayStep);
    }
}
