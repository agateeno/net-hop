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
        AboutCard.Description = $"Version {Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}";
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        StartupSwitch.IsOn = ShellIntegration.StartsWithWindows;
        _loading = false;

        ServiceCard.Description = _connection.State switch
        {
            ConnectionState.Connected => "Running",
            ConnectionState.ServiceNotRunning => "Not running",
            ConnectionState.AccessDenied => "Running, but your account is not an administrator",
            _ => "Connecting...",
        };

        if (_connection.IsConnected)
        {
            var info = await _connection.Service.GetInfoAsync();
            ServiceCard.Description = $"Running · version {info.Version} · profiles in {info.ProfilesFolder}";
        }
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            ShellIntegration.StartsWithWindows = StartupSwitch.IsOn;
    }
}
