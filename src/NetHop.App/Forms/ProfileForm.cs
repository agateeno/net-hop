using System.Globalization;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetHop.App.Services;
using NetHop.Core;
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
    private readonly Grid _adapterGrid = new() { ColumnSpacing = 4 };
    private readonly List<SettingsExpander> _adapterViews = [];
    private readonly HashSet<int> _expanded = [];
    private int _adapterColumns = 1;

    public ProfileForm(Profile profile, IReadOnlyList<AdapterInfo> adapters)
    {
        _profile = profile;
        _adapters = adapters;
        Render();
    }

    /// <summary>Raised after every edit.</summary>
    public event EventHandler? Changed;

    public UIElement View => _root;

    /// <summary>Adapters side by side in wide windows; changing it moves the existing cards, so edits and expanded state stay.</summary>
    public int AdapterColumns
    {
        get => _adapterColumns;
        set
        {
            if (value == _adapterColumns)
                return;
            _adapterColumns = value;
            LayoutAdapters();
        }
    }

    /// <summary>Drops empty IP blocks so the YAML stays minimal (and free of "changes nothing" warnings).</summary>
    public static void Tidy(Profile profile)
    {
        foreach (var adapter in profile.Adapters)
        {
            if (adapter.Ipv4 is { Mode: IpMode.Unchanged, Dns: null or { Mode: DnsMode.Unchanged }, Metric: null })
                adapter.Ipv4 = null;
            if (adapter.Ipv6 is { Mode: IpMode.Unchanged, Dns: null or { Mode: DnsMode.Unchanged }, Metric: null })
                adapter.Ipv6 = null;
        }
        if (profile.Hosts is { Mode: HostsMode.Unchanged, Entries.Count: 0 })
            profile.Hosts = null;
    }

    private void Render()
    {
        _root.Children.Clear();

        _root.Children.Add(Card(Loc.Get("ProfileForm_Name"), TextBox(_profile.Name, Loc.Get("ProfileForm_NamePlaceholder"), text => Edit(() => _profile.Name = text.Trim()))));
        _root.Children.Add(Card(Loc.Get("ProfileForm_Description"), TextBox(_profile.Description, Loc.Get("ProfileForm_Optional"),
            text => Edit(() => _profile.Description = string.IsNullOrWhiteSpace(text) ? null : text.Trim()))));
        _root.Children.Add(Card(Loc.Get("ProfileForm_Appearance"), WrapRow(IconPicker(), ColorPicker()), wrapBelow: 600));

        var header = new Grid { Margin = new Thickness(1, 24, 0, 6) };
        header.Children.Add(new TextBlock { Text = Loc.Get("ProfileForm_Adapters"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        var add = new DropDownButton { Content = Loc.Get("ProfileForm_AddAdapter"), HorizontalAlignment = HorizontalAlignment.Right, Flyout = AddAdapterMenu() };
        header.Children.Add(add);
        _root.Children.Add(header);

        _adapterViews.Clear();
        for (var i = 0; i < _profile.Adapters.Count; i++)
            _adapterViews.Add(AdapterExpander(i));
        LayoutAdapters();
        _root.Children.Add(_adapterGrid);

        if (_profile.Adapters.Count == 0)
            _root.Children.Add(new TextBlock { Text = Loc.Get("ProfileForm_NoAdapters"), Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });

        AddHosts();
    }

    private void AddHosts()
    {
        _root.Children.Add(new TextBlock
        {
            Text = Loc.Get("ProfileForm_Hosts"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(1, 24, 0, 6),
        });

        var hosts = _profile.Hosts ?? new HostsConfig();
        _root.Children.Add(Card(Loc.Get("ProfileForm_HostsMode"), EnumCombo(hosts.Mode, "Hosts_", mode =>
        {
            hosts.Mode = mode;
            if (mode != HostsMode.Set)
                hosts.Entries.Clear();
            else if (hosts.Entries.Count == 0)
                hosts.Entries.Add(new HostsEntry());
            _profile.Hosts = mode == HostsMode.Unchanged ? null : hosts;
            Rebuild();
        }), Loc.Get("ProfileForm_HostsHelp"), wrapBelow: 600));

        if (hosts.Mode != HostsMode.Set)
            return;

        for (var i = 0; i < hosts.Entries.Count; i++)
            _root.Children.Add(HostsEntryCard(hosts, i));

        var add = new Button { Content = Loc.Get("ProfileForm_HostsAdd"), Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) =>
        {
            hosts.Entries.Add(new HostsEntry());
            Rebuild();
        };
        _root.Children.Add(add);
    }

    /// <summary>One entry: address, names and, right below them, what is wrong with it (protected domain, bad name...).</summary>
    private SettingsCard HostsEntryCard(HostsConfig hosts, int index)
    {
        var entry = hosts.Entries[index];
        var errors = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        void ShowErrors()
        {
            // A new, empty row is not an error yet.
            IReadOnlyList<Issue> problems = string.IsNullOrEmpty(entry.Ip) && entry.Names.Count == 0 ? [] : ProfileValidator.ValidateHostsEntry(entry);
            errors.Text = string.Join(Environment.NewLine, problems.Select(p => p.Message));
            errors.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        var ip = TextBox(entry.Ip, Loc.Get("ProfileForm_HostsIp"), text => Edit(() =>
        {
            entry.Ip = text.Trim();
            ShowErrors();
        }), width: 180);
        var names = TextBox(string.Join(" ", entry.Names), Loc.Get("ProfileForm_HostsNames"), text => Edit(() =>
        {
            entry.Names = text.Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            ShowErrors();
        }), width: 320);
        var remove = DeleteButton(() =>
        {
            hosts.Entries.RemoveAt(index);
            Rebuild();
        });
        ShowErrors();

        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(WrapRow(ip, names, remove));
        content.Children.Add(errors);
        return Card(Loc.Format("ProfileForm_HostsEntry", index + 1), content, wrapBelow: 700);
    }

    /// <summary>
    /// One stack per column, filled in turn (1st, 3rd... left; 2nd, 4th... right): expanding a card pushes down only
    /// its own column instead of leaving a gap next to it.
    /// </summary>
    private void LayoutAdapters()
    {
        foreach (var column in _adapterGrid.Children.OfType<StackPanel>())
            column.Children.Clear();
        _adapterGrid.Children.Clear();
        _adapterGrid.ColumnDefinitions.Clear();

        var columns = Math.Max(1, Math.Min(_adapterColumns, _adapterViews.Count));
        for (var c = 0; c < columns; c++)
        {
            _adapterGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var column = new StackPanel { Spacing = 4 };
            Grid.SetColumn(column, c);
            _adapterGrid.Children.Add(column);
        }
        for (var i = 0; i < _adapterViews.Count; i++)
            ((StackPanel)_adapterGrid.Children[i % columns]).Children.Add(_adapterViews[i]);
    }

    private ComboBox IconPicker()
    {
        var combo = new ComboBox { MinWidth = 180 };
        foreach (var (glyph, key) in Icons)
            combo.Items.Add(Row(new FontIcon { Glyph = glyph, FontSize = 16 }, new TextBlock { Text = Loc.Get(key) }));

        // The profile's own icon when it is not a preset: a custom image, or a glyph typed in the YAML.
        var preset = Array.FindIndex(Icons, i => i.Glyph == _profile.Icon);
        var custom = preset < 0 && !string.IsNullOrEmpty(_profile.Icon);
        if (custom)
            combo.Items.Add(Row(IconPreview(_profile.Icon!), new TextBlock { Text = Loc.Get("Icon_Custom") }));
        var chooseIndex = combo.Items.Count;
        combo.Items.Add(Row(new FontIcon { Glyph = "\uE8B9", FontSize = 16 }, new TextBlock { Text = Loc.Get("Icon_ChooseImage") }));

        combo.SelectedIndex = preset >= 0 ? preset : custom ? Icons.Length : 0;
        var current = combo.SelectedIndex;
        combo.SelectionChanged += async (_, _) =>
        {
            if (combo.SelectedIndex == chooseIndex)
            {
                var image = await IconImage.PickAsync();
                if (image is null)
                {
                    combo.SelectedIndex = current; // cancelled: back to the previous choice
                    return;
                }
                _profile.Icon = image;
                Rebuild(); // shows the image as the "Custom" entry
                return;
            }
            current = combo.SelectedIndex;
            if (current >= 0 && current < Icons.Length)
                Edit(() => _profile.Icon = Icons[current].Glyph);
        };
        return combo;
    }

    private static FrameworkElement IconPreview(string icon) =>
        XamlHelpers.ProfileImage(icon) is { } image
            ? new Image { Source = image, Width = 16, Height = 16 }
            : new FontIcon { Glyph = XamlHelpers.GlyphOrDefault(icon), FontSize = 16 };

    private ComboBox ColorPicker()
    {
        var combo = new ComboBox { MinWidth = 180 };
        foreach (var (color, key) in Colors)
            combo.Items.Add(Row(Swatch(color), new TextBlock { Text = Loc.Get(key) }));

        // A color that is not a preset (picked with "Custom color..." or typed in the YAML).
        var preset = Array.FindIndex(Colors, c => string.Equals(c.Color, _profile.Color, StringComparison.OrdinalIgnoreCase));
        var custom = preset < 0 && _profile.Color is not null;
        if (custom)
            combo.Items.Add(Row(Swatch(_profile.Color), new TextBlock { Text = Loc.Format("Color_CustomValue", _profile.Color!.ToUpperInvariant()) }));
        var chooseIndex = combo.Items.Count;
        combo.Items.Add(Row(new FontIcon { Glyph = "\uE790", FontSize = 14 }, new TextBlock { Text = Loc.Get("Color_Choose") }));

        combo.SelectedIndex = preset >= 0 ? preset : custom ? Colors.Length : 0;
        var current = combo.SelectedIndex;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex == chooseIndex)
            {
                ShowColorFlyout(combo, () => combo.SelectedIndex = current);
                return;
            }
            current = combo.SelectedIndex;
            if (current >= 0 && current < Colors.Length)
                Edit(() => _profile.Color = Colors[current].Color);
        };
        return combo;
    }

    private void ShowColorFlyout(FrameworkElement anchor, Action cancelled)
    {
        var start = XamlHelpers.TryParseColor(_profile.Color, out var color) ? color : Microsoft.UI.ColorHelper.FromArgb(255, 0x00, 0x78, 0xD4);
        var picker = new Microsoft.UI.Xaml.Controls.ColorPicker
        {
            Color = start,
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            IsHexInputVisible = true,
            ColorSpectrumShape = ColorSpectrumShape.Ring,
        };
        var ok = new Button
        {
            Content = Loc.Get("Color_Use"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(picker);
        panel.Children.Add(ok);

        var flyout = new Flyout { Content = panel };
        var applied = false;
        ok.Click += (_, _) =>
        {
            applied = true;
            flyout.Hide();
            _profile.Color = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
            Rebuild(); // shows the new color as the "Custom" entry
        };
        flyout.Closed += (_, _) =>
        {
            if (!applied)
                cancelled();
        };
        flyout.ShowAt(anchor);
    }

    private static Border Swatch(string? color) => new()
    {
        Width = 14,
        Height = 14,
        CornerRadius = new CornerRadius(7),
        Background = color is null ? null : XamlHelpers.ProfileBrush(color),
        BorderThickness = new Thickness(1),
        BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
    };

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

        // A metric typed wrong in the YAML shows as "manual": the validator reports it.
        var metricValid = IpConfig.TryParseMetric(current.Metric, out var metric);
        var metricChoice = current.Metric is null ? MetricChoice.Unchanged
            : metricValid && metric is null ? MetricChoice.Automatic
            : MetricChoice.Manual;
        var metricCombo = EnumCombo(metricChoice, "Metric_", choice =>
        {
            current.Metric = choice switch
            {
                MetricChoice.Automatic => IpConfig.AutomaticMetric,
                MetricChoice.Manual => DefaultManualMetric.ToString(CultureInfo.InvariantCulture),
                _ => null,
            };
            set(current);
            Rebuild();
        });
        UIElement metricContent = metricChoice != MetricChoice.Manual
            ? metricCombo
            : WrapRow(metricCombo, NumberBox(metric ?? DefaultManualMetric, 1, IpConfig.MaxMetric,
                value => Edit(() => current.Metric = value.ToString(CultureInfo.InvariantCulture))));
        expander.Items.Add(Card(Loc.Format("ProfileForm_Metric", family), metricContent, Loc.Get("ProfileForm_MetricHelp"), wrapBelow: 600));
    }

    private const int DefaultManualMetric = 10;

    private enum MetricChoice
    {
        Unchanged,
        Automatic,
        Manual,
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
}
