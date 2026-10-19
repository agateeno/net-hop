using System.ComponentModel;
using System.Runtime.InteropServices;
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
        ["Integrations"] = typeof(IntegrationsPage),
    };

    private readonly ServiceConnection _connection;

    public MainWindow(ServiceConnection connection)
    {
        _connection = connection;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "NetHop.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));

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

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var type) ? type : null;

        if (page is not null && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServiceConnection.State))
            UpdateConnectionInfo();
    }

    private void UpdateConnectionInfo()
    {
        (ConnectionInfo.Severity, ConnectionInfo.Title, ConnectionInfo.Message) = _connection.State switch
        {
            ConnectionState.ServiceNotRunning => (InfoBarSeverity.Error, "The NetHop service is not running",
                "Profiles cannot be applied. Start the service from an administrator terminal: sc start NetHop"),
            ConnectionState.AccessDenied => (InfoBarSeverity.Error, "Administrator account required",
                "Only members of the Administrators group can use NetHop."),
            _ => (InfoBarSeverity.Informational, "Connecting to the NetHop service...", ""),
        };
        ConnectionInfo.IsOpen = _connection.State != ConnectionState.Connected;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);
}
