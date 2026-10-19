using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetHop.App.Services;
using NetHop.Core;
using NetHop.Core.Profiles;
using NetHop.Core.Serialization;
using NetHop.Ipc;
using Windows.Storage.Pickers;

namespace NetHop.App.Pages;

/// <summary>
/// Profile list and YAML editor. Validation runs locally as you type; saving goes through the service,
/// which validates again before writing.
/// </summary>
/// <remarks>The form-based editor (SettingsExpander per adapter) will sit on top of the same YAML model.</remarks>
public sealed partial class ProfilesPage : Page
{
    public sealed record ImportRequest(string Path);

    private const string NewProfileTemplate = """
        schema: 1
        name: New profile
        adapters:
          - match:
              name: Ethernet
            state: enabled
            ipv4:
              mode: static
              addresses:
                - 192.168.1.10/24
              gateways:
                - 192.168.1.1
              dns:
                mode: static
                servers: [192.168.1.1, 1.1.1.1]
            ipv6:
              mode: dhcp
        """;

    private readonly ServiceConnection _connection = App.Current!.Connection;
    private Guid? _editingId;
    private bool _loading;

    public ProfilesPage()
    {
        InitializeComponent();
        UpdateButtons();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _connection.PropertyChanged += OnConnectionChanged;
        ProfileList.ItemsSource = _connection.Profiles;
        if (e.Parameter is ImportRequest import)
            Import(import.Path);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) =>
        _connection.PropertyChanged -= OnConnectionChanged;

    /// <summary>Loads a .nethop file into the editor; nothing is saved until the user clicks Save.</summary>
    public async void Import(string path)
    {
        try
        {
            ShowEditor(await File.ReadAllTextAsync(path), id: null);
            ShowResult(InfoBarSeverity.Informational, $"Imported {Path.GetFileName(path)}", "Review the profile, then click Save to add it.");
        }
        catch (IOException ex)
        {
            ShowResult(InfoBarSeverity.Error, "Cannot read the file", ex.Message);
        }
    }

    private void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ServiceConnection.Profiles))
            return;
        var selected = _editingId;
        ProfileList.ItemsSource = _connection.Profiles;
        ProfileList.SelectedItem = _connection.Profiles.FirstOrDefault(p => p.Id == selected);
    }

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ProfileSummary profile || profile.Id == _editingId || !_connection.IsConnected)
            return;
        ShowEditor(await _connection.Service.GetProfileYamlAsync(profile.Id), profile.Id);
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        ProfileList.SelectedItem = null;
        ShowEditor(NewProfileTemplate, id: null);
    }

    private async void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        if (!_connection.IsConnected)
            return;
        var network = await _connection.Service.GetNetworkAsync();
        var adapters = network.Adapters.Where(a => !a.Info.IsVirtual).Select(a => a.Info.Id).ToArray();
        ProfileList.SelectedItem = null;
        ShowEditor(await _connection.Service.CaptureProfileYamlAsync("Current configuration", adapters), id: null);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var result = await _connection.Service.SaveProfileYamlAsync(Editor.Text);
        if (result.Saved)
        {
            _editingId = result.ProfileId;
            ShowResult(InfoBarSeverity.Success, "Profile saved", Describe(result.Issues));
        }
        else
            ShowResult(InfoBarSeverity.Error, "Not saved", Describe(result.Issues));
        UpdateButtons();
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (_editingId is { } id)
            Report(await _connection.Service.ApplyProfileAsync(id.ToString()));
    }

    private async void OnDryRunClick(object sender, RoutedEventArgs e)
    {
        if (_editingId is { } id)
            Report(await _connection.Service.ApplyProfileAsync(id.ToString(), dryRun: true));
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = TryGetName() ?? "profile" };
        picker.FileTypeChoices.Add("NetHop profile", [NetHopConstants.ProfileExtension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Current!.MainWindowForDialogs));

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
            await File.WriteAllTextAsync(file.Path, Editor.Text);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_editingId is not { } id)
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete '{TryGetName()}'?",
            Content = "The profile file is removed. This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await _connection.Service.DeleteProfileAsync(id);
        ShowEditor("", id: null);
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;
        UpdateButtons();

        // Live validation, no service round-trip.
        try
        {
            var issues = ProfileValidator.Validate(ProfileSerializer.Deserialize(Editor.Text));
            if (issues.Count == 0)
                Result.IsOpen = false;
            else
                ShowResult(issues.HasErrors() ? InfoBarSeverity.Error : InfoBarSeverity.Warning, "Check the profile", Describe(issues));
        }
        catch (NetHopFormatException ex)
        {
            ShowResult(InfoBarSeverity.Error, "YAML error", ex.Message);
        }
    }

    private void ShowEditor(string yaml, Guid? id)
    {
        _loading = true;
        _editingId = id;
        Editor.Text = yaml;
        _loading = false;
        Result.IsOpen = false;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var connected = _connection.IsConnected;
        SaveButton.IsEnabled = connected && Editor.Text.Length > 0;
        ApplyButton.IsEnabled = DryRunButton.IsEnabled = DeleteButton.IsEnabled = connected && _editingId is not null;
        ExportButton.IsEnabled = Editor.Text.Length > 0;
    }

    private void Report(ApplyResult result)
    {
        var steps = result.Steps.Length == 0 ? "Nothing to change." : string.Join(Environment.NewLine, result.Steps.Select(s => "• " + s));
        if (result.DryRun)
            ShowResult(InfoBarSeverity.Informational, "Preview", steps + Describe(result.Issues, leadingNewLine: true));
        else if (result.Success)
            ShowResult(InfoBarSeverity.Success, $"'{result.ProfileName}' applied", steps);
        else
            ShowResult(InfoBarSeverity.Error, "Apply failed", result.Error + (result.RolledBack ? " The previous configuration was restored." : ""));
    }

    private void ShowResult(InfoBarSeverity severity, string title, string message)
    {
        Result.Severity = severity;
        Result.Title = title;
        Result.Message = message;
        Result.IsOpen = true;
    }

    private static string Describe(IEnumerable<Issue> issues, bool leadingNewLine = false)
    {
        var text = string.Join(Environment.NewLine, issues.Select(i => i.ToString()));
        return leadingNewLine && text.Length > 0 ? Environment.NewLine + text : text;
    }

    private string? TryGetName()
    {
        try
        {
            return ProfileSerializer.Deserialize(Editor.Text).Name;
        }
        catch (NetHopFormatException)
        {
            return null;
        }
    }
}
