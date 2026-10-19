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

        ActiveCard.Header = status?.ActiveProfileName ?? Loc.Get("Dashboard_NoActiveProfile");
        ActiveCard.Description = status switch
        {
            null => Loc.Get("Dashboard_NotConnected"),
            { ActiveProfileDrifted: true } => Loc.Get("Dashboard_Drifted"),
            { LastAppliedAt: { } at } => Loc.Format("Dashboard_LastChange", at.LocalDateTime),
            _ => Loc.Get("Dashboard_NoMatch"),
        };
        ProfileGrid.IsItemClickEnabled = _connection.CanEdit;
        NextButton.IsEnabled = _connection.CanEdit && _connection.Profiles.Length > 0;

        AutomationsSwitch.IsEnabled = _connection.CanEdit;
        AutomationsSwitch.IsOn = status?.AutomationsPausedUntil is null;
        AutomationsCard.Description = status?.AutomationsPausedUntil is { } until ? Loc.Format("Common_PausedUntil", until.LocalDateTime) : Loc.Get("Dashboard_RulesRun");

        SsidWarning.IsOpen = status?.SsidUnavailable == true;
        _updating = false;
    }

    private void OnProfileApplied(object? sender, ApplyResult result)
    {
        LastResult.Severity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        LastResult.Title = Loc.Format(result.Success ? "Common_ProfileApplied" : "Common_ProfileFailed", result.ProfileName);
        LastResult.Message = result.Success
            ? (result.Steps.Length == 0 ? Loc.Get("Common_NothingToChange") : Loc.Format("Dashboard_ChangesIn", result.Steps.Length, result.Duration.TotalSeconds))
            : result.Error + (result.RolledBack ? " " + Loc.Get("Common_RolledBack") : "");
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
        if (!_updating && _connection.CanEdit)
            await _connection.Service.PauseAutomationsAsync(AutomationsSwitch.IsOn ? 0 : 60);
    }
}
