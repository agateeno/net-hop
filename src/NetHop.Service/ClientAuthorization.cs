using System.Runtime.InteropServices;
using System.Security.Principal;

namespace NetHop.Service;

/// <summary>
/// Decides whether a pipe client may talk to the service: only members of the Administrators group (and SYSTEM).
/// </summary>
/// <remarks>
/// With UAC on, an administrator's normal (non-elevated) token still lists the Administrators group,
/// but as "deny only". <see cref="WindowsPrincipal.IsInRole(WindowsBuiltInRole)"/> and
/// <see cref="WindowsIdentity.Groups"/> both ignore deny-only groups, so the token groups are read
/// directly: this way the tray app works without elevation, yet standard users are refused.
/// </remarks>
internal static partial class ClientAuthorization
{
    private const int TokenGroupsClass = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
        int tokenInformationLength, out int returnLength);

    public static bool IsAdministratorAccount(WindowsIdentity identity)
    {
        if (identity.IsSystem || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return true;

        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        GetTokenInformation(identity.Token, TokenGroupsClass, IntPtr.Zero, 0, out var length);
        if (length <= 0)
            return false;

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(identity.Token, TokenGroupsClass, buffer, length, out _))
                return false;

            // TOKEN_GROUPS: DWORD GroupCount, then SID_AND_ATTRIBUTES[] aligned to pointer size.
            var count = Marshal.ReadInt32(buffer);
            var entrySize = Marshal.SizeOf<SidAndAttributes>();
            for (var i = 0; i < count; i++)
            {
                var entry = Marshal.PtrToStructure<SidAndAttributes>(buffer + IntPtr.Size + i * entrySize);
                if (new SecurityIdentifier(entry.Sid) == administrators)
                    return true;
            }
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
