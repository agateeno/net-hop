using NetHop.Core.Networking;
using NetHop.Core.Rules;
using static NetHop.Core.Tests.TestData;

namespace NetHop.Core.Tests;

public class DetectorAndUriTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Now;

    private static AdapterSnapshot WifiOn(string? ssid) => Up(WiFi) with { Ssid = ssid };

    [Fact]
    public void First_snapshot_produces_no_events() =>
        Assert.Empty(NetworkEventDetector.Detect(null, Snapshot(Up(Ethernet)), Now));

    [Fact]
    public void Joining_a_wifi_network()
    {
        var before = Snapshot(Up(Ethernet), Disabled(WiFi) with { Enabled = true });
        var after = Snapshot(Up(Ethernet), WifiOn("CORP"));

        var types = NetworkEventDetector.Detect(before, after, Now).Select(e => e.Type);

        Assert.Equal([TriggerType.AdapterConnected, TriggerType.WifiConnected, TriggerType.NetworkChanged], types);
    }

    [Fact]
    public void Roaming_between_ssids_reports_disconnect_then_connect()
    {
        var events = NetworkEventDetector.Detect(Snapshot(WifiOn("Home")), Snapshot(WifiOn("CORP")), Now);

        Assert.Collection(events,
            e => Assert.Equal((TriggerType.WifiDisconnected, "Home"), (e.Type, e.Ssid)),
            e => Assert.Equal((TriggerType.WifiConnected, "CORP"), (e.Type, e.Ssid)),
            e => Assert.Equal(TriggerType.NetworkChanged, e.Type));
    }

    [Fact]
    public void Unplugged_usb_adapter_is_a_disconnect()
    {
        var events = NetworkEventDetector.Detect(Snapshot(Up(Ethernet), Up(WiFi)), Snapshot(Up(WiFi)), Now);

        Assert.Contains(events, e => e.Type == TriggerType.AdapterDisconnected && e.Adapter == Ethernet);
    }

    [Fact]
    public void Unchanged_network_produces_no_events() =>
        Assert.Empty(NetworkEventDetector.Detect(Snapshot(Up(Ethernet)), Snapshot(Up(Ethernet)), Now));

    [Theory]
    [InlineData("nethop://apply/Office", UriCommand.Apply, "Office")]
    [InlineData("NETHOP://apply/Home%20Office", UriCommand.Apply, "Home Office")]
    [InlineData("nethop://next", UriCommand.Next, null)]
    [InlineData("nethop://open", UriCommand.Open, null)]
    public void Uris_are_parsed(string text, UriCommand command, string? profile)
    {
        Assert.True(NetHopUri.TryParse(text, out var uri));
        Assert.Equal(new NetHopUri(command, profile), uri);
    }

    [Theory]
    [InlineData("nethop://apply/")]
    [InlineData("nethop://format/c")]
    [InlineData("https://apply/Office")]
    [InlineData("Office")]
    public void Bad_uris_are_rejected(string text) =>
        Assert.False(NetHopUri.TryParse(text, out _));

    [Fact]
    public void Uri_round_trips() =>
        Assert.Equal("nethop://apply/Home%20Office", new NetHopUri(UriCommand.Apply, "Home Office").ToString());
}
