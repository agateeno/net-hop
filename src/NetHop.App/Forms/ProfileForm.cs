using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetHop.App.Services;
using NetHop.Core.Networking;
using NetHop.Core.Profiles;
using static NetHop.App.Forms.FormControls;

namespace NetHop.App.Forms;

/// <summary>
/// Form view of a <see cref="Profile"/>. Edits change the profile object directly; the page serializes it
/// for the YAML view and for saving. Structural changes (mode, add/remove) rebuild the form, typing does not.
/// </summary>
internal sealed class ProfileForm
{
    public static readonly (string Glyph, string Key)[] Icons =
    [
        ("", "Icon_Network"), ("", "Icon_Home"), ("", "Icon_Work"), ("", "Icon_Ethernet"),
        ("", "Icon_WiFi"), ("", "Icon_Globe"), ("", "Icon_Lock"), ("", "Icon_Travel"),
    ];

    public static readonly (string? Color, string Key)[] Colors =
    [
        (null, "Color_None"), ("#0078D4", "Color_Blue"), ("#107C10", "Color_Green"), ("#CA5010", "Color_Orange"),
        ("#D13438", "Color_Red"), ("#8764B8", "Color_Purple"), ("#038387", "Color_Teal"), ("#69797E", "Color_Gray"),
    ];

    private readonly Profile _profile;
    private readonly IReadOnlyList<AdapterInfo> _adapters;
    private readonly StackPanel _root = new() { Spacing = 4 };
    private readonly HashSet<int> _expanded = [];

    public ProfileForm(Profile profile, IReadOnlyList<AdapterInfo> adapters)
    {
        _profile = profile;
        _adapters = adapters;
        Render();
    }

    /// <summary>Raised after every edit.</summary>
    public event EventHandler? Changed;

    public UIElement View => _root;

    /// <summary>Drops empty IP blocks so the YAML stays minimal (and free of "changes nothing" warnings).</summary>
    public static void Tidy(Profile profile)
    {
        foreach (var adapter in profile.Adapters)
        {
            if (adapter.Ipv4 is { Mode: IpMode.Unchanged, Dns: null or { Mode: DnsMode.Unchanged } })
                adapter.Ipv4 = null;
            if (adapter.Ipv6 is { Mode: IpMode.Unchanged, Dns: null or { Mode: DnsMode.Unchanged } })
                adapter.Ipv6 = null;
        }
    }

    private void Render()
    {
        _root.Children.Clear();

        _root.Children.Add(Card(Loc.Get("ProfileForm_Name"), TextBox(_profile.Name, Loc.Get("ProfileForm_NamePlaceholder"), text => Edit(() => _profile.Name = text.Trim()))));
        _root.Children.Add(Card(Loc.Get("ProfileForm_Description"), TextBox(_profile.Description, Loc.Get("ProfileForm_Optional"),
            text => Edit(() => _profile.Description = string.IsNullOrWhiteSpace(text) ? null : text.Trim()))));
        _root.Children.Add(Card(Loc.Get("ProfileForm_Appearance"), Row(IconPicker(), ColorPicker())));

        var header = new Grid { Margin = new Thickness(1, 24, 0, 6) };
        header.Children.Add(new TextBlock { Text = Loc.Get("ProfileForm_Adapters"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        var add = new DropDownButton { Content = Loc.Get("ProfileForm_AddAdapter"), HorizontalAlignment = HorizontalAlignment.Right, Flyout = AddAdapterMenu() };
        header.Children.Add(add);
        _root.Children.Add(header);

        for (var i = 0; i < _profile.Adapters.Count; i++)
            _root.Children.Add(AdapterExpander(i));

        if (_profile.Adapters.Count == 0)
            _root.Children.Add(new TextBlock { Text = Loc.Get("ProfileForm_NoAdapters"), Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
    }

    private ComboBox IconPicker()
    {
        var combo = new ComboBox { MinWidth = 160 };
        foreach (var (glyph, key) in Icons)
            combo.Items.Add(Row(new FontIcon { Glyph = glyph, FontSize = 16 }, new TextBlock { Text = Loc.Get(key) }));
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(Icons, i => i.Glyph == _profile.Icon));
        combo.SelectionChanged += (_, _) => Edit(() => _profile.Icon = Icons[combo.SelectedIndex].Glyph);
        return combo;
    }

    private ComboBox ColorPicker()
    {
        var combo = new ComboBox { MinWidth = 160 };
        foreach (var (color, key) in Colors)
        {
            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = color is null ? null : new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseColor(color)),
                BorderThickness = new Thickness(1),
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            };
            combo.Items.Add(Row(swatch, new TextBlock { Text = Loc.Get(key) }));
        }
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(Colors, c => string.Equals(c.Color, _profile.Color, StringComparison.OrdinalIgnoreCase)));
        combo.SelectionChanged += (_, _) => Edit(() => _profile.Color = Colors[Math.Max(0, combo.SelectedIndex)].Color);
        return combo;
    }

    private MenuFlyout AddAdapterMenu()
    {
        var menu = new MenuFlyout();
        var used = _profile.Adapters.Select(a => AdapterMatcher.Resolve(a.Match, _adapters).Adapter?.Id).ToHashSet();
        foreach (var adapter in _adapters.Where(a => !used.Contains(a.Id)).OrderBy(a => a.IsVirtual).ThenBy(a => a.Name))
        {
            var item = new MenuFlyoutItem { Text = adapter.Name, Icon = new FontIcon { Glyph = adapter.Kind == AdapterKind.WiFi ? "" : "" } };
            item.Click += (_, _) => AddAdapter(AdapterMatcher.CreateMatch(adapter));
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0)
            menu.Items.Add(new MenuFlyoutSeparator());
        var byName = new MenuFlyoutItem { Text = Loc.Get("ProfileForm_AddByName") };
        byName.Click += (_, _) => AddAdapter(new AdapterMatch { Name = "Ethernet" });
        menu.Items.Add(byName);
        return menu;
    }

    private void AddAdapter(AdapterMatch match)
    {
        _profile.Adapters.Add(new AdapterConfig { Match = match, State = DesiredAdapterState.Enabled });
        _expanded.Add(_profile.Adapters.Count - 1);
        Rebuild();
    }

    private SettingsExpander AdapterExpander(int index)
    {
        var config = _profile.Adapters[index];
        var live = AdapterMatcher.Resolve(config.Match, _adapters);

        var state = EnumCombo(config.State, "State_", value => { config.State = value; Rebuild(); });
        var remove = DeleteButton(() =>
        {
            _profile.Adapters.RemoveAt(index);
            _expanded.Clear();
            Rebuild();
        });

        var expander = new SettingsExpander
        {
            Header = live.Adapter?.Name ?? config.Match.ToString(),
            Description = live.Found
                ? Loc.Format("ProfileForm_AdapterFound", live.Adapter!.Description)
                : Loc.Get("ProfileForm_AdapterMissing"),
            HeaderIcon = new FontIcon { Glyph = live.Adapter?.Kind == AdapterKind.WiFi ? "" : "" },
            Content = Row(state, remove),
            IsExpanded = _expanded.Contains(index),
        };
        expander.RegisterPropertyChangedCallback(SettingsExpander.IsExpandedProperty, (_, _) =>
        {
            if (expander.IsExpanded)
                _expanded.Add(index);
            else
                _expanded.Remove(index);
        });

        expander.Items.Add(Card(Loc.Get("ProfileForm_MatchName"),
            TextBox(config.Match.Name, "Ethernet", text => Edit(() => config.Match.Name = string.IsNullOrWhiteSpace(text) ? null : text.Trim())),
            Loc.Get("ProfileForm_MatchHelp")));

        if (config.State == DesiredAdapterState.Disabled)
        {
            expander.Items.Add(Card(Loc.Get("ProfileForm_DisabledNoIp"), null));
            return expander;
        }

        AddFamily(expander, "IPv4", config.Ipv4, value => config.Ipv4 = value, "192.168.1.10/24", "192.168.1.1", "1.1.1.1");
        AddFamily(expander, "IPv6", config.Ipv6, value => config.Ipv6 = value, "2001:db8::10/64", "fe80::1", "2606:4700:4700::1111");
        return expander;
    }

    private void AddFamily(SettingsExpander expander, string family, IpConfig? config, Action<IpConfig> set,
        string addressExample, string gatewayExample, string dnsExample)
    {
        var current = config ?? new IpConfig();
        expander.Items.Add(Card(family, EnumCombo(current.Mode, "Mode_", mode =>
        {
            current.Mode = mode;
            if (mode != IpMode.Static)
            {
                current.Addresses.Clear();
                current.Gateways.Clear();
            }
            set(current);
            Rebuild();
        })));

        if (current.Mode == IpMode.Static)
        {
            expander.Items.Add(Card(Loc.Format("ProfileForm_Addresses", family), new StringListEditor(current.Addresses, addressExample, RaiseChanged),
                Loc.Get("ProfileForm_AddressesHelp")));
            expander.Items.Add(Card(Loc.Format("ProfileForm_Gateways", family), new StringListEditor(current.Gateways, gatewayExample, RaiseChanged)));
        }

        // With automatic IP, omitted DNS means "automatic" too; show what will actually happen.
        var dnsMode = current.Dns?.Mode ?? (current.Mode == IpMode.Dhcp ? DnsMode.Dhcp : DnsMode.Unchanged);
        expander.Items.Add(Card(Loc.Format("ProfileForm_Dns", family), EnumCombo(dnsMode, "Dns_", mode =>
        {
            current.Dns ??= new DnsConfig();
            current.Dns.Mode = mode;
            if (mode != DnsMode.Static)
                current.Dns.Servers.Clear();
            set(current);
            Rebuild();
        })));

        if (dnsMode == DnsMode.Static)
        {
            current.Dns ??= new DnsConfig { Mode = DnsMode.Static };
            expander.Items.Add(Card(Loc.Format("ProfileForm_DnsServers", family), new StringListEditor(current.Dns.Servers, dnsExample, RaiseChanged),
                Loc.Get("ProfileForm_DnsHelp")));
        }
    }

    private void Edit(Action change)
    {
        change();
        RaiseChanged();
    }

    private void Rebuild()
    {
        Render();
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static Windows.UI.Color ParseColor(string hex)
    {
        var rgb = Convert.ToInt32(hex[1..], 16);
        return Microsoft.UI.ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
