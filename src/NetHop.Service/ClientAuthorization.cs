using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace NetHop.Service;

/// <summary>
/// Decides how much a pipe client may do: members of the Administrators group (and SYSTEM) get full access,
/// everyone else a read-only connection.
/// </summary>
/// <remarks>
/// With UAC on, an administrator's normal (non-elevated) token still lists the Administrators group,
/// but as "deny only". <see cref="WindowsPrincipal.IsInRole(WindowsBuiltInRole)"/> and
/// <see cref="WindowsIdentity.Groups"/> both ignore deny-only groups, so the token groups are read
/// directly: this way the tray app works without elevation, yet standard users only get read access.
/// </remarks>
internal static partial class ClientAuthorization
{
    private const int TokenGroupsClass = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    // IsInRole duplicates the token into an impersonation token for CheckTokenMembership.
    private const uint TokenDuplicate = 0x0002;

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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, uint desiredAccess, out SafeAccessTokenHandle token);

    /// <summary>The identity of the process at the other end of the pipe (the service runs as SYSTEM, so it may open its token).</summary>
    /// <exception cref="Win32Exception">The client could not be identified.</exception>
    public static WindowsIdentity GetClientIdentity(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var processId))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        if (!OpenProcessToken(process, TokenQuery | TokenDuplicate, out var token))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        using (token)
            return new WindowsIdentity(token.DangerousGetHandle());
    }

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
