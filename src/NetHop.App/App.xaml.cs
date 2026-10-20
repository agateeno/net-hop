using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using NetHop.App.Services;
using NetHop.App.Tray;
using NetHop.Core;
using Windows.ApplicationModel.Activation;

namespace NetHop.App;

public partial class App : Application
{
    private readonly AppActivationArguments _launchActivation;
    private MainWindow? _mainWindow;
    private TrayIconManager? _tray;
    private bool _exiting;

    public App(AppActivationArguments launchActivation)
    {
        _launchActivation = launchActivation;
        Current = this;
        UnhandledException += (_, e) => CrashLog.Write("XAML", e.Exception);
        InitializeComponent();
    }

    public static new App? Current { get; private set; }

    public ServiceConnection Connection { get; } = new();

    /// <summary>Owner window for file pickers, which unpackaged apps must initialize with an HWND.</summary>
    public Window MainWindowForDialogs => _mainWindow ?? throw new InvalidOperationException("The main window is not open.");

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        ShellIntegration.Register();
        _ = ContextMenuPackage.EnsureRegisteredAsync();
        ShellIntegration.ApplyFirstRunDefaults();
        ProfileFolder.EnsureCreated();
        Connection.PropertyChanged += (_, e) =>
        {
            // Keep %USERPROFILE%\NetHop Profiles in step with the profiles.
            if (e.PropertyName == nameof(ServiceConnection.Profiles) && Connection.IsConnected)
                _ = ProfileFolder.SyncAsync(Connection.Service, Connection.Profiles);
        };
        UserActionRunner.Initialize(Connection);
        Connection.Start();
        UpdateCheck.Start();

        _tray = new TrayIconManager(Connection, ShowMainWindow, ExitApp);
        HandleActivation(_launchActivation, isLaunch: true);
    }

    /// <summary>Handles the first launch and activations redirected from later launches.</summary>
    public void HandleActivation(AppActivationArguments activation, bool isLaunch)
    {
        // Redirected activations arrive on a background thread.
        if (_tray is null || !Connection.Dispatcher.HasThreadAccess)
        {
            Connection.Dispatcher.TryEnqueue(() => HandleActivation(activation, isLaunch));
            return;
        }

        switch (activation.Kind)
        {
            case ExtendedActivationKind.File when activation.Data is IFileActivatedEventArgs fileArgs && fileArgs.Files.Count > 0:
                ShowMainWindow();
                _mainWindow!.ImportProfileFile(fileArgs.Files[0].Path);
                break;

            case ExtendedActivationKind.Protocol when activation.Data is IProtocolActivatedEventArgs protocol:
                HandleUri(protocol.Uri.OriginalString);
                break;

            default:
                var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
                if (activation.Data is ILaunchActivatedEventArgs launch && !string.IsNullOrWhiteSpace(launch.Arguments))
                    arguments = SplitCommandLine(launch.Arguments);

                var uri = arguments.FirstOrDefault(a => a.StartsWith(NetHopConstants.UriScheme + ":", StringComparison.OrdinalIgnoreCase));
                var file = arguments.FirstOrDefault(a => a.EndsWith(NetHopConstants.ProfileExtension, StringComparison.OrdinalIgnoreCase));
                if (file is not null && arguments.Contains("--apply"))
                    ApplyFile(file); // "Apply with NetHop" in Explorer's context menu
                else if (uri is not null)
                    HandleUri(uri);
                else if (file is not null)
                {
                    ShowMainWindow();
                    _mainWindow!.ImportProfileFile(file);
                }
                else if (!(isLaunch && arguments.Contains("--tray")))
                    ShowMainWindow(); // started at logon with --tray: stay in the tray
                break;
        }
    }

    /// <summary>
    /// Applies a .nethop file that is one of the saved profiles (the copies in the profiles folder), using the saved
    /// version. Any other file opens in the editor instead: a downloaded profile could, for example, point DNS at
    /// an attacker's server, so it is never saved and applied without the user seeing it first.
    /// </summary>
    private async void ApplyFile(string path)
    {
        try
        {
            // At launch the service connection may still be opening.
            for (var i = 0; i < 20 && Connection.State == ConnectionState.Connecting; i++)
                await Task.Delay(500);
            if (!Connection.IsConnected)
            {
                UserActionRunner.Notify(Loc.Get("Main_ServiceStoppedTitle"));
                return;
            }

            var profile = Core.Profiles.ProfileSerializer.Deserialize(await File.ReadAllTextAsync(path));
            if (Connection.Profiles.All(p => p.Id != profile.Id))
            {
                ShowMainWindow();
                _mainWindow!.ImportProfileFile(path);
                UserActionRunner.Notify(Loc.Format("Apply_ReviewFirst", profile.Name), Loc.Get("Profiles_ImportedHelp"));
                return;
            }
            await Connection.ApplyAsync(profile.Id.ToString()); // the saved profile, not the file; the result arrives as a toast
        }
        catch (Exception ex) when (ex is IOException or Core.Serialization.NetHopFormatException or UnauthorizedAccessException)
        {
            UserActionRunner.Notify(Loc.Format("Common_ProfileFailed", Path.GetFileName(path)), ex.Message);
        }
    }

    private async void HandleUri(string text)
    {
        if (!NetHopUri.TryParse(text, out var uri))
            return;

        switch (uri.Command)
        {
            case UriCommand.Apply:
                await Connection.ApplyAsync(uri.Profile!);
                break;
            default:
                ShowMainWindow();
                break;
        }
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(Connection);
            _mainWindow.AppWindow.Closing += (sender, e) =>
            {
                if (_exiting)
                    return;
                // Closing the window keeps NetHop in the tray, like PowerToys.
                e.Cancel = true;
                sender.Hide();
            };
        }

        _mainWindow.ShowAndFocus();
    }

    public void ShowSettings()
    {
        ShowMainWindow();
        _mainWindow!.ShowSettings();
    }

    /// <summary>
    /// Starts NetHop again through UAC (which asks for an administrator's credentials) and closes this instance.
    /// The new instance waits for this one to exit before taking over the single-instance key and the tray.
    /// </summary>
    public void RestartAsAdministrator()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"{Program.RestartedFromArgument} {Environment.ProcessId}",
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return; // UAC prompt cancelled: keep running read-only
        }
        ExitApp();
    }

    private async void ExitApp()
    {
        _exiting = true;
        _tray?.Dispose();
        await Connection.DisposeAsync();
        _mainWindow?.Close();
        Current = null;
        Exit(); // Application.Exit
    }

    /// <summary>Splits a command line, keeping quoted paths with spaces together.</summary>
    private static string[] SplitCommandLine(string commandLine) =>
        Regex.Matches(commandLine, "\"([^\"]*)\"|(\\S+)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .ToArray();
}
