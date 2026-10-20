using NetHop.Core.Profiles;

namespace NetHop.Core.Tests;

public class ProfileValidatorTests
{
    private static Profile WithIpv4(IpConfig ipv4) => new()
    {
        Name = "Test",
        Adapters = [new AdapterConfig { Match = new AdapterMatch { Name = "Ethernet" }, Ipv4 = ipv4 }],
    };

    private static Profile WithIpv6(IpConfig ipv6) => new()
    {
        Name = "Test",
        Adapters = [new AdapterConfig { Match = new AdapterMatch { Name = "Ethernet" }, Ipv6 = ipv6 }],
    };

    private static IEnumerable<Issue> Errors(Profile p) =>
        ProfileValidator.Validate(p).Where(i => i.Severity == IssueSeverity.Error);

    private static IEnumerable<Issue> Warnings(Profile p) =>
        ProfileValidator.Validate(p).Where(i => i.Severity == IssueSeverity.Warning);

    [Theory]
    [InlineData("\uE968", true)]
    [InlineData("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==", true)]
    [InlineData("data:image/jpeg;base64,/9j/4AAQSkZJRg==", false)]
    [InlineData("data:image/png;base64,not base64!", false)]
    [InlineData("data:image/png;base64,R0lGODlhAQABAAAAACw=", false)] // a GIF, not a PNG
    public void Icons_are_glyphs_or_png_images(string icon, bool valid)
    {
        var p = WithIpv4(new IpConfig { Mode = IpMode.Dhcp });
        p.Icon = icon;
        Assert.Equal(valid, !Errors(p).Any(i => i.Path == "icon"));
    }

    [Fact]
    public void Valid_static_ipv4_with_multiple_addresses()
    {
        var p = WithIpv4(new IpConfig
        {
            Mode = IpMode.Static,
            Addresses = ["192.168.1.10/24", "192.168.1.11/24", "10.0.0.5/8"],
            Gateways = ["192.168.1.1"],
            Dns = new DnsConfig { Mode = DnsMode.Static, Servers = ["1.1.1.1", "8.8.8.8"] },
        });

        Assert.Empty(Errors(p));
        Assert.Empty(Warnings(p));
    }

    [Fact]
    public void Missing_name_and_adapters_are_errors()
    {
        var issues = ProfileValidator.Validate(new Profile());

        Assert.Contains(issues, i => i.Path == "name");
        Assert.Contains(issues, i => i.Path == "adapters");
    }

    [Theory]
    [InlineData("192.168.1.10")]        // no prefix
    [InlineData("192.168.1.10/33")]     // prefix too long
    [InlineData("192.168.1.10/0")]
    [InlineData("300.1.1.1/24")]
    [InlineData("2001:db8::1/64")]      // wrong family
    public void Bad_ipv4_addresses_are_errors(string address)
    {
        var p = WithIpv4(new IpConfig { Mode = IpMode.Static, Addresses = [address] });

        Assert.Contains(Errors(p), i => i.Path == "adapters[0].ipv4.addresses[0]");
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("9999", true)]
    [InlineData("automatic", true)]
    [InlineData("Automatic", true)]
    [InlineData("0", false)]
    [InlineData("10000", false)]
    [InlineData("-5", false)]
    [InlineData("auto", false)]
    public void Metric_is_automatic_or_between_1_and_9999(string metric, bool valid)
    {
        var p = WithIpv4(new IpConfig { Metric = metric });

        Assert.Equal(valid, !Errors(p).Any(i => i.Path == "adapters[0].ipv4.metric"));
        // A block that only sets the metric still changes something.
        Assert.DoesNotContain(Warnings(p), i => i.Path == "adapters[0].ipv4");
    }

    [Fact]
    public void Static_without_addresses_is_an_error()
    {
        var p = WithIpv4(new IpConfig { Mode = IpMode.Static });

        Assert.Contains(Errors(p), i => i.Path == "adapters[0].ipv4.addresses");
    }

    [Fact]
    public void Addresses_in_dhcp_mode_are_an_error()
    {
        var p = WithIpv4(new IpConfig { Mode = IpMode.Dhcp, Addresses = ["192.168.1.10/24"] });

        Assert.NotEmpty(Errors(p));
    }

    [Fact]
    public void Duplicate_address_is_an_error()
    {
        var p = WithIpv4(new IpConfig { Mode = IpMode.Static, Addresses = ["192.168.1.10/24", "192.168.1.10/24"] });

        Assert.Contains(Errors(p), i => i.Path == "adapters[0].ipv4.addresses[1]");
    }

    [Fact]
    public void Gateway_outside_subnet_and_broadcast_address_are_warnings()
    {
        var p = WithIpv4(new IpConfig
        {
            Mode = IpMode.Static,
            Addresses = ["192.168.1.255/24"],
            Gateways = ["10.0.0.1"],
        });

        Assert.Empty(Errors(p));
        Assert.Contains(Warnings(p), i => i.Path == "adapters[0].ipv4.gateways[0]");
        Assert.Contains(Warnings(p), i => i.Path == "adapters[0].ipv4.addresses[0]");
    }

    [Fact]
    public void Valid_static_ipv6_with_link_local_gateway()
    {
        var p = WithIpv6(new IpConfig
        {
            Mode = IpMode.Static,
            Addresses = ["2001:db8::10/64", "fd00::10/64"],
            Gateways = ["fe80::1"],
            Dns = new DnsConfig { Mode = DnsMode.Static, Servers = ["2606:4700:4700::1111"] },
        });

        Assert.Empty(Errors(p));
    }

    [Fact]
    public void Ipv4_dns_in_ipv6_block_is_an_error()
    {
        var p = WithIpv6(new IpConfig { Mode = IpMode.Dhcp, Dns = new DnsConfig { Mode = DnsMode.Static, Servers = ["1.1.1.1"] } });

        Assert.Contains(Errors(p), i => i.Path == "adapters[0].ipv6.dns.servers[0]");
    }

    [Fact]
    public void Same_adapter_twice_is_an_error()
    {
        var p = new Profile
        {
            Name = "Dup",
            Adapters =
            [
                new AdapterConfig { Match = new AdapterMatch { Name = "Ethernet" }, State = DesiredAdapterState.Enabled },
                new AdapterConfig { Match = new AdapterMatch { Name = "ethernet" }, State = DesiredAdapterState.Disabled },
            ],
        };

        Assert.Contains(Errors(p), i => i.Path == "adapters[1].match");
    }

    [Theory]
    [InlineData("not-a-guid", null)]
    [InlineData(null, "00-11-22")]
    public void Bad_match_values_are_errors(string? guid, string? mac)
    {
        var p = new Profile
        {
            Name = "Bad match",
            Adapters = [new AdapterConfig { Match = new AdapterMatch { Guid = guid, Mac = mac }, State = DesiredAdapterState.Enabled }],
        };

        Assert.NotEmpty(Errors(p));
    }
}
