using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetHop.App.Forms;
using NetHop.App.Services;
using NetHop.Core;
using NetHop.Core.Rules;
using NetHop.Core.Serialization;

namespace NetHop.App.Pages;

/// <summary>Automation rules with a form editor and a YAML editor, plus the global on/off switch.</summary>
public sealed partial class AutomationsPage : Page
{
    private readonly ServiceConnection _connection = App.Current!.Connection;
    private RuleSet? _rules;
    private RuleForm? _form;
    private bool _yamlView;
    private bool _updating;

    public AutomationsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        UpdateSwitch();
        _connection.PropertyChanged += OnConnectionChanged;
        if (!_connection.IsConnected)
            return;

        var yaml = await _connection.Service.GetRulesYamlAsync();
        SetEditorText(yaml);
        if (TryParse(yaml, out var rules))
        {
            _rules = rules;
            await BuildFormAsync();
        }
        else
            SwitchToYaml();
        UpdateView();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) =>
        _connection.PropertyChanged -= OnConnectionChanged;

    private void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateSwitch();

    private async Task BuildFormAsync()
    {
        if (_rules is null)
            return;

        var profiles = _connection.Profiles.Select(p => p.Name).ToList();
        var adapters = new List<string>();
        try
        {
            adapters = (await _connection.Service.GetNetworkAsync()).Adapters.Select(a => a.Info.Name).ToList();
        }
        catch (Exception ex) when (ex is StreamJsonRpc.RemoteInvocationException or StreamJsonRpc.ConnectionLostException)
        {
        }

        _form = new RuleForm(_rules, profiles, adapters);
        _form.Changed += (_, _) => Validate(_rules);
        FormHost.Content = _form.View;
        Validate(_rules);
    }

    private void UpdateSwitch()
    {
        _updating = true;
        RunSwitch.IsEnabled = SaveButton.IsEnabled = _connection.CanEdit;
        RunSwitch.IsOn = _connection.Status?.AutomationsPausedUntil is null;
        RunSwitch.OnContent = Loc.Get("RuleForm_On"); // without it, WinUI shows "On" in Windows' language
        RunSwitch.OffContent = _connection.Status?.AutomationsPausedUntil is { } until
            ? Loc.Format("Common_PausedUntil", until.LocalDateTime)
            : Loc.Get("RuleForm_Off");
        _updating = false;
    }

    private async void OnRunToggled(object sender, RoutedEventArgs e)
    {
        if (!_updating && _connection.CanEdit)
            await _connection.Service.PauseAutomationsAsync(RunSwitch.IsOn ? 0 : 60);
    }

    private async void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        // Rules are added in the form: switch to it first (the YAML must parse), then add.
        if (_yamlView)
        {
            if (!TryParse(Editor.Text, out var rules))
                return;
            _rules = rules;
            _yamlView = false;
            _updating = true;
            ViewSelector.SelectedItem = FormViewItem;
            _updating = false;
            await BuildFormAsync();
            UpdateView();
        }
        if (_rules is null)
        {
            _rules = new RuleSet();
            await BuildFormAsync();
        }
        _form?.AddRule();
    }

    private async void OnViewChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var toYaml = sender.SelectedItem == YamlViewItem;
        if (toYaml == _yamlView || _updating)
            return;

        if (toYaml)
        {
            SetEditorText(CurrentYaml());
            _yamlView = true;
        }
        else if (TryParse(Editor.Text, out var rules))
        {
            _rules = rules;
            _yamlView = false;
            await BuildFormAsync();
        }
        else
        {
            SwitchToYaml();
            return;
        }
        UpdateView();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var result = await _connection.Service.SaveRulesYamlAsync(CurrentYaml());
        Result.Severity = result.Saved ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        Result.Title = Loc.Get(result.Saved ? "Automations_Saved" : "Automations_NotSaved");
        Result.Message = string.Join(Environment.NewLine, result.Issues.Select(i => i.ToString()));
        Result.IsOpen = true;
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updating && TryParse(Editor.Text, out var rules))
            Validate(rules);
    }

    private string CurrentYaml() =>
        _yamlView || _rules is null ? Editor.Text : RuleSetSerializer.Serialize(_rules);

    private bool TryParse(string yaml, out RuleSet rules)
    {
        try
        {
            rules = RuleSetSerializer.Deserialize(yaml);
            return true;
        }
        catch (NetHopFormatException ex)
        {
            rules = null!;
            ShowResult(InfoBarSeverity.Error, Loc.Get("Profiles_YamlError"), ex.Message);
            return false;
        }
    }

    private void Validate(RuleSet rules)
    {
        var issues = RuleSetSerializer.Validate(rules);
        if (issues.Count == 0)
            Result.IsOpen = false;
        else
            ShowResult(issues.HasErrors() ? InfoBarSeverity.Error : InfoBarSeverity.Warning, Loc.Get("Automations_Check"),
                string.Join(Environment.NewLine, issues.Select(i => i.ToString())));
    }

    private void SwitchToYaml()
    {
        _yamlView = true;
        _updating = true;
        ViewSelector.SelectedItem = YamlViewItem;
        _updating = false;
        UpdateView();
    }

    private void SetEditorText(string text)
    {
        _updating = true;
        Editor.Text = text;
        _updating = false;
    }

    private void UpdateView()
    {
        FormScroller.Visibility = _yamlView ? Visibility.Collapsed : Visibility.Visible;
        Editor.Visibility = _yamlView ? Visibility.Visible : Visibility.Collapsed;
        AddRuleButton.IsEnabled = _connection.CanEdit;
    }

    private void ShowResult(InfoBarSeverity severity, string title, string message)
    {
        Result.Severity = severity;
        Result.Title = title;
        Result.Message = message;
        Result.IsOpen = true;
    }
}
