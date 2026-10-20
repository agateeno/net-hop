using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using NetHop.Core.Networking;
using NetHop.Core.Planning;
using NetHop.Engine.Native;

namespace NetHop.Engine;

/// <summary>
/// Network backend built on the same CIM classes as the NetAdapter/NetTCPIP PowerShell modules
/// (<c>root\StandardCimv2</c>). Must run elevated: NetHop runs it inside the Windows service.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CimNetworkBackend : INetworkBackend, IDisposable
{
    private const string Namespace = @"root\StandardCimv2";

    // CIM enum values (see MSFT_NetAdapter, MSFT_NetIPAddress, MSFT_NetRoute docs).
    private const uint AdapterStateStarted = 2;
    private const uint AdapterStateDisabled = 3;
    private const uint MediaConnected = 1;
    private const uint FamilyIPv4 = 2;
    private const uint FamilyIPv6 = 23;
    private const uint PrefixOriginManual = 1;
    private const uint RouteProtocolNetMgmt = 3;

    // TODO(spike): verify on real hardware that changes made through both stores survive a reboot
    // exactly like New-NetIPAddress / Set-NetIPInterface do.
    private static readonly string[] PolicyStores = ["ActiveStore", "PersistentStore"];

    private readonly CimSession _session = CimSession.Create(null);

    public TimeSpan AdapterUpTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>True when the last snapshot could not read Wi-Fi SSIDs because location access is off.</summary>
    public bool SsidAccessDenied { get; private set; }

    public NetworkSnapshot GetSnapshot()
    {
        var adapters = Query("SELECT * FROM MSFT_NetAdapter")
            .Where(a => Get<bool?>(a, "Hidden") != true)
            .ToList();
        var ipInterfaces = Query("SELECT InterfaceIndex, AddressFamily, Dhcp, AutomaticMetric, InterfaceMetric FROM MSFT_NetIPInterface").ToList();
        var addresses = Query("SELECT InterfaceIndex, AddressFamily, IPAddress, PrefixLength, PrefixOrigin FROM MSFT_NetIPAddress").ToList();
        var routes = Query("SELECT InterfaceIndex, AddressFamily, DestinationPrefix, NextHop, Protocol FROM MSFT_NetRoute WHERE DestinationPrefix = '0.0.0.0/0' OR DestinationPrefix = '::/0'").ToList();
        var effectiveDns = NetworkInterface.GetAllNetworkInterfaces()
            .ToDictionary(n => n.Id, n => (IReadOnlyList<IPAddress>)n.GetIPProperties().DnsAddresses.ToList(), StringComparer.OrdinalIgnoreCase);

        SsidAccessDenied = false;
        var result = new List<AdapterSnapshot>();
        foreach (var adapter in adapters)
        {
            var info = ToInfo(adapter);
            var state = Get<uint>(adapter, "State");
            var enabled = state != AdapterStateDisabled;
            var connected = enabled && Get<uint>(adapter, "MediaConnectState") == MediaConnected;

            string? ssid = null;
            if (connected && info.Kind == AdapterKind.WiFi)
            {
                ssid = WlanApi.GetConnectedSsid(info.Id);
                SsidAccessDenied |= WlanApi.LastCallDenied;
            }

            effectiveDns.TryGetValue(info.Id.ToString("B"), out var dns);
            result.Add(new AdapterSnapshot
            {
                Info = info,
                Enabled = enabled,
                Connected = connected,
                Ssid = ssid,
                Ipv4 = Family(info, FamilyIPv4, ipInterfaces, addresses, routes, dns),
                Ipv6 = Family(info, FamilyIPv6, ipInterfaces, addresses, routes, dns),
            });
        }

        return new NetworkSnapshot { Adapters = result, Sharing = ConnectionSharing.Read(), TakenAt = DateTimeOffset.Now };
    }

    private static FamilySnapshot? Family(AdapterInfo info, uint family, List<CimInstance> ipInterfaces,
        List<CimInstance> addresses, List<CimInstance> routes, IReadOnlyList<IPAddress>? effectiveDns)
    {
        var ipInterface = ipInterfaces.FirstOrDefault(i => Get<uint>(i, "InterfaceIndex") == info.InterfaceIndex && Get<uint>(i, "AddressFamily") == family);
        if (ipInterface is null)
            return null; // adapter disabled or protocol unbound

        var dhcp = Get<uint>(ipInterface, "Dhcp") == 1;
        var mine = addresses.Where(a => Get<uint>(a, "InterfaceIndex") == info.InterfaceIndex && Get<uint>(a, "AddressFamily") == family).ToList();
        var all = mine.Select(ToCidr).OfType<IpCidr>().ToList();
        var manual = mine.Where(a => Get<uint>(a, "PrefixOrigin") == PrefixOriginManual).Select(ToCidr).OfType<IpCidr>().ToList();

        var gateways = dhcp && family == FamilyIPv4
            ? []
            : routes
                .Where(r => Get<uint>(r, "InterfaceIndex") == info.InterfaceIndex && Get<uint>(r, "AddressFamily") == family
                    && Get<uint>(r, "Protocol") == RouteProtocolNetMgmt)
                .Select(r => IPAddress.TryParse(Get<string>(r, "NextHop"), out var hop) ? hop : null)
                .OfType<IPAddress>()
                .Where(hop => !hop.Equals(IPAddress.Any) && !hop.Equals(IPAddress.IPv6Any))
                .Distinct()
                .ToList();

        var addressFamily = family == FamilyIPv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;
        var metric = (int)Get<uint>(ipInterface, "InterfaceMetric");
        return new FamilySnapshot
        {
            Dhcp = dhcp,
            ManualAddresses = manual,
            ManualGateways = gateways,
            StaticDnsServers = DnsSettings.GetStaticServers(info.Id, addressFamily),
            ManualMetric = Get<uint>(ipInterface, "AutomaticMetric") == 1 ? null : metric,
            InterfaceMetric = metric,
            AllAddresses = all,
            EffectiveDnsServers = (effectiveDns ?? []).Where(d => d.AddressFamily == addressFamily).ToList(),
        };
    }

    public void Execute(PlanStep step, CancellationToken cancellationToken)
    {
        switch (step)
        {
            case EnableAdapterStep s:
                InvokeOnAdapter(s.Adapter.Id, "Enable");
                break;
            case DisableAdapterStep s:
                InvokeOnAdapter(s.Adapter.Id, "Disable");
                break;
            case WaitForAdapterStep s:
                WaitForAdapter(s.Adapter.Id, cancellationToken);
                break;
            case SetDhcpStep s:
                SetDhcp(s.Adapter.Id, s.Family, s.Enabled);
                break;
            case AddAddressStep s:
                IgnoreAlreadyExists(() => InvokeStatic("MSFT_NetIPAddress", "Create",
                    ("InterfaceIndex", CurrentIndex(s.Adapter.Id), CimType.UInt32),
                    ("IPAddress", s.Address.Address.ToString(), CimType.String),
                    ("PrefixLength", (byte)s.Address.PrefixLength, CimType.UInt8)));
                break;
            case RemoveAddressStep s:
                DeleteEverywhere($"SELECT * FROM MSFT_NetIPAddress WHERE InterfaceIndex = {CurrentIndex(s.Adapter.Id)} AND IPAddress = '{s.Address.Address}'");
                break;
            case AddGatewayStep s:
                IgnoreAlreadyExists(() => InvokeStatic("MSFT_NetRoute", "Create",
                    ("DestinationPrefix", s.Gateway.AddressFamily == AddressFamily.InterNetwork ? "0.0.0.0/0" : "::/0", CimType.String),
                    ("InterfaceIndex", CurrentIndex(s.Adapter.Id), CimType.UInt32),
                    ("NextHop", s.Gateway.ToString(), CimType.String)));
                break;
            case RemoveGatewayStep s:
                var prefix = s.Gateway.AddressFamily == AddressFamily.InterNetwork ? "0.0.0.0/0" : "::/0";
                DeleteEverywhere($"SELECT * FROM MSFT_NetRoute WHERE InterfaceIndex = {CurrentIndex(s.Adapter.Id)} AND DestinationPrefix = '{prefix}' AND NextHop = '{s.Gateway}'");
                break;
            case SetDnsStep s:
                DnsSettings.SetServers(s.Adapter.Id, s.Family, s.Servers);
                break;
            case SetMetricStep s:
                // Like Set-NetIPInterface: a fixed metric turns automatic metric off, "automatic" turns it back on.
                ModifyIpInterface(s.Adapter.Id, s.Family, instance =>
                {
                    instance.CimInstanceProperties["AutomaticMetric"].Value = (byte)(s.Metric is null ? 1 : 0);
                    if (s.Metric is { } metric)
                        instance.CimInstanceProperties["InterfaceMetric"].Value = (uint)metric;
                });
                break;
            case ShareThroughHotspotStep s:
                ConnectionSharing.ShareThroughHotspot(s.Source?.Id, cancellationToken);
                break;
            case StopHotspotStep:
                ConnectionSharing.StopHotspot(cancellationToken);
                break;
            case ShareWithAdapterStep s:
                ConnectionSharing.ShareWith(s.Source.Id, s.Target.Id);
                break;
            case StopSharingStep:
                ConnectionSharing.StopSharing();
                break;
            default:
                throw new NotSupportedException($"Unknown step {step.GetType().Name}.");
        }
    }

    public void RenameAdapter(Guid adapterId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.Length > 255)
            throw new ArgumentException("Invalid adapter name.", nameof(newName));

        var parameters = new CimMethodParametersCollection
        {
            CimMethodParameter.Create("NewName", newName.Trim(), CimType.String, CimFlags.In),
        };
        CheckReturn(_session.InvokeMethod(Namespace, FindAdapter(adapterId), "Rename", parameters), "Rename");
    }

    private void SetDhcp(Guid adapterId, AddressFamily family, bool enabled) =>
        ModifyIpInterface(adapterId, family, instance => instance.CimInstanceProperties["Dhcp"].Value = (byte)(enabled ? 1 : 0));

    /// <summary>Changes the adapter's IP interface of one family, in both the active and the persistent store.</summary>
    private void ModifyIpInterface(Guid adapterId, AddressFamily family, Action<CimInstance> change)
    {
        var cimFamily = family == AddressFamily.InterNetwork ? FamilyIPv4 : FamilyIPv6;
        foreach (var store in PolicyStores)
        {
            var options = StoreOptions(store);
            var instances = _session.QueryInstances(Namespace, "WQL",
                $"SELECT * FROM MSFT_NetIPInterface WHERE InterfaceIndex = {CurrentIndex(adapterId)} AND AddressFamily = {cimFamily}", options);
            foreach (var instance in instances)
            {
                change(instance);
                _session.ModifyInstance(Namespace, instance, options);
            }
        }
    }

    private void WaitForAdapter(Guid adapterId, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + AdapterUpTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = TryFindAdapter(adapterId);
            if (adapter is not null && Get<uint>(adapter, "State") == AdapterStateStarted)
            {
                var index = Get<uint>(adapter, "InterfaceIndex");
                if (Query($"SELECT InterfaceIndex FROM MSFT_NetIPInterface WHERE InterfaceIndex = {index}").Any())
                    return;
            }
            Thread.Sleep(500);
        }
        throw new TimeoutException($"The adapter did not come up within {AdapterUpTimeout.TotalSeconds:0} seconds.");
    }

    // The interface index can change when an adapter is disabled and re-enabled, so it is re-read by GUID.
    private uint CurrentIndex(Guid adapterId) => Get<uint>(FindAdapter(adapterId), "InterfaceIndex");

    private void InvokeOnAdapter(Guid adapterId, string method) =>
        CheckReturn(_session.InvokeMethod(Namespace, FindAdapter(adapterId), method, new CimMethodParametersCollection()), method);

    private void InvokeStatic(string className, string method, params (string Name, object Value, CimType Type)[] parameters)
    {
        var collection = new CimMethodParametersCollection();
        foreach (var (name, value, type) in parameters)
            collection.Add(CimMethodParameter.Create(name, value, type, CimFlags.In));
        CheckReturn(_session.InvokeMethod(Namespace, className, method, collection), $"{className}.{method}");
    }

    private void DeleteEverywhere(string query)
    {
        foreach (var store in PolicyStores)
        {
            var options = StoreOptions(store);
            foreach (var instance in _session.QueryInstances(Namespace, "WQL", query, options).ToList())
            {
                try
                {
                    _session.DeleteInstance(Namespace, instance, options);
                }
                catch (CimException ex) when (ex.NativeErrorCode == NativeErrorCode.NotFound)
                {
                    // Already gone: removal is idempotent.
                }
            }
        }
    }

    private static void IgnoreAlreadyExists(Action action)
    {
        try
        {
            action();
        }
        catch (CimException ex) when (ex.NativeErrorCode == NativeErrorCode.AlreadyExists)
        {
        }
    }

    private static void CheckReturn(CimMethodResult result, string what)
    {
        if (result.ReturnValue?.Value is { } value && Convert.ToUInt32(value) != 0)
            throw new InvalidOperationException($"{what} failed with code {value}.");
    }

    private static CimOperationOptions StoreOptions(string store)
    {
        var options = new CimOperationOptions();
        options.SetCustomOption("PolicyStore", store, mustComply: false);
        return options;
    }

    private CimInstance FindAdapter(Guid adapterId) =>
        TryFindAdapter(adapterId) ?? throw new InvalidOperationException($"Adapter {adapterId:B} not found.");

    private CimInstance? TryFindAdapter(Guid adapterId) =>
        Query($"SELECT * FROM MSFT_NetAdapter WHERE InterfaceGuid = '{adapterId.ToString("B").ToUpperInvariant()}'").FirstOrDefault();

    private IEnumerable<CimInstance> Query(string wql) => _session.QueryInstances(Namespace, "WQL", wql);

    private static AdapterInfo ToInfo(CimInstance adapter)
    {
        var description = Get<string>(adapter, "InterfaceDescription") ?? "";
        var isVirtual = Get<bool?>(adapter, "Virtual") == true || Get<bool?>(adapter, "HardwareInterface") == false;
        MacAddress.TryNormalize(Get<string>(adapter, "PermanentAddress") ?? Get<string>(adapter, "MacAddress"), out var mac);

        return new AdapterInfo
        {
            Id = Guid.Parse(Get<string>(adapter, "InterfaceGuid")!),
            Name = Get<string>(adapter, "Name") ?? "",
            Description = description,
            Mac = mac ?? "",
            InterfaceIndex = Get<uint>(adapter, "InterfaceIndex"),
            Kind = KindOf(Get<uint>(adapter, "NdisPhysicalMedium"), description),
            IsVirtual = isVirtual,
        };
    }

    private static AdapterKind KindOf(uint ndisPhysicalMedium, string description) => ndisPhysicalMedium switch
    {
        1 or 9 => AdapterKind.WiFi,         // WirelessLan, Native802_11
        8 => AdapterKind.Cellular,          // WirelessWan
        10 => AdapterKind.Bluetooth,
        14 => AdapterKind.Ethernet,         // 802_3
        _ when description.Contains("VPN", StringComparison.OrdinalIgnoreCase)
            || description.Contains("TAP", StringComparison.OrdinalIgnoreCase)
            || description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) => AdapterKind.Vpn,
        _ => AdapterKind.Other,
    };

    private static IpCidr? ToCidr(CimInstance address) =>
        IpCidr.TryParse($"{Get<string>(address, "IPAddress")?.Split('%')[0]}/{Get<uint>(address, "PrefixLength")}", out var cidr) ? cidr : null;

    private static T? Get<T>(CimInstance instance, string property)
    {
        var value = instance.CimInstanceProperties[property]?.Value;
        if (value is null)
            return default;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, target);
    }

    public void Dispose() => _session.Dispose();
}
