using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using NetHop.Ipc;

namespace NetHop.App.Services;

/// <summary>
/// Turns the NetHop Windows service on (registers and starts it) and off (stops and removes it). NetHop installs
/// without it: the app then changes the network itself, and only the service runs automations and serves the
/// command line, Power Automate and the Logi plugin. Changes need an elevated process.
/// </summary>
public static partial class ServiceSetup
{
    private const string Description = "Applies NetHop network profiles and runs NetHop automations.";
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStopped = 1;
    private const uint ServiceRunning = 4;

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(30);

    public static string ServiceExePath => Path.Combine(AppContext.BaseDirectory, "NetHop.Service.exe");

    public static bool IsInstalled => QueryState() is not null;

    public static bool IsRunning => QueryState() == ServiceRunning;

    /// <summary>The process is elevated: it may change the service and, without the service, the network.</summary>
    public static bool IsElevated => Environment.IsPrivilegedProcess;

    /// <summary>Registers the service, starts it and waits until it runs.</summary>
    /// <exception cref="InvalidOperationException">It cannot be installed from this folder, or a step failed.</exception>
    public static void Install()
    {
        if (!File.Exists(ServiceExePath))
            throw new InvalidOperationException(Loc.Format("Service_MissingExe", ServiceExePath));
        // The service runs as SYSTEM from here: anyone who can change these files could run code as SYSTEM.
        if (!IsProtected(AppContext.BaseDirectory) || !IsProtected(ServiceExePath))
            throw new InvalidOperationException(Loc.Format("Service_UnsafeFolder", AppContext.BaseDirectory));

        if (!IsInstalled)
        {
            Sc("create", NetHopPipe.ServiceName, "binPath=", $"\"{ServiceExePath}\"", "start=", "auto", "DisplayName=", "NetHop");
            Sc("description", NetHopPipe.ServiceName, Description);
            // Restart automatically if it crashes, as the MSI did up to 0.5.
            Sc("failure", NetHopPipe.ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/30000");
        }
        Start();
    }

    /// <summary>Starts the service if it is installed and not running (after an upgrade, for example).</summary>
    public static void Start()
    {
        if (QueryState() is null or ServiceRunning)
            return;
        Sc("start", NetHopPipe.ServiceName);
        WaitFor(ServiceRunning);
    }

    /// <summary>Stops and removes the service. Profiles, rules and history stay in %ProgramData%\NetHop.</summary>
    public static void Remove()
    {
        var state = QueryState();
        if (state is null)
            return;
        if (state != ServiceStopped)
        {
            Sc("stop", NetHopPipe.ServiceName);
            WaitFor(ServiceStopped);
        }
        Sc("delete", NetHopPipe.ServiceName);
    }

    private static void WaitFor(uint wanted)
    {
        var watch = Stopwatch.StartNew();
        while (QueryState() != wanted)
        {
            if (watch.Elapsed > StateTimeout)
                throw new InvalidOperationException(Loc.Get("Service_Timeout"));
            Thread.Sleep(250);
        }
    }

    private static void Sc(params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"sc {arguments[0]}: {output.Trim()}");
    }

    /// <summary>
    /// Only SYSTEM, Administrators and TrustedInstaller own the path and may change it, as in Program Files (and in
    /// any folder Install-NetHop.ps1 installs to).
    /// </summary>
    private static bool IsProtected(string path)
    {
        const FileSystemRights Writes = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete
            | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !IsTrusted(owner))
            return false;

        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            // Inherit-only entries (CREATOR OWNER in Program Files) do not apply to the path itself.
            if (rule.AccessControlType == AccessControlType.Allow && !rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)
                && (rule.FileSystemRights & Writes) != 0 && !IsTrusted((SecurityIdentifier)rule.IdentityReference))
                return false;
        }
        return true;
    }

    private static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    private static bool IsTrusted(SecurityIdentifier sid) =>
        sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || sid == TrustedInstaller;

    /// <summary>The service's state (SERVICE_STOPPED, SERVICE_RUNNING...), or null when it is not installed.</summary>
    private static uint? QueryState()
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
            return null;
        try
        {
            var service = OpenService(manager, NetHopPipe.ServiceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
                return null;
            try
            {
                return QueryServiceStatus(service, out var status) ? status.CurrentState : null;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct ScServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatus(IntPtr service, out ScServiceStatus status);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr handle);
}
