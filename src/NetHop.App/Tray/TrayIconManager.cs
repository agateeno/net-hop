using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using NetHop.App.Services;

namespace NetHop.App.Tray;

/// <summary>
/// The notification-area icon: left click opens the Fluent flyout, right click a short menu.
/// The tooltip always shows the active profile.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly ServiceConnection _connection;
    private readonly TaskbarIcon _icon;
    private TrayFlyoutWindow? _flyout;

    public TrayIconManager(ServiceConnection connection, Action openSettings, Action exit)
    {
        _connection = connection;

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Open NetHop", Command = new RelayCommand(openSettings) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Apply next profile", Command = new AsyncRelayCommand(() => _connection.ApplyNextAsync()) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Exit", Command = new RelayCommand(exit) });

        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "NetHop.ico"))),
            ToolTipText = "NetHop",
            NoLeftClickDelay = true,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            LeftClickCommand = new RelayCommand(() => ToggleFlyout(openSettings)),
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);

        _connection.PropertyChanged += OnConnectionChanged;
    }

    private void ToggleFlyout(Action openSettings)
    {
        _flyout ??= new TrayFlyoutWindow(_connection, openSettings);
        _flyout.Toggle();
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ServiceConnection.Status) or nameof(ServiceConnection.State)))
            return;

        _icon.ToolTipText = _connection.State switch
        {
            ConnectionState.Connected => $"NetHop - {_connection.Status?.ActiveProfileName ?? "no active profile"}",
            ConnectionState.ServiceNotRunning => "NetHop - service not running",
            ConnectionState.AccessDenied => "NetHop - administrator account required",
            _ => "NetHop - connecting...",
        };
    }

    public void Dispose()
    {
        _connection.PropertyChanged -= OnConnectionChanged;
        _flyout?.Close();
        _icon.Dispose();
    }
}
