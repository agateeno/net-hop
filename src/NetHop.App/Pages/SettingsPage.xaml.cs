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
        Unloaded += (_, _) => UpdateCheck.Changed -= OnUpdateCheckChanged;
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
        foreach (var toggle in new[] { UpdateAutoSwitch, PrereleaseSwitch })
            (toggle.OnContent, toggle.OffContent) = (Loc.Get("RuleForm_On"), Loc.Get("RuleForm_Off"));
        // Language names are shown in their own language, as Windows does.
        LanguageBox.ItemsSource = Languages.Select(l => l.Tag == AppLanguage.SystemDefault ? Loc.Get(l.Label) : l.Label).ToList();
        LanguageBox.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.Tag == AppLanguage.Saved));
        ThemeBox.ItemsSource = Themes.Select(t => Loc.Get(t.Label)).ToList();
        ThemeBox.SelectedIndex = Math.Max(0, Array.FindIndex(Themes, t => t.Theme == AppTheme.Saved));
        _loading = false;
        ShowUpdateState();

        ServiceCard.Description = _connection.State switch
        {
            ConnectionState.Connected => Loc.Get("Settings_ServiceRunning"),
            ConnectionState.ServiceNotRunning => Loc.Get("Settings_ServiceStopped"),
            ConnectionState.AccessDenied => Loc.Get("Settings_ServiceNotAdmin"),
            _ => Loc.Get("Common_Connecting"),
        };

        if (_connection.IsConnected)
        {
            var info = await _connection.Service.GetInfoAsync();
            ServiceCard.Description = Loc.Format("Settings_ServiceDetails", info.Version, info.ProfilesFolder)
                + (info.ReadOnly ? " · " + Loc.Get("Settings_ServiceReadOnly") : "");
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

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            ShellIntegration.StartsWithWindows = StartupSwitch.IsOn;
    }
}
