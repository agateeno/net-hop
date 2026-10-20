using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetHop.App.Services;
using NetHop.Core.Networking;
using NetHop.Ipc;

namespace NetHop.App.Pages;

/// <summary>One SettingsExpander per adapter: live state, addresses, enable/disable and rename.</summary>
public sealed partial class AdaptersPage : Page
{
    private readonly ServiceConnection _connection = App.Current!.Connection;
    private NetworkSnapshot? _network;

    // Below this page width the title and its controls no longer fit on one line.
    private const double WideLayoutMinWidth = 640;

    public AdaptersPage()
    {
        InitializeComponent();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        VisualStateManager.GoToState(this, e.NewSize.Width >= WideLayoutMinWidth ? "WideLayout" : "NarrowLayout", false);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _connection.StatusChanged += OnStatusChanged;
        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) =>
        _connection.StatusChanged -= OnStatusChanged;

    private async void OnStatusChanged(object? sender, StatusChangedEventArgs e)
    {
        if (e.Kind is StatusChangeKind.NetworkChanged or StatusChangeKind.ProfileApplied)
            await LoadAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAsync();

    private void OnShowVirtualToggled(object sender, RoutedEventArgs e) => Render();

    private async Task LoadAsync()
    {
        if (!_connection.IsConnected)
            return;
        try
        {
            _network = await _connection.Service.GetNetworkAsync();
            Render();
        }
        catch (Exception ex) when (ex is StreamJsonRpc.RemoteInvocationException or StreamJsonRpc.ConnectionLostException)
        {
            ShowError(ex.Message);
        }
    }

    private void Render()
    {
        AdapterList.Children.Clear();
        if (_network is null)
            return;

        var adapters = _network.Adapters
            .Where(a => ShowVirtual.IsOn || !a.Info.IsVirtual)
            .OrderByDescending(a => a.Connected)
            .ThenBy(a => a.Info.Name, StringComparer.CurrentCultureIgnoreCase);

        foreach (var adapter in adapters)
            AdapterList.Children.Add(CreateExpander(adapter));
    }

    private SettingsExpander CreateExpander(AdapterSnapshot adapter)
    {
        var info = adapter.Info;
        var toggle = new ToggleSwitch { IsOn = adapter.Enabled, IsEnabled = _connection.CanEdit, OnContent = Loc.Get("Adapters_Enabled"), OffContent = Loc.Get("Adapters_Disabled") };
        toggle.Toggled += async (_, _) => await RunAsync(() => _connection.Service.SetAdapterEnabledAsync(info.Id, toggle.IsOn));

        var expander = new SettingsExpander
        {
            Header = info.Name,
            Description = $"{StateText(adapter)}  ·  {info.Description}",
            HeaderIcon = new FontIcon { Glyph = GlyphFor(info.Kind) },
            Content = toggle,
        };

        AddRow(expander, Loc.Get("Adapters_Mac"), string.IsNullOrEmpty(info.Mac) ? "-" : info.Mac);
        AddRow(expander, Loc.Get("Adapters_Id"), info.Id.ToString("B"));
        if (adapter.Ssid is not null)
            AddRow(expander, Loc.Get("Adapters_WifiNetwork"), adapter.Ssid);
        AddFamilyRows(expander, "IPv4", adapter.Ipv4);
        AddFamilyRows(expander, "IPv6", adapter.Ipv6);

        var rename = new Button { Content = Loc.Get("Adapters_Rename"), IsEnabled = _connection.CanEdit };
        rename.Click += async (_, _) => await RenameAsync(info);
        expander.Items.Add(new SettingsCard
        {
            Header = Loc.Get("Adapters_NameHeader"),
            Description = Loc.Get("Adapters_NameDescription"),
            Content = rename,
        });

        return expander;
    }

    private static void AddFamilyRows(SettingsExpander expander, string label, FamilySnapshot? family)
    {
        if (family is null)
            return;
        var addresses = family.AllAddresses.Count == 0 ? Loc.Get("Adapters_NoAddress") : string.Join(Environment.NewLine, family.AllAddresses);
        AddRow(expander, Loc.Format(family.Dhcp ? "Adapters_FamilyAutomatic" : "Adapters_FamilyStatic", label), addresses);
        if (family.ManualGateways.Count > 0)
            AddRow(expander, Loc.Format("Adapters_Gateway", label), string.Join(Environment.NewLine, family.ManualGateways));
        if (family.EffectiveDnsServers.Count > 0)
            AddRow(expander, Loc.Format(family.StaticDnsServers.Count > 0 ? "Adapters_DnsStatic" : "Adapters_Dns", label), string.Join(Environment.NewLine, family.EffectiveDnsServers));
        AddRow(expander, Loc.Format("Adapters_Metric", label),
            family.ManualMetric is null ? Loc.Format("Adapters_MetricAutomatic", family.InterfaceMetric) : family.InterfaceMetric.ToString());
    }

    private static void AddRow(SettingsExpander expander, string header, string value) =>
        expander.Items.Add(new SettingsCard
        {
            Header = header,
            Content = new TextBlock { Text = value, IsTextSelectionEnabled = true, TextAlignment = TextAlignment.Right },
        });

    private async Task RenameAsync(AdapterInfo info)
    {
        var box = new TextBox { Text = info.Name, SelectionStart = 0, SelectionLength = info.Name.Length };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("Adapters_RenameTitle"),
            Content = box,
            PrimaryButtonText = Loc.Get("Adapters_Rename"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && box.Text.Trim() is { Length: > 0 } name && name != info.Name)
            await RunAsync(() => _connection.Service.RenameAdapterAsync(info.Id, name));
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            await LoadAsync();
        }
        catch (Exception ex) when (ex is StreamJsonRpc.RemoteInvocationException or StreamJsonRpc.ConnectionLostException or InvalidOperationException)
        {
            ShowError(ex.Message);
            await LoadAsync();
        }
    }

    private void ShowError(string message)
    {
        ErrorInfo.Title = Loc.Get("Common_OperationFailed");
        ErrorInfo.Message = message;
        ErrorInfo.IsOpen = true;
    }

    private static string StateText(AdapterSnapshot a) => Loc.Get(!a.Enabled ? "Adapters_StateDisabled" : a.Connected ? "Adapters_StateConnected" : "Adapters_StateDisconnected");

    private static string GlyphFor(AdapterKind kind) => kind switch
    {
        AdapterKind.WiFi => "",
        AdapterKind.Ethernet => "",
        AdapterKind.Cellular => "",
        AdapterKind.Bluetooth => "",
        AdapterKind.Vpn => "",
        _ => "",
    };
}
