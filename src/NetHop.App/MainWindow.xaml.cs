using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NetHop.App.Pages;
using NetHop.App.Services;

namespace NetHop.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Dashboard"] = typeof(DashboardPage),
        ["Profiles"] = typeof(ProfilesPage),
        ["Adapters"] = typeof(AdaptersPage),
        ["Automations"] = typeof(AutomationsPage),
        ["History"] = typeof(HistoryPage),
        ["Integrations"] = typeof(IntegrationsPage),
    };

    // Pages lay out down to this width (DIPs); narrower would only clip them.
    private const int MinWidthDip = 500;
    private const int MinHeightDip = 480;
    private const double MinimalPaneButtonHeight = 40;

    private readonly ServiceConnection _connection;

    public MainWindow(ServiceConnection connection)
    {
        _connection = connection;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppTheme.Track(this);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "NetHop.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // The presenter works in physical pixels.
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            presenter.PreferredMinimumWidth = (int)(MinWidthDip * scale);
            presenter.PreferredMinimumHeight = (int)(MinHeightDip * scale);
        }

        _connection.PropertyChanged += OnConnectionChanged;
        UpdateConnectionInfo();
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    public void ShowAndFocus()
    {
        AppWindow.Show();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    /// <summary>A .nethop file was opened from Explorer: show it in the profile editor.</summary>
    public void ImportProfileFile(string path)
    {
        if (ContentFrame.Content is ProfilesPage page)
            page.Import(path);
        else
            ContentFrame.Navigate(typeof(ProfilesPage), new ProfilesPage.ImportRequest(path));
        // Navigate first: the selection handler then sees the page is already shown.
        Nav.SelectedItem = Nav.MenuItems[1];
    }

    public void ShowSettings() => Nav.SelectedItem = Nav.SettingsItem;

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var type) ? type : null;

        if (page is not null && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }

    // In minimal mode the menu button floats over the top-left corner of the content, where the page title is.
    private void OnNavigationDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) =>
        ContentRoot.Margin = new Thickness(0, args.DisplayMode == NavigationViewDisplayMode.Minimal ? MinimalPaneButtonHeight : 0, 0, 0);

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ServiceConnection.State) or nameof(ServiceConnection.IsReadOnly))
            UpdateConnectionInfo();
    }

    private void UpdateConnectionInfo()
    {
        (ConnectionInfo.Severity, ConnectionInfo.Title, ConnectionInfo.Message) = _connection.State switch
        {
            ConnectionState.ServiceNotRunning => (InfoBarSeverity.Error, Loc.Get("Main_ServiceStoppedTitle"), Loc.Get("Main_ServiceStoppedMessage")),
            ConnectionState.AccessDenied => (InfoBarSeverity.Error, Loc.Get("Main_AccessDeniedTitle"), Loc.Get("Main_AccessDeniedMessage")),
            _ => (InfoBarSeverity.Informational, Loc.Get("Common_ConnectingService"), ""),
        };
        ConnectionInfo.IsOpen = _connection.State != ConnectionState.Connected;
        ReadOnlyInfo.IsOpen = _connection.IsConnected && _connection.IsReadOnly;
    }

    private void OnRestartAsAdminClick(object sender, RoutedEventArgs e) => App.Current?.RestartAsAdministrator();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);
}
