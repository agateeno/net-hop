using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetHop.App.Forms;
using NetHop.App.Services;
using NetHop.Core;
using NetHop.Core.Networking;
using NetHop.Core.Profiles;
using NetHop.Core.Serialization;
using NetHop.Ipc;
using Microsoft.Windows.Storage.Pickers;

namespace NetHop.App.Pages;

/// <summary>
/// Profile list with two synchronized editors: a form (default) and the raw YAML.
/// Validation runs locally on every edit; saving goes through the service, which validates again.
/// </summary>
public sealed partial class ProfilesPage : Page
{
    public sealed record ImportRequest(string Path);

    private readonly ServiceConnection _connection = App.Current!.Connection;
    private IReadOnlyList<AdapterInfo> _adapters = [];
    private Profile? _profile;
    private Guid? _editingId;
    private bool _yamlView;
    private bool _loading;

    public ProfilesPage()
    {
        InitializeComponent();
        UpdateView();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _connection.PropertyChanged += OnConnectionChanged;
        ProfileList.ItemsSource = _connection.Profiles;
        await LoadAdaptersAsync();
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
            Show(await File.ReadAllTextAsync(path), id: null);
            ShowResult(InfoBarSeverity.Informational, Loc.Format("Profiles_Imported", Path.GetFileName(path)), Loc.Get("Profiles_ImportedHelp"));
        }
        catch (IOException ex)
        {
            ShowResult(InfoBarSeverity.Error, Loc.Get("Profiles_CannotRead"), ex.Message);
        }
    }

    private async Task LoadAdaptersAsync()
    {
        if (!_connection.IsConnected)
            return;
        try
        {
            _adapters = (await _connection.Service.GetNetworkAsync()).Adapters.Select(a => a.Info).ToList();
        }
        catch (Exception ex) when (ex is StreamJsonRpc.RemoteInvocationException or StreamJsonRpc.ConnectionLostException)
        {
            _adapters = [];
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
        Show(await _connection.Service.GetProfileYamlAsync(profile.Id), profile.Id);
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        ProfileList.SelectedItem = null;
        Show(ProfileSerializer.Serialize(new Profile { Name = Loc.Get("Profiles_NewName"), Icon = ProfileForm.Icons[0].Glyph }), id: null);
    }

    private async void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        if (!_connection.IsConnected)
            return;
        var network = await _connection.Service.GetNetworkAsync();
        var adapters = network.Adapters.Where(a => !a.Info.IsVirtual).Select(a => a.Info.Id).ToArray();
        ProfileList.SelectedItem = null;
        Show(await _connection.Service.CaptureProfileYamlAsync(Loc.Get("Profiles_CapturedName"), adapters), id: null);
    }

    private void OnViewChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var toYaml = sender.SelectedItem == YamlViewItem;
        if (toYaml == _yamlView || _loading)
            return;

        if (toYaml)
        {
            SetEditorText(CurrentYaml());
            _yamlView = true;
        }
        else
        {
            // Leaving YAML: the text must parse, otherwise stay so the user can fix it.
            if (!TryParse(Editor.Text, out var parsed))
            {
                _loading = true;
                ViewSelector.SelectedItem = YamlViewItem;
                _loading = false;
                return;
            }
            _profile = parsed;
            _yamlView = false;
            BuildForm();
        }
        UpdateView();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var result = await _connection.Service.SaveProfileYamlAsync(CurrentYaml());
        if (result.Saved)
        {
            _editingId = result.ProfileId;
            ShowResult(InfoBarSeverity.Success, Loc.Get("Profiles_Saved"), Describe(result.Issues));
        }
        else
            ShowResult(InfoBarSeverity.Error, Loc.Get("Profiles_NotSaved"), Describe(result.Issues));
        UpdateView();
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
        var yaml = CurrentYaml();
        // The Windows App SDK picker also works when NetHop runs as administrator, unlike Windows.Storage.Pickers.
        var picker = new FileSavePicker(App.Current!.MainWindowForDialogs.AppWindow.Id) { SuggestedFileName = _profile?.Name ?? "profile" };
        picker.FileTypeChoices.Add(Loc.Get("Profiles_FileType"), [NetHopConstants.ProfileExtension]);

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
            await File.WriteAllTextAsync(file.Path, yaml);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_editingId is not { } id)
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Format("Profiles_DeleteTitle", _profile?.Name),
            Content = Loc.Get("Profiles_DeleteMessage"),
            PrimaryButtonText = Loc.Get("Profiles_DeleteConfirm"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await _connection.Service.DeleteProfileAsync(id);
        _profile = null;
        _editingId = null;
        SetEditorText("");
        FormHost.Content = null;
        Result.IsOpen = false;
        UpdateView();
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;
        UpdateView();
        if (TryParse(Editor.Text, out var parsed))
            Validate(parsed);
    }

    /// <summary>Shows a profile in whichever view is active; YAML that does not parse opens in the YAML view.</summary>
    private void Show(string yaml, Guid? id)
    {
        _editingId = id;
        Result.IsOpen = false;
        SetEditorText(yaml);

        if (TryParse(yaml, out var parsed))
        {
            _profile = parsed;
            BuildForm();
            Validate(parsed);
        }
        else
        {
            _profile = null;
            _yamlView = true;
            _loading = true;
            ViewSelector.SelectedItem = YamlViewItem;
            _loading = false;
        }
        UpdateView();
    }

    private void BuildForm()
    {
        if (_profile is null)
            return;
        var form = new ProfileForm(_profile, _adapters);
        form.Changed += (_, _) =>
        {
            Validate(_profile);
            UpdateView();
        };
        FormHost.Content = form.View;
    }

    private string CurrentYaml()
    {
        if (_yamlView || _profile is null)
            return Editor.Text;
        ProfileForm.Tidy(_profile);
        return ProfileSerializer.Serialize(_profile);
    }

    private bool TryParse(string yaml, out Profile profile)
    {
        try
        {
            profile = ProfileSerializer.Deserialize(yaml);
            return true;
        }
        catch (NetHopFormatException ex)
        {
            profile = null!;
            ShowResult(InfoBarSeverity.Error, Loc.Get("Profiles_YamlError"), ex.Message);
            return false;
        }
    }

    private void Validate(Profile profile)
    {
        var issues = ProfileValidator.Validate(profile);
        if (issues.Count == 0)
            Result.IsOpen = false;
        else
            ShowResult(issues.HasErrors() ? InfoBarSeverity.Error : InfoBarSeverity.Warning, Loc.Get("Profiles_Check"), Describe(issues));
    }

    private void SetEditorText(string text)
    {
        _loading = true;
        Editor.Text = text;
        _loading = false;
    }

    private void UpdateView()
    {
        var hasContent = _profile is not null || Editor.Text.Length > 0;
        FormScroller.Visibility = !_yamlView && _profile is not null ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = _yamlView && hasContent ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
        ViewSelector.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;

        var connected = _connection.IsConnected;
        var canEdit = _connection.CanEdit;
        SaveButton.IsEnabled = canEdit && hasContent;
        ApplyButton.IsEnabled = DeleteButton.IsEnabled = canEdit && _editingId is not null;
        DryRunButton.IsEnabled = connected && _editingId is not null; // a preview changes nothing: allowed read-only
        ExportButton.IsEnabled = hasContent;
    }

    private void Report(ApplyResult result)
    {
        var steps = result.Steps.Length == 0 ? Loc.Get("Common_NothingToChange") : string.Join(Environment.NewLine, result.Steps.Select(s => "• " + s));
        if (result.DryRun)
            ShowResult(InfoBarSeverity.Informational, Loc.Get("Profiles_PreviewTitle"), steps + Describe(result.Issues, leadingNewLine: true));
        else if (result.Success)
            ShowResult(InfoBarSeverity.Success, Loc.Format("Common_ProfileApplied", result.ProfileName), steps);
        else
            ShowResult(InfoBarSeverity.Error, Loc.Get("Profiles_ApplyFailed"), result.Error + (result.RolledBack ? " " + Loc.Get("Common_RolledBack") : ""));
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
}
