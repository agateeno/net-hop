using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetHop.App.Services;

namespace NetHop.App.Pages;

/// <summary>Rules editor (YAML for now; a visual rule builder comes next) and the global on/off switch.</summary>
public sealed partial class AutomationsPage : Page
{
    private const string ExampleRule = """
          - name: Arrive at the office
            priority: 10
            cooldownSeconds: 60
            trigger:
              type: wifiConnected
              ssid: CORP-WiFi
            conditions:
              - type: timeBetween
                from: "07:00"
                to: "20:00"
            actions:
              - type: applyProfile
                profile: Office
              - type: notify
                message: Office profile applied
        """;

    private readonly ServiceConnection _connection = App.Current!.Connection;
    private bool _updating;

    public AutomationsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        UpdateSwitch();
        _connection.PropertyChanged += OnConnectionChanged;
        if (_connection.IsConnected)
            Editor.Text = await _connection.Service.GetRulesYamlAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) =>
        _connection.PropertyChanged -= OnConnectionChanged;

    private void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateSwitch();

    private void UpdateSwitch()
    {
        _updating = true;
        RunSwitch.IsEnabled = SaveButton.IsEnabled = _connection.IsConnected;
        RunSwitch.IsOn = _connection.Status?.AutomationsPausedUntil is null;
        RunSwitch.OffContent = _connection.Status?.AutomationsPausedUntil is { } until ? $"Paused until {until.LocalDateTime:t}" : "Off";
        _updating = false;
    }

    private async void OnRunToggled(object sender, RoutedEventArgs e)
    {
        if (!_updating && _connection.IsConnected)
            await _connection.Service.PauseAutomationsAsync(RunSwitch.IsOn ? 0 : 60);
    }

    private void OnExampleClick(object sender, RoutedEventArgs e)
    {
        var text = Editor.Text.TrimEnd();
        if (!text.Contains("rules:"))
            text = "schema: 1\nrules:";
        else if (text.EndsWith("rules: []"))
            text = text[..^" []".Length];
        Editor.Text = text + "\n" + ExampleRule;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var result = await _connection.Service.SaveRulesYamlAsync(Editor.Text);
        Result.Severity = result.Saved ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        Result.Title = result.Saved ? "Rules saved" : "Rules not saved";
        Result.Message = string.Join(Environment.NewLine, result.Issues.Select(i => i.ToString()));
        Result.IsOpen = true;
    }
}
