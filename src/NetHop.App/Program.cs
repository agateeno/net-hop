using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace NetHop.App;

/// <summary>
/// Custom entry point: NetHop runs as a single instance living in the tray. A second launch
/// (double-clicking a .nethop file, a nethop:// link, a Logi Options+ shortcut) is redirected to it.
/// </summary>
public static class Program
{
    private const string InstanceKey = "NetHop.Main";

    /// <summary>Passed with the process id by "Restart as administrator": wait for that instance to exit first.</summary>
    public const string RestartedFromArgument = "--restarted-from";

    /// <summary>Settings › NetHop service, from an app that is not elevated: the elevated instance turns it on or off.</summary>
    public const string EnableServiceArgument = "--enable-service";
    public const string DisableServiceArgument = "--disable-service";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Services.CrashLog.Write(".NET", e.ExceptionObject);

        // Helpers started by the MSI: no window, no single-instance redirect.
        if (args.Contains("--uninstall"))
        {
            Services.AppLanguage.Apply();
            Services.UninstallCleanup.Run(quiet: args.Contains("--quiet"));
            return 0;
        }
        if (args.Contains("--uninstall-machine"))
            return Services.UninstallCleanup.RunForMachine();
        if (args.Contains("--install-service"))
            return RunHelper(Services.ServiceSetup.Install); // after an upgrade, when the service was on

        WaitForPreviousInstance(args);
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

        if (!mainInstance.IsCurrent)
        {
            // Redirect on a worker thread: blocking the STA thread on the async call can deadlock.
            Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        // Without the service NetHop changes the network itself, which takes administrator rights: one UAC prompt now.
        // Cancelled, NetHop runs without them and offers to restart as administrator.
        if (!Services.ServiceSetup.IsElevated && !Services.ServiceSetup.IsInstalled && RestartElevated(args))
            return 0;

        mainInstance.Activated += (_, e) => App.Current?.HandleActivation(e, isLaunch: false);
        Services.AppLanguage.Apply();

        // The parameter must not be named "_": inside the lambda "_ = new App()" would then assign to it.
        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(activation);
        });
        return 0;
    }

    /// <summary>Starts this executable again, elevated, with the same arguments. False when the UAC prompt is cancelled.</summary>
    public static bool RestartElevated(IEnumerable<string> args)
    {
        var list = args.ToList();
        var previous = list.IndexOf(RestartedFromArgument);
        if (previous >= 0)
            list.RemoveRange(previous, Math.Min(2, list.Count - previous));
        list.Add(RestartedFromArgument);
        list.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var arguments = list.Select(a => a.Contains(' ') || a.Length == 0 ? $"\"{a}\"" : a);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", arguments),
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static int RunHelper(Action action)
    {
        try
        {
            action();
            return 0;
        }
        catch (Exception ex)
        {
            Services.CrashLog.Write("Helper", ex);
            return 1;
        }
    }

    private static void WaitForPreviousInstance(string[] args)
    {
        var index = Array.IndexOf(args, RestartedFromArgument);
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var processId))
            return;
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(processId);
            previous.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }
}
