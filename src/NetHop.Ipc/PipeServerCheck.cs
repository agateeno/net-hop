using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NetHop.Ipc;

/// <summary>
/// The pipe server is not the NetHop service. Derives from <see cref="TimeoutException"/> so that every client
/// treats it like a service that is not running, which, for the real service, it is.
/// </summary>
public sealed class UntrustedPipeServerException(string message) : TimeoutException(message);

/// <summary>
/// Pipe names are global: while the service is stopped or restarting, any local user could create
/// <see cref="NetHopPipe.PipeName"/> and pose as NetHop, feeding clients fake data and rule actions that the app
/// would run in an administrator's session. Clients therefore only talk to the process of the NetHop service,
/// as the Service Control Manager reports it (registering or replacing a service requires an administrator).
/// </summary>
internal static partial class PipeServerCheck
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ScStatusProcessInfo = 0;
    private const int ServiceStatusProcessSize = 36; // SERVICE_STATUS_PROCESS: nine DWORDs
    private const int ProcessIdOffset = 28;          // dwProcessId, the eighth DWORD

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatusEx(IntPtr service, int infoLevel, Span<byte> buffer, int bufferSize, out int bytesNeeded);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr handle);

    /// <exception cref="UntrustedPipeServerException">Another process serves the pipe, or the service is not running.</exception>
    public static void EnsureServedByService(NamedPipeClientStream pipe)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverProcessId))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        var serviceProcessId = GetServiceProcessId();
        if (serviceProcessId == 0 || serviceProcessId != serverProcessId)
            throw new UntrustedPipeServerException(
                $"The NetHop pipe is served by process {serverProcessId}, not by the NetHop service. Refusing to connect.");
    }

    /// <summary>The process id of the running NetHop service, or 0 when it is not running or not installed.</summary>
    private static uint GetServiceProcessId()
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
            return 0;
        try
        {
            var service = OpenService(manager, NetHopPipe.ServiceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
                return 0;
            try
            {
                Span<byte> status = stackalloc byte[ServiceStatusProcessSize];
                return QueryServiceStatusEx(service, ScStatusProcessInfo, status, status.Length, out _)
                    ? BitConverter.ToUInt32(status[ProcessIdOffset..])
                    : 0;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }
}
