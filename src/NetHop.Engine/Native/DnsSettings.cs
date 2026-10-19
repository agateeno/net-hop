using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NetHop.Engine.Native;

/// <summary>
/// Per-interface DNS configuration through <c>Get/SetInterfaceDnsSettings</c> (iphlpapi, Windows 10 2004+).
/// Unlike netsh this is locale-independent, and an empty server list cleanly returns DNS to DHCP.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class DnsSettings
{
    private const uint Version1 = 1;
    private const ulong SettingIpv6 = 0x0001;
    private const ulong SettingNameServer = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsInterfaceSettings
    {
        public uint Version;
        public ulong Flags;
        public IntPtr Domain;
        public IntPtr NameServer;
        public IntPtr SearchList;
        public uint RegistrationEnabled;
        public uint RegisterAdapterName;
        public uint EnableLlmnr;
        public uint QueryAdapterName;
        public IntPtr ProfileNameServer;
    }

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetInterfaceDnsSettings(Guid adapterId, ref DnsInterfaceSettings settings);

    [LibraryImport("iphlpapi.dll")]
    private static partial uint SetInterfaceDnsSettings(Guid adapterId, ref DnsInterfaceSettings settings);

    [LibraryImport("iphlpapi.dll")]
    private static partial void FreeInterfaceDnsSettings(ref DnsInterfaceSettings settings);

    /// <summary>Returns the statically configured servers; empty when DNS comes from DHCP.</summary>
    public static IReadOnlyList<IPAddress> GetStaticServers(Guid adapterId, AddressFamily family)
    {
        var settings = new DnsInterfaceSettings
        {
            Version = Version1,
            Flags = SettingNameServer | (family == AddressFamily.InterNetworkV6 ? SettingIpv6 : 0),
        };

        var error = GetInterfaceDnsSettings(adapterId, ref settings);
        if (error != 0)
            return [];

        try
        {
            var text = Marshal.PtrToStringUni(settings.NameServer) ?? "";
            return text
                .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => IPAddress.TryParse(s, out var ip) ? ip : null)
                .OfType<IPAddress>()
                .ToList();
        }
        finally
        {
            FreeInterfaceDnsSettings(ref settings);
        }
    }

    /// <param name="servers">Empty to return to automatic (DHCP) DNS.</param>
    public static void SetServers(Guid adapterId, AddressFamily family, IReadOnlyList<IPAddress> servers)
    {
        var nameServer = Marshal.StringToHGlobalUni(string.Join(',', servers));
        try
        {
            var settings = new DnsInterfaceSettings
            {
                Version = Version1,
                Flags = SettingNameServer | (family == AddressFamily.InterNetworkV6 ? SettingIpv6 : 0),
                NameServer = nameServer,
            };

            var error = SetInterfaceDnsSettings(adapterId, ref settings);
            if (error != 0)
                throw new Win32Exception((int)error);
        }
        finally
        {
            Marshal.FreeHGlobal(nameServer);
        }
    }
}
