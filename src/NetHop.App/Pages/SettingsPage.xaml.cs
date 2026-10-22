using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetHop.App.Services;

namespace NetHop.App.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly ServiceConnection _connection = App.Current!.Connection;
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        AboutCard.Description = Loc.Format("Settings_Version", Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        UpdateCheck.Changed += OnUpdateCheckChanged;
        _connection.PropertyChanged += OnConnectionChanged;
        Unloaded += (_, _) =>
        {
            UpdateCheck.Changed -= OnUpdateCheckChanged;
            _connection.PropertyChanged -= OnConnectionChanged;
        };
    }

    private static readonly (string Tag, string Label)[] Languages =
    [
        (AppLanguage.SystemDefault, "Settings_LanguageSystem"),
        ("de-DE", "Deutsch"),
        ("en-US", "English"),
        ("es-ES", "Español"),
        ("fr-FR", "Français"),
        ("it-IT", "Italiano"),
        ("pt-BR", "Português (Brasil)"),
    ];

    private static readonly (ElementTheme Theme, string Label)[] Themes =
    [
        (ElementTheme.Default, "Settings_ThemeSystem"),
        (ElementTheme.Light, "Settings_ThemeLight"),
        (ElementTheme.Dark, "Settings_ThemeDark"),
    ];

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        StartupSwitch.IsOn = ShellIntegration.StartsWithWindows;
        // Without these, WinUI shows "On"/"Off" in Windows' language rather than NetHop's.
        StartupSwitch.OnContent = Loc.Get("RuleForm_On");
        StartupSwitch.OffContent = Loc.Get("RuleForm_Off");
        UpdateAutoSwitch.IsOn = UpdateCheck.CheckAutomatically;
        PrereleaseSwitch.IsOn = UpdateCheck.IncludePrereleases;
        foreach (var toggle in new[] { UpdateAutoSwitch, PrereleaseSwitch, ServiceSwitch })
            (toggle.OnContent, toggle.OffContent) = (Loc.Get("RuleForm_On"), Loc.Get("RuleForm_Off"));
        // Language names are shown in their own language, as Windows does.
        LanguageBox.ItemsSource = Languages.Select(l => l.Tag == AppLanguage.SystemDefault ? Loc.Get(l.Label) : l.Label).ToList();
        LanguageBox.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.Tag == AppLanguage.Saved));
        ThemeBox.ItemsSource = Themes.Select(t => Loc.Get(t.Label)).ToList();
        ThemeBox.SelectedIndex = Math.Max(0, Array.FindIndex(Themes, t => t.Theme == AppTheme.Saved));
        _loading = false;
        ShowUpdateState();
        await ShowServiceStateAsync();
    }

    // The service turned on or off: the connection moves over a few seconds later.
    private async void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ServiceConnection.State) or nameof(ServiceConnection.IsLocal))
            await ShowServiceStateAsync();
    }

    private async Task ShowServiceStateAsync()
    {
        _loading = true;
        ServiceSwitch.IsOn = ServiceSetup.IsInstalled;
        _loading = false;

        var state = _connection.State switch
        {
            _ when !ServiceSetup.IsInstalled => Loc.Get("Settings_ServiceOff"),
            ConnectionState.Connected => Loc.Get("Settings_ServiceRunning"),
            ConnectionState.ServiceNotRunning => Loc.Get("Settings_ServiceStopped"),
            ConnectionState.AccessDenied => Loc.Get("Settings_ServiceNotAdmin"),
            _ => Loc.Get("Common_Connecting"),
        };
        if (_connection.IsConnected && !_connection.IsLocal)
        {
            try
            {
                var info = await _connection.Service.GetInfoAsync();
                state = Loc.Format("Settings_ServiceDetails", info.Version, info.ProfilesFolder)
                    + (info.ReadOnly ? " · " + Loc.Get("Settings_ServiceReadOnly") : "");
            }
            catch (Exception ex) when (ex is StreamJsonRpc.ConnectionLostException or InvalidOperationException)
            {
                // Disconnected meanwhile: the state change that follows shows it.
            }
        }
        ServiceCard.Description = state + Environment.NewLine + Loc.Get("Settings_ServiceNeededFor");
    }

    private async void OnServiceToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || ServiceSwitch.IsOn == ServiceSetup.IsInstalled || App.Current is not { } app)
            return;

        if (!ServiceSwitch.IsOn)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.Get("Settings_ServiceOffTitle"),
                Content = new TextBlock { Text = Loc.Get("Settings_ServiceOffQuestion"), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = Loc.Get("Settings_ServiceOffConfirm"),
                CloseButtonText = Loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                await ShowServiceStateAsync();
                return;
            }
        }

        ServiceSwitch.IsEnabled = false;
        ServiceProgress.IsActive = true;
        try
        {
            // Not elevated, this restarts NetHop through UAC and this page closes with it.
            await app.SetServiceEnabledAsync(ServiceSwitch.IsOn);
            _loading = true;
            StartupSwitch.IsOn = ShellIntegration.StartsWithWindows;
            _loading = false;
        }
        finally
        {
            ServiceProgress.IsActive = false;
            ServiceSwitch.IsEnabled = true;
            await ShowServiceStateAsync();
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedIndex < 0)
            return;
        AppLanguage.Saved = Languages[LanguageBox.SelectedIndex].Tag;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(LanguageBox, Loc.Get("Settings_LanguageRestart"));
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeBox.SelectedIndex >= 0)
            AppTheme.Saved = Themes[ThemeBox.SelectedIndex].Theme;
    }

    private void OnUpdateCheckChanged(object? sender, EventArgs e) => ShowUpdateState();

    private void ShowUpdateState()
    {
        var latest = UpdateCheck.Latest;
        var installed = Loc.Format("Settings_UpdateInstalled", UpdateCheck.CurrentVersion);
        var lastChecked = UpdateCheck.LastChecked is { } time
            ? installed + " · " + Loc.Format("Settings_UpdateLastChecked", time.LocalDateTime.ToString("t", System.Globalization.CultureInfo.CurrentCulture))
            : installed;

        (UpdateCard.Header, UpdateCard.Description, UpdateIcon.Glyph) = UpdateCheck.State switch
        {
            UpdateState.Checking => (Loc.Get("Settings_UpdateChecking"), installed, "\uE895"),
            UpdateState.Failed => (Loc.Get("Settings_UpdateFailed"), UpdateCheck.Error, "\uE7BA"),
            UpdateState.Available => (Loc.Format("Settings_UpdateAvailable", latest!.Version), lastChecked, "\uE896"),
            _ when UpdateCheck.IsDevelopmentBuild => (Loc.Get("Settings_UpdateDevelopment"),
                latest is null ? installed : installed + " · " + Loc.Format("Settings_UpdateLatestRelease", latest.Version), "\uEC7A"),
            UpdateState.UpToDate => (Loc.Get("Settings_UpdateUpToDate"), lastChecked, "\uE930"),
            _ => (Loc.Get("Settings_UpdateNotChecked"), installed, "\uE895"),
        };

        UpdateProgress.IsActive = UpdateCheck.State == UpdateState.Checking;
        CheckButton.IsEnabled = UpdateCheck.State != UpdateState.Checking;
        var available = UpdateCheck.State == UpdateState.Available;
        DownloadButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        ReleaseNotesButton.Visibility = latest is not null && !available ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e) => await UpdateCheck.CheckAsync();

    private void OnReleaseClick(object sender, RoutedEventArgs e) => UpdateCheck.OpenReleasePage();

    private void OnUpdateAutoToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            UpdateCheck.CheckAutomatically = UpdateAutoSwitch.IsOn;
    }

    private void OnPrereleaseToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            UpdateCheck.IncludePrereleases = PrereleaseSwitch.IsOn;
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => ProfileFolder.Open();

    private void OnGitHubClick(object sender, RoutedEventArgs e) => ShellIntegration.OpenUrl(Core.NetHopConstants.RepositoryUrl);

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        try
        {
            ShellIntegration.StartsWithWindows = StartupSwitch.IsOn;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
        {
            // Without the service, the startup task needs NetHop to run as administrator.
            UserActionRunner.Notify(Loc.Get("Settings_StartupFailed"), ex.Message);
            _loading = true;
            StartupSwitch.IsOn = ShellIntegration.StartsWithWindows;
            _loading = false;
        }
    }
}
