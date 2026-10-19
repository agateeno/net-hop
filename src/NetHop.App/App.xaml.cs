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
        InitializeComponent();
    }

    public static new App? Current { get; private set; }

    public ServiceConnection Connection { get; } = new();

    /// <summary>Owner window for file pickers, which unpackaged apps must initialize with an HWND.</summary>
    public Window MainWindowForDialogs => _mainWindow ?? throw new InvalidOperationException("The main window is not open.");

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        ShellIntegration.Register();
        ShellIntegration.ApplyFirstRunDefaults();
        UserActionRunner.Initialize(Connection);
        Connection.Start();

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
            case ExtendedActivationKind.File when activation.Data is IFileActivatedEventArgs file && file.Files.Count > 0:
                ShowMainWindow();
                _mainWindow!.ImportProfileFile(file.Files[0].Path);
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
                if (uri is not null)
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

    private async void HandleUri(string text)
    {
        if (!NetHopUri.TryParse(text, out var uri))
            return;

        switch (uri.Command)
        {
            case UriCommand.Apply:
                await Connection.ApplyAsync(uri.Profile!);
                break;
            case UriCommand.Next:
                await Connection.ApplyNextAsync();
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
