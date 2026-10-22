using System.Runtime.InteropServices;

namespace NetHop.Host;

internal static partial class PowerStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>Null when unknown (desktop PC or API failure).</summary>
    public static bool? IsOnBattery() =>
        GetSystemPowerStatus(out var status) && status.ACLineStatus != 255 ? status.ACLineStatus == 0 : null;
}
