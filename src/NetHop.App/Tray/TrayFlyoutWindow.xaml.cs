using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NetHop.App.Services;
using NetHop.Ipc;
using Windows.Graphics;

namespace NetHop.App.Tray;

/// <summary>
/// Borderless acrylic window anchored above the notification area, in the spirit of PowerToys Quick Access.
/// It hides as soon as it loses focus.
/// </summary>
public sealed partial class TrayFlyoutWindow : Window
{
    private const int WidthDip = 340;
    private const int HeightDip = 420;
    private const int MarginDip = 12;

    private readonly ServiceConnection _connection;
    private readonly Action _openSettings;

    public TrayFlyoutWindow(ServiceConnection connection, Action openSettings)
    {
        _connection = connection;
        _openSettings = openSettings;
        InitializeComponent();

        SystemBackdrop = new DesktopAcrylicBackdrop();
        AppTheme.Track(this);
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                AppWindow.Hide();
        };

        _connection.PropertyChanged += OnConnectionChanged;
        Refresh();
    }

    public void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
            return;
        }

        PositionNearTray();
        Refresh();
        AppWindow.Show(activateWindow: true);
        Activate();
        _ = _connection.RefreshAsync();
    }

    private void PositionNearTray()
    {
        GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;

        var width = (int)(WidthDip * scale);
        var height = (int)(HeightDip * scale);
        var margin = (int)(MarginDip * scale);
        var work = area.WorkArea;

        // Bottom-right corner of the work area, i.e. just above the taskbar clock.
        AppWindow.MoveAndResize(new RectInt32(
            work.X + work.Width - width - margin,
            work.Y + work.Height - height - margin,
            width,
            height));
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        ProfileList.ItemsSource = _connection.Profiles;
        ActiveProfileText.Text = _connection.Status?.ActiveProfileName ?? Loc.Get("Flyout_None");

        var warning = _connection.State switch
        {
            ConnectionState.ServiceNotRunning => Loc.Get("Main_ServiceStoppedTitle"),
            ConnectionState.AccessDenied => Loc.Get("Main_AccessDeniedTitle"),
            ConnectionState.NeedsElevation => Loc.Get("Main_NeedsElevationTitle"),
            ConnectionState.Connecting => Loc.Get("Common_ConnectingService"),
            _ when _connection.Status?.ActiveProfileDrifted == true => Loc.Get("Dashboard_Drifted"),
            _ => null,
        };
        StateText.Text = warning ?? "";
        StateText.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;

        PauseToggle.IsChecked = _connection.Status?.AutomationsPausedUntil is not null;
        PauseToggle.IsEnabled = ProfileList.IsEnabled = _connection.CanEdit;
        // Without the service no automations run: nothing to pause.
        PauseToggle.Visibility = _connection.IsLocal ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnProfileClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ProfileSummary profile)
            return;

        Busy.Visibility = Visibility.Visible;
        ProfileList.IsEnabled = false;
        try
        {
            await _connection.ApplyAsync(profile.Id.ToString());
        }
        finally
        {
            Busy.Visibility = Visibility.Collapsed;
            ProfileList.IsEnabled = true;
        }
    }

    private async void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (_connection.IsConnected)
            await _connection.Service.PauseAutomationsAsync(PauseToggle.IsChecked == true ? 60 : 0);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
        _openSettings();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);
}
