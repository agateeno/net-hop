using System.Net;
using NetHop.Core.Networking;
using NetHop.Core.Planning;
using NetHop.Core.Profiles;
using static NetHop.Core.Tests.TestData;

namespace NetHop.Core.Tests;

public class HostsTests
{
    private const string WindowsHosts =
        "# Copyright (c) 1993-2009 Microsoft Corp.\r\n" +
        "#\r\n" +
        "# localhost name resolution is handled within DNS itself.\r\n" +
        "#\t127.0.0.1       localhost\r\n" +
        "10.0.0.5 build-server # added by hand\r\n";

    private static HostsMapping Map(string address, string name) => new(IPAddress.Parse(address), name);

    private static readonly HostsMapping[] Office = [Map("192.168.10.20", "intranet.corp.local"), Map("192.168.10.20", "wiki.corp.local")];

    private static Profile HostsProfile(HostsMode mode, params HostsEntry[] entries) => new()
    {
        Name = "Hosts",
        Hosts = new HostsConfig { Mode = mode, Entries = [.. entries] },
    };

    private static HostsEntry Entry(string ip, params string[] names) => new() { Ip = ip, Names = [.. names] };

    private static NetworkSnapshot Network(params HostsMapping[] mappings) =>
        Snapshot(Up(Ethernet), Up(WiFi)) with { Hosts = new HostsSnapshot { Mappings = mappings } };

    private static IEnumerable<Issue> Errors(Profile profile) =>
        ProfileValidator.Validate(profile).Where(i => i.Severity == IssueSeverity.Error);

    [Fact]
    public void Block_is_appended_and_the_rest_of_the_file_is_kept()
    {
        var written = HostsFile.WriteBlock(WindowsHosts, Office);

        Assert.StartsWith(WindowsHosts, written);
        Assert.EndsWith(
            "\r\n" + HostsFile.BeginMarker + "\r\n192.168.10.20 intranet.corp.local\r\n192.168.10.20 wiki.corp.local\r\n" + HostsFile.EndMarker + "\r\n",
            written);
    }

    [Fact]
    public void Block_is_replaced_in_place()
    {
        var withBlock = HostsFile.WriteBlock(WindowsHosts, Office) + "127.0.0.1 after.local\r\n";

        var written = HostsFile.WriteBlock(withBlock, [Map("10.20.0.5", "nas.corp.local")]);

        Assert.Equal(new[] { Map("10.20.0.5", "nas.corp.local") }, HostsFile.ReadBlock(written));
        Assert.EndsWith(HostsFile.EndMarker + "\r\n127.0.0.1 after.local\r\n", written);
        Assert.DoesNotContain("wiki.corp.local", written);
    }

    [Fact]
    public void No_mappings_removes_the_block()
    {
        var withBlock = HostsFile.WriteBlock(WindowsHosts, Office);

        var written = HostsFile.WriteBlock(withBlock, []);

        Assert.DoesNotContain("NetHop", written);
        Assert.Contains("10.0.0.5 build-server # added by hand", written);
        Assert.Equal(WindowsHosts, HostsFile.WriteBlock(WindowsHosts, []));
    }

    [Fact]
    public void Lines_outside_the_block_are_not_read()
    {
        Assert.Empty(HostsFile.ReadBlock(WindowsHosts));
        Assert.Equal(Office, HostsFile.ReadBlock(HostsFile.WriteBlock(WindowsHosts, Office)));
    }

    [Fact]
    public void Unix_line_endings_and_empty_files_are_handled()
    {
        Assert.Equal("127.0.0.1 localhost\n\n" + HostsFile.BeginMarker + "\n10.0.0.1 a.local\n" + HostsFile.EndMarker + "\n",
            HostsFile.WriteBlock("127.0.0.1 localhost\n", [Map("10.0.0.1", "a.local")]));
        Assert.Equal(HostsFile.BeginMarker + "\r\n10.0.0.1 a.local\r\n" + HostsFile.EndMarker + "\r\n",
            HostsFile.WriteBlock("", [Map("10.0.0.1", "a.local")]));
    }

    [Fact]
    public void A_broken_block_is_left_for_the_user_to_fix()
    {
        var broken = WindowsHosts + HostsFile.BeginMarker + "\r\n10.0.0.1 a.local\r\n";

        Assert.Throws<FormatException>(() => HostsFile.ReadBlock(broken));
        Assert.Throws<FormatException>(() => HostsFile.WriteBlock(broken, Office));
    }

    [Theory]
    [InlineData("192.168.1.20", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("fd00::20", true)]
    [InlineData("10.1", false)]
    [InlineData("192.168.1.20/24", false)]
    [InlineData("fe80::1%3", false)]
    [InlineData("intranet", false)]
    [InlineData("", false)]
    public void Addresses_are_parsed_strictly(string text, bool valid) =>
        Assert.Equal(valid, HostsFile.TryParseAddress(text, out _));

    [Theory]
    [InlineData("nas", true)]
    [InlineData("intranet.corp.local", true)]
    [InlineData("my-server01.lan", true)]
    [InlineData("bad name", false)]
    [InlineData("a#b.local", false)]
    [InlineData("*.corp.local", false)]
    [InlineData("-a.local", false)]
    [InlineData("a..local", false)]
    [InlineData("intranet.local\n10.0.0.1 evil.local", false)]
    [InlineData("intranet.local\n", false)]
    public void Host_names_are_checked_strictly(string name, bool valid) =>
        Assert.Equal(valid, HostsFile.IsValidHostName(name));

    [Theory]
    [InlineData("mail.google.com", "google.com")]
    [InlineData("GOOGLE.COM", "google.com")]
    [InlineData("update.microsoft.com", "microsoft.com")]
    [InlineData("www.icloud.com.", "icloud.com")]
    [InlineData("googleadservices.com", null)]
    [InlineData("intranet.corp.local", null)]
    public void Protected_domains_cover_their_subdomains_only(string name, string? domain) =>
        Assert.Equal(domain, ProtectedDomains.Find(name));

    [Fact]
    public void Protected_domain_list_is_well_formed()
    {
        Assert.Contains("apple.com", ProtectedDomains.All);
        Assert.Equal(ProtectedDomains.All.Count, ProtectedDomains.All.Distinct().Count());
        Assert.All(ProtectedDomains.All, d => Assert.True(HostsFile.IsValidHostName(d) && d == d.ToLowerInvariant(), d));
        Assert.Equal(new[] { "a.com", "b.org" }, ProtectedDomains.Parse("# comment\n\nA.com # trailing\r\nb.org\n"));
    }

    [Fact]
    public void Protected_domain_is_an_error()
    {
        var errors = Errors(HostsProfile(HostsMode.Set, Entry("0.0.0.0", "intranet.corp.local", "accounts.google.com")));

        Assert.Contains(errors, i => i.Path == "hosts.entries[0].names[1]");
        Assert.DoesNotContain(errors, i => i.Path == "hosts.entries[0].names[0]");
    }

    [Fact]
    public void Same_name_with_two_addresses_of_one_family_is_an_error()
    {
        var conflict = HostsProfile(HostsMode.Set, Entry("10.0.0.1", "nas.lan"), Entry("10.0.0.2", "nas.lan"));
        var dualStack = HostsProfile(HostsMode.Set, Entry("10.0.0.1", "nas.lan"), Entry("fd00::1", "nas.lan"));
        var repeated = HostsProfile(HostsMode.Set, Entry("10.0.0.1", "nas.lan"), Entry("10.0.0.1", "NAS.lan"));

        Assert.Contains(Errors(conflict), i => i.Path == "hosts.entries[1].names[0]");
        Assert.Empty(Errors(dualStack));
        Assert.Empty(Errors(repeated));
        Assert.Contains(ProfileValidator.Validate(repeated), i => i.Severity == IssueSeverity.Warning && i.Path == "hosts.entries[1].names[0]");
    }

    [Fact]
    public void Mode_and_entries_must_agree()
    {
        Assert.Contains(Errors(HostsProfile(HostsMode.Clear, Entry("10.0.0.1", "a.lan"))), i => i.Path == "hosts.entries");
        Assert.Contains(Errors(HostsProfile(HostsMode.Set)), i => i.Path == "hosts.entries");
        Assert.Contains(Errors(HostsProfile(HostsMode.Set, Entry("10.0.0.1"))), i => i.Path == "hosts.entries[0].names");
        Assert.Empty(Errors(HostsProfile(HostsMode.Clear)));
    }

    [Fact]
    public void Too_many_names_is_an_error()
    {
        var names = Enumerable.Range(0, HostsConfig.MaxNames + 1).Select(i => $"host{i}.lan").ToArray();

        Assert.Contains(Errors(HostsProfile(HostsMode.Set, Entry("10.0.0.1", names))), i => i.Path == "hosts.entries");
    }

    [Fact]
    public void Different_entries_rewrite_the_block()
    {
        var profile = HostsProfile(HostsMode.Set, Entry("192.168.10.20", "intranet.corp.local", "wiki.corp.local"));

        var plan = ProfilePlanner.Plan(profile, Network(Map("10.20.0.5", "nas.corp.local")));

        Assert.Equal(new PlanStep[] { new SetHostsStep(Office) }, plan.Steps);
    }

    [Fact]
    public void Same_entries_in_another_order_are_active()
    {
        var profile = HostsProfile(HostsMode.Set, Entry("192.168.10.20", "WIKI.corp.local", "intranet.corp.local"));

        Assert.True(ProfilePlanner.IsSatisfied(profile, Network(Office)));
    }

    [Fact]
    public void Clear_removes_only_what_is_there()
    {
        Assert.Equal(new PlanStep[] { new SetHostsStep([]) }, ProfilePlanner.Plan(HostsProfile(HostsMode.Clear), Network(Office)).Steps);
        Assert.True(ProfilePlanner.IsSatisfied(HostsProfile(HostsMode.Clear), Network()));
    }

    [Fact]
    public void Unreadable_hosts_file_rewrites_the_block()
    {
        var unknown = Snapshot(Up(Ethernet));

        Assert.Single(ProfilePlanner.Plan(HostsProfile(HostsMode.Clear), unknown).Steps);
    }

    [Fact]
    public void A_failed_apply_restores_the_previous_block()
    {
        var old = Map("10.20.0.5", "nas.corp.local");
        var profile = HostsProfile(HostsMode.Set, Entry("192.168.10.20", "intranet.corp.local", "wiki.corp.local"));
        profile.Adapters = [new AdapterConfig { Match = new AdapterMatch { Name = "Wi-Fi" }, State = DesiredAdapterState.Disabled }];
        var backend = new FakeBackend(Network(old), Network(Office)) { FailWhen = s => s is DisableAdapterStep };

        var outcome = new ProfileApplier(backend).Apply(profile, dryRun: false);

        Assert.True(outcome.RolledBack);
        Assert.Equal(new PlanStep[] { new SetHostsStep(Office), new SetHostsStep([old]) }, backend.Executed);
    }

    [Fact]
    public void Hosts_section_round_trips_through_yaml()
    {
        const string yaml = """
            schema: 1
            name: Office hosts
            hosts:
              mode: set
              entries:
                - ip: 192.168.10.20
                  names: [intranet.corp.local, wiki.corp.local]
            """;

        var profile = ProfileSerializer.Deserialize(ProfileSerializer.Serialize(ProfileSerializer.Deserialize(yaml)));

        Assert.Equal(HostsMode.Set, profile.Hosts!.Mode);
        Assert.Equal("192.168.10.20", profile.Hosts.Entries[0].Ip);
        Assert.Equal(new[] { "intranet.corp.local", "wiki.corp.local" }, profile.Hosts.Entries[0].Names);
        Assert.Empty(Errors(profile));
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
