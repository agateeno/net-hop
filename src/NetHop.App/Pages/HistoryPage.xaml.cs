using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetHop.App.Services;
using NetHop.Ipc;

namespace NetHop.App.Pages;

/// <summary>The last profile changes kept by the service: when, which profile, who, and what changed.</summary>
public sealed partial class HistoryPage : Page
{
    private readonly ServiceConnection _connection = App.Current!.Connection;

    public HistoryPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _connection.StatusChanged += OnStatusChanged;
        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) =>
        _connection.StatusChanged -= OnStatusChanged;

    private async void OnStatusChanged(object? sender, StatusChangedEventArgs e)
    {
        if (e.Kind == StatusChangeKind.ProfileApplied)
            await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (!_connection.IsConnected)
            return;
        try
        {
            Render(await _connection.Service.GetHistoryAsync());
            ErrorInfo.IsOpen = false;
        }
        catch (Exception ex) when (ex is StreamJsonRpc.RemoteInvocationException or StreamJsonRpc.ConnectionLostException)
        {
            ErrorInfo.Title = Loc.Get("History_CannotLoad");
            ErrorInfo.Message = ex.Message;
            ErrorInfo.IsOpen = true;
        }
    }

    private void Render(HistoryEntry[] entries)
    {
        EntryList.Children.Clear();
        EmptyText.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in entries)
            EntryList.Children.Add(CreateExpander(entry));
    }

    private static SettingsExpander CreateExpander(HistoryEntry entry)
    {
        var who = entry.RuleName is null ? Loc.Get("History_ByHand") : Loc.Format("History_ByRule", entry.RuleName);
        var outcome = entry switch
        {
            { Success: true, Steps.Length: 0 } => Loc.Get("Common_NothingToChange"),
            { Success: true } => Loc.Format("History_Changes", entry.Steps.Length),
            { RolledBack: true } => Loc.Get("History_FailedRolledBack"),
            _ => Loc.Get("History_Failed"),
        };

        var expander = new SettingsExpander
        {
            Header = entry.ProfileName,
            Description = $"{entry.At.LocalDateTime:g}  ·  {who}",
            HeaderIcon = new FontIcon { Glyph = entry.Success ? "" : "" },
            Content = new TextBlock { Text = outcome, VerticalAlignment = VerticalAlignment.Center },
        };

        if (entry.Error is not null)
            expander.Items.Add(Row(Loc.Get("History_Error"), entry.Error));
        if (entry.Steps.Length > 0)
            expander.Items.Add(Row(Loc.Get("History_Steps"), string.Join(Environment.NewLine, entry.Steps.Select(s => "• " + s))));
        return expander;
    }

    // Long text under its header, not beside it: steps and errors are sentences, not short values.
    private static SettingsCard Row(string header, string text)
    {
        var card = new SettingsCard
        {
            Header = header,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
        };
        card.Resources["SettingsCardWrapThreshold"] = 100_000d;
        return card;
    }
}
