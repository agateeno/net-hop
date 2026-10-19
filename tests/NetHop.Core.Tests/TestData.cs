using System.Net;
using NetHop.Core.Networking;

namespace NetHop.Core.Tests;

internal static class TestData
{
    public static readonly AdapterInfo Ethernet = new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Name = "Ethernet",
        Description = "Intel(R) Ethernet Connection I219-V",
        Mac = "00-1A-2B-3C-4D-5E",
        InterfaceIndex = 12,
        Kind = AdapterKind.Ethernet,
    };

    public static readonly AdapterInfo WiFi = new()
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Name = "Wi-Fi",
        Description = "Intel(R) Wi-Fi 6E AX211",
        Mac = "AA-BB-CC-DD-EE-FF",
        InterfaceIndex = 7,
        Kind = AdapterKind.WiFi,
    };

    public static FamilySnapshot Dhcp() => new() { Dhcp = true };

    public static FamilySnapshot Static(string[] addresses, string[]? gateways = null, string[]? dns = null) => new()
    {
        Dhcp = false,
        ManualAddresses = addresses.Select(IpCidr.Parse).ToList(),
        ManualGateways = (gateways ?? []).Select(IPAddress.Parse).ToList(),
        StaticDnsServers = (dns ?? []).Select(IPAddress.Parse).ToList(),
    };

    public static NetworkSnapshot Snapshot(params AdapterSnapshot[] adapters) => new() { Adapters = adapters };

    public static AdapterSnapshot Up(AdapterInfo info, FamilySnapshot? ipv4 = null, FamilySnapshot? ipv6 = null) => new()
    {
        Info = info,
        Enabled = true,
        Connected = true,
        Ipv4 = ipv4 ?? Dhcp(),
        Ipv6 = ipv6 ?? Dhcp(),
    };

    public static AdapterSnapshot Disabled(AdapterInfo info) => new() { Info = info, Enabled = false };

    public static string SamplePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("samples folder not found"), "samples", fileName);
    }
}
