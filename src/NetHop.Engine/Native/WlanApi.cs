using System.Runtime.InteropServices;
using System.Text;

namespace NetHop.Engine.Native;

/// <summary>
/// Reads the SSID of the network a Wi-Fi interface is connected to.
/// Since Windows 11 24H2 this requires location access; when it is denied the call fails
/// and <see cref="LastCallDenied"/> becomes true so the UI can ask the user to enable it.
/// </summary>
internal static partial class WlanApi
{
    private const uint ClientVersion = 2;
    private const int OpcodeCurrentConnection = 7;
    private const uint ErrorAccessDenied = 5;

    // WLAN_CONNECTION_ATTRIBUTES: isState(4) + wlanConnectionMode(4) + strProfileName(WCHAR[256]) = 520,
    // then WLAN_ASSOCIATION_ATTRIBUTES starts with DOT11_SSID { ULONG uSSIDLength; UCHAR ucSSID[32]; }.
    private const int SsidLengthOffset = 4 + 4 + 256 * 2;
    private const int SsidBytesOffset = SsidLengthOffset + 4;

    public static bool LastCallDenied { get; private set; }

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanQueryInterface(IntPtr clientHandle, in Guid interfaceGuid, int opCode, IntPtr reserved,
        out uint dataSize, out IntPtr data, out int opcodeValueType);

    [LibraryImport("wlanapi.dll")]
    private static partial void WlanFreeMemory(IntPtr memory);

    /// <summary>Null when the interface is not connected, is not Wi-Fi or the SSID cannot be read.</summary>
    public static string? GetConnectedSsid(Guid interfaceId)
    {
        if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out var handle) != 0)
            return null;

        try
        {
            var error = WlanQueryInterface(handle, interfaceId, OpcodeCurrentConnection, IntPtr.Zero, out _, out var data, out _);
            LastCallDenied = error == ErrorAccessDenied;
            if (error != 0 || data == IntPtr.Zero)
                return null;

            try
            {
                var length = Math.Clamp(Marshal.ReadInt32(data, SsidLengthOffset), 0, 32);
                var bytes = new byte[length];
                Marshal.Copy(data + SsidBytesOffset, bytes, 0, length);
                return length == 0 ? null : Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }
        finally
        {
            WlanCloseHandle(handle, IntPtr.Zero);
        }
    }
}
