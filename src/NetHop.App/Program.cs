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

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Services.CrashLog.Write(".NET", e.ExceptionObject);

        // Uninstall helper started by the MSI: no window, no single-instance redirect.
        if (args.Contains("--uninstall"))
        {
            Services.AppLanguage.Apply();
            Services.UninstallCleanup.Run();
            return 0;
        }

        WaitForPreviousInstance(args);
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

        if (!mainInstance.IsCurrent)
        {
            // Redirect on a worker thread: blocking the STA thread on the async call can deadlock.
            Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

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
