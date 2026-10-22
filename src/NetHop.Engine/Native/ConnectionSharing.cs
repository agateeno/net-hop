using NetHop.Core.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace NetHop.Engine.Native;

/// <summary>
/// Internet Connection Sharing (the HNetCfg COM API behind the Sharing tab of an adapter's properties) and the Windows
/// mobile hotspot (WinRT tethering manager). Windows allows one sharing pair at a time, and the hotspot uses it too:
/// while it is on, it shares its source connection with its own virtual Wi-Fi adapter.
/// </summary>
internal static class ConnectionSharing
{
    // ICSSHARINGTYPE
    private const int SharingPublic = 0;
    private const int SharingPrivate = 1;

    private static readonly TimeSpan HotspotTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Null when Internet Connection Sharing cannot be read (for example, the service that runs it is disabled).</summary>
    public static SharingSnapshot? Read()
    {
        try
        {
            var (from, to) = ReadPair();
            return new SharingSnapshot { HotspotOn = IsHotspotOn(), SharedFrom = from, SharedTo = to };
        }
        catch (Exception ex)
        {
            // COM, WinRT and late-binding errors all mean the same here: the state is unknown.
            System.Diagnostics.Debug.WriteLine($"Could not read connection sharing: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Turns the hotspot on, sharing <paramref name="sourceId"/> (null: the internet connection Windows uses). Windows
    /// tethers only some connections directly; others, such as most VPN adapters, are shared the way it is done by hand:
    /// the hotspot starts from the internet connection, then Internet Connection Sharing is pointed at the requested one.
    /// </summary>
    public static void ShareThroughHotspot(Guid? sourceId, CancellationToken cancellationToken)
    {
        var on = IsHotspotOn();
        if (on && (sourceId is null || ReadPair().From == sourceId))
            return;

        var source = sourceId is { } id
            ? NetworkInformation.GetConnectionProfiles().FirstOrDefault(p => p.NetworkAdapter?.NetworkAdapterId == id)
            : null;
        var direct = source is not null
            && NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(source) == TetheringCapability.Enabled;

        if (on && direct)
            StopHotspot(cancellationToken); // restarted below from the requested connection
        if (!on || direct)
            StartHotspot(direct ? source! : InternetProfile(), cancellationToken);
        if (sourceId is { } wanted && !direct)
            ShareWith(wanted, WaitForHotspotAdapter(cancellationToken));
    }

    public static void StopHotspot(CancellationToken cancellationToken)
    {
        if (!IsHotspotOn())
            return;
        var result = Manager(AnyProfile() ?? InternetProfile()).StopTetheringAsync().AsTask(cancellationToken).WaitAsync(HotspotTimeout, cancellationToken).GetAwaiter().GetResult();
        if (result.Status != TetheringOperationStatus.Success)
            throw new InvalidOperationException($"The mobile hotspot did not stop: {Describe(result)}");
    }

    /// <summary>Shares <paramref name="publicId"/> with <paramref name="privateId"/>; sharing on any other adapter stops.</summary>
    public static void ShareWith(Guid publicId, Guid privateId)
    {
        object? publicConfig = null, privateConfig = null;
        foreach (var (id, config) in Connections())
        {
            if (id == publicId)
                publicConfig = config;
            else if (id == privateId)
                privateConfig = config;
            else if ((bool)config.SharingEnabled)
                config.DisableSharing(); // one pair at a time
        }
        if (publicConfig is null || privateConfig is null)
            throw new InvalidOperationException($"Adapter {(publicConfig is null ? publicId : privateId):B} is not available for sharing.");

        Share(publicConfig, SharingPublic);
        Share(privateConfig, SharingPrivate);
    }

    public static void StopSharing()
    {
        foreach (var (_, config) in Connections())
        {
            if ((bool)config.SharingEnabled)
                config.DisableSharing();
        }
    }

    private static void Share(dynamic config, int type)
    {
        if ((bool)config.SharingEnabled)
        {
            if ((int)config.SharingConnectionType == type)
                return;
            config.DisableSharing();
        }
        config.EnableSharing(type);
    }

    private static (Guid? From, Guid? To) ReadPair()
    {
        Guid? from = null, to = null;
        foreach (var (id, config) in Connections())
        {
            if (!(bool)config.SharingEnabled)
                continue;
            if ((int)config.SharingConnectionType == SharingPublic)
                from = id;
            else
                to = id;
        }
        return (from, to);
    }

    private static List<(Guid Id, dynamic Config)> Connections()
    {
        var type = Type.GetTypeFromProgID("HNetCfg.HNetShare", throwOnError: true)!;
        dynamic manager = Activator.CreateInstance(type)!;
        var result = new List<(Guid, dynamic)>();
        foreach (dynamic connection in manager.EnumEveryConnection)
        {
            dynamic properties = manager.NetConnectionProps[connection];
            if (Guid.TryParse((string)properties.Guid, out var id))
                result.Add((id, manager.INetSharingConfigurationForINetConnection[connection]));
        }
        return result;
    }

    private static bool IsHotspotOn()
    {
        try
        {
            return AnyProfile() is { } profile && Manager(profile).TetheringOperationalState == TetheringOperationalState.On;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return false; // no Wi-Fi adapter that can host a hotspot
        }
    }

    private static void StartHotspot(ConnectionProfile source, CancellationToken cancellationToken)
    {
        var result = Manager(source).StartTetheringAsync().AsTask(cancellationToken).WaitAsync(HotspotTimeout, cancellationToken).GetAwaiter().GetResult();
        if (result.Status != TetheringOperationStatus.Success)
            throw new InvalidOperationException($"The mobile hotspot did not start: {Describe(result)}");
    }

    /// <summary>The hotspot's own adapter: the receiving end of the sharing pair it sets up when it starts.</summary>
    private static Guid WaitForHotspotAdapter(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + HotspotTimeout;
        while (true)
        {
            if (ReadPair().To is { } adapter)
                return adapter;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The mobile hotspot started, but its adapter did not appear in Internet Connection Sharing.");
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Any connection: the tethering manager needs one, but its on/off state and stopping are machine-wide.</summary>
    private static ConnectionProfile? AnyProfile() =>
        NetworkInformation.GetInternetConnectionProfile() ?? NetworkInformation.GetConnectionProfiles().FirstOrDefault();

    private static ConnectionProfile InternetProfile() =>
        NetworkInformation.GetInternetConnectionProfile()
        ?? throw new InvalidOperationException("There is no internet connection to start the mobile hotspot from.");

    private static NetworkOperatorTetheringManager Manager(ConnectionProfile profile) =>
        NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);

    private static string Describe(NetworkOperatorTetheringOperationResult result) =>
        string.IsNullOrWhiteSpace(result.AdditionalErrorMessage) ? result.Status.ToString() : $"{result.Status}, {result.AdditionalErrorMessage}";
}
