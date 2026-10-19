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
    }

    private static readonly (string Tag, string Label)[] Languages =
    [
        (AppLanguage.SystemDefault, "Settings_LanguageSystem"),
        ("en-US", "English"),
        ("it-IT", "Italiano"),
    ];

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        StartupSwitch.IsOn = ShellIntegration.StartsWithWindows;
        // Language names are shown in their own language, as Windows does.
        LanguageBox.ItemsSource = Languages.Select(l => l.Tag == AppLanguage.SystemDefault ? Loc.Get(l.Label) : l.Label).ToList();
        LanguageBox.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.Tag == AppLanguage.Saved));
        _loading = false;

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
            ServiceCard.Description = Loc.Format("Settings_ServiceDetails", info.Version, info.ProfilesFolder);
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedIndex < 0)
            return;
        AppLanguage.Saved = Languages[LanguageBox.SelectedIndex].Tag;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(LanguageBox, Loc.Get("Settings_LanguageRestart"));
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => ProfileFolder.Open();

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            ShellIntegration.StartsWithWindows = StartupSwitch.IsOn;
    }
}
