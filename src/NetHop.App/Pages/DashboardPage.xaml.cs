using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetHop.App.Services;
using NetHop.Ipc;

namespace NetHop.App.Pages;

public sealed partial class DashboardPage : Page
{
    private readonly ServiceConnection _connection = App.Current!.Connection;
    private bool _updating;

    public DashboardPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _connection.PropertyChanged += OnConnectionChanged;
        _connection.ProfileApplied += OnProfileApplied;
        Update();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _connection.PropertyChanged -= OnConnectionChanged;
        _connection.ProfileApplied -= OnProfileApplied;
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        _updating = true;
        var status = _connection.Status;

        ProfileGrid.ItemsSource = _connection.Profiles;
        NoProfilesText.Visibility = _connection.IsConnected && _connection.Profiles.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        ActiveCard.Header = status?.ActiveProfileName ?? "No active profile";
        ActiveCard.Description = status switch
        {
            null => "Not connected to the service",
            { ActiveProfileDrifted: true } => "The network was changed by hand after the last profile was applied",
            { LastAppliedAt: { } at } => $"Last change {at.LocalDateTime:g}",
            _ => "The current network does not match any profile",
        };
        NextButton.IsEnabled = _connection.IsConnected && _connection.Profiles.Length > 0;

        AutomationsSwitch.IsEnabled = _connection.IsConnected;
        AutomationsSwitch.IsOn = status?.AutomationsPausedUntil is null;
        AutomationsCard.Description = status?.AutomationsPausedUntil is { } until ? $"Paused until {until.LocalDateTime:t}" : "Rules run when the network changes";

        SsidWarning.IsOpen = status?.SsidUnavailable == true;
        _updating = false;
    }

    private void OnProfileApplied(object? sender, ApplyResult result)
    {
        LastResult.Severity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        LastResult.Title = result.Success ? $"'{result.ProfileName}' applied" : $"'{result.ProfileName}' failed";
        LastResult.Message = result.Success
            ? (result.Steps.Length == 0 ? "Nothing needed to change." : $"{result.Steps.Length} changes in {result.Duration.TotalSeconds:0.0}s")
            : result.Error + (result.RolledBack ? " The previous configuration was restored." : "");
        LastResult.IsOpen = true;
    }

    private async void OnProfileClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ProfileSummary profile)
            await _connection.ApplyAsync(profile.Id.ToString());
    }

    private async void OnNextClick(object sender, RoutedEventArgs e) => await _connection.ApplyNextAsync();

    private async void OnAutomationsToggled(object sender, RoutedEventArgs e)
    {
        if (!_updating && _connection.IsConnected)
            await _connection.Service.PauseAutomationsAsync(AutomationsSwitch.IsOn ? 0 : 60);
    }
}
