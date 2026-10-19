using System.Net;
using NetHop.Core.Networking;
using NetHop.Core.Profiles;

namespace NetHop.Core.Tests;

public class NetworkingTests
{
    [Theory]
    [InlineData("192.168.1.10/24", "192.168.1.200", true)]
    [InlineData("192.168.1.10/24", "192.168.2.1", false)]
    [InlineData("10.20.0.5/12", "10.31.255.254", true)]
    [InlineData("10.20.0.5/12", "10.32.0.1", false)]
    [InlineData("2001:db8::10/64", "2001:db8::1", true)]
    [InlineData("2001:db8::10/64", "2001:db9::1", false)]
    [InlineData("192.168.1.10/24", "2001:db8::1", false)]
    public void Cidr_contains(string cidr, string address, bool expected) =>
        Assert.Equal(expected, IpCidr.Parse(cidr).Contains(IPAddress.Parse(address)));

    [Theory]
    [InlineData("192.168.1.0/24", true)]
    [InlineData("192.168.1.255/24", true)]
    [InlineData("192.168.1.1/24", false)]
    [InlineData("10.0.0.0/31", false)]
    public void Network_or_broadcast(string cidr, bool expected) =>
        Assert.Equal(expected, IpCidr.Parse(cidr).IsNetworkOrBroadcastAddress());

    [Fact]
    public void Cidr_equality_is_by_value() =>
        Assert.Equal(IpCidr.Parse("2001:DB8::10/64"), IpCidr.Parse("2001:db8:0::10/64"));

    [Theory]
    [InlineData("00:1a:2b:3c:4d:5e")]
    [InlineData("00-1A-2B-3C-4D-5E")]
    [InlineData("001A2B3C4D5E")]
    public void Mac_is_normalized(string mac)
    {
        Assert.True(MacAddress.TryNormalize(mac, out var normalized));
        Assert.Equal("00-1A-2B-3C-4D-5E", normalized);
    }

    [Fact]
    public void Matcher_prefers_guid_then_mac_then_name()
    {
        AdapterInfo[] adapters = [TestData.Ethernet, TestData.WiFi];

        var byGuid = AdapterMatcher.Resolve(new AdapterMatch { Guid = TestData.WiFi.Id.ToString(), Name = "Ethernet" }, adapters);
        var byMac = AdapterMatcher.Resolve(new AdapterMatch { Guid = Guid.NewGuid().ToString(), Mac = "aa:bb:cc:dd:ee:ff", Name = "Ethernet" }, adapters);
        var byName = AdapterMatcher.Resolve(new AdapterMatch { Mac = "01-02-03-04-05-06", Name = "wi-fi" }, adapters);

        Assert.Equal((TestData.WiFi, AdapterMatchKind.Guid), (byGuid.Adapter, byGuid.MatchedBy));
        Assert.Equal((TestData.WiFi, AdapterMatchKind.Mac), (byMac.Adapter, byMac.MatchedBy));
        Assert.Equal((TestData.WiFi, AdapterMatchKind.Name), (byName.Adapter, byName.MatchedBy));
    }

    [Fact]
    public void Matcher_reports_missing_adapter() =>
        Assert.False(AdapterMatcher.Resolve(new AdapterMatch { Name = "Ethernet 9" }, [TestData.Ethernet]).Found);
}
