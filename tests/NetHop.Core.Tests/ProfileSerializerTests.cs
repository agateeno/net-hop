using NetHop.Core.Profiles;
using NetHop.Core.Serialization;

namespace NetHop.Core.Tests;

public class ProfileSerializerTests
{
    [Theory]
    [InlineData("Office.nethop")]
    [InlineData("Home.nethop")]
    public void Samples_load_and_have_no_errors(string file)
    {
        var profile = ProfileSerializer.Load(TestData.SamplePath(file));

        var issues = ProfileValidator.Validate(profile);

        Assert.False(issues.HasErrors(), string.Join('\n', issues));
    }

    [Fact]
    public void Office_sample_is_read_correctly()
    {
        var profile = ProfileSerializer.Load(TestData.SamplePath("Office.nethop"));

        Assert.Equal("Office", profile.Name);
        Assert.Equal(2, profile.Adapters.Count);
        var ethernet = profile.Adapters[0];
        Assert.Equal(DesiredAdapterState.Enabled, ethernet.State);
        Assert.Equal(IpMode.Static, ethernet.Ipv4!.Mode);
        Assert.Equal(["192.168.10.50/24", "10.20.0.5/16"], ethernet.Ipv4.Addresses);
        Assert.Equal(DnsMode.Static, ethernet.Ipv4.Dns!.Mode);
        Assert.Equal(IpMode.Static, ethernet.Ipv6!.Mode);
        Assert.Equal(DesiredAdapterState.Disabled, profile.Adapters[1].State);
        Assert.Equal("", profile.Icon);
    }

    [Fact]
    public void Round_trip_preserves_content_and_omits_defaults()
    {
        var original = ProfileSerializer.Load(TestData.SamplePath("Office.nethop"));

        var yaml = ProfileSerializer.Serialize(original);
        var copy = ProfileSerializer.Deserialize(yaml);

        Assert.Equal(ProfileSerializer.Serialize(copy), yaml);
        Assert.Contains("mode: static", yaml);
        Assert.Contains("state: disabled", yaml);
        Assert.DoesNotContain("unchanged", yaml);
    }

    [Fact]
    public void Unknown_keys_are_rejected_with_position()
    {
        const string yaml = """
            name: Typo
            adapters:
              - match: { name: Ethernet }
                ipv4:
                  mode: static
                  adress: [192.168.1.2/24]
            """;

        var ex = Assert.Throws<NetHopFormatException>(() => ProfileSerializer.Deserialize(yaml));

        Assert.Equal(6, ex.Line);
    }

    [Fact]
    public void Metric_is_a_number_or_automatic_and_round_trips()
    {
        const string yaml = """
            name: Metric
            adapters:
              - match: { name: Ethernet }
                ipv4: { metric: 25 }
                ipv6: { metric: automatic }
            """;

        var profile = ProfileSerializer.Deserialize(yaml);
        var copy = ProfileSerializer.Deserialize(ProfileSerializer.Serialize(profile));

        Assert.Equal("25", copy.Adapters[0].Ipv4!.Metric);
        Assert.Equal(IpConfig.AutomaticMetric, copy.Adapters[0].Ipv6!.Metric);
        Assert.Empty(ProfileValidator.Validate(copy));
    }

    [Fact]
    public void Invalid_enum_value_is_rejected()
    {
        const string yaml = """
            name: Bad
            adapters:
              - match: { name: Ethernet }
                state: maybe
            """;

        Assert.Throws<NetHopFormatException>(() => ProfileSerializer.Deserialize(yaml));
    }
}
