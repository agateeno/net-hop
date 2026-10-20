using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetHop.App.Services;

namespace NetHop.App.Forms;

/// <summary>Small builders for the form editors, so pages read as a description of the form.</summary>
internal static class FormControls
{
    public static SettingsCard Card(string header, object? content, string? description = null)
    {
        var card = new SettingsCard { Header = header, Content = content };
        if (description is not null)
            card.Description = description;
        return card;
    }

    public static TextBox TextBox(string? text, string placeholder, Action<string> changed, double width = 280)
    {
        var box = new TextBox { Text = text ?? "", PlaceholderText = placeholder, Width = width };
        box.TextChanged += (_, _) => changed(box.Text);
        return box;
    }

    /// <summary>A ComboBox over enum values; <paramref name="labelPrefix"/> + value name is the resource key of each label.</summary>
    public static ComboBox EnumCombo<T>(T selected, string labelPrefix, Action<T> changed, params T[] values) where T : struct, Enum
    {
        if (values.Length == 0)
            values = Enum.GetValues<T>();

        var combo = new ComboBox { MinWidth = 200 };
        foreach (var value in values)
            combo.Items.Add(Loc.Get(labelPrefix + value));
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(values, selected));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
                changed(values[combo.SelectedIndex]);
        };
        return combo;
    }

    /// <summary>Editable list of choices: the user can pick one or type a different value.</summary>
    public static ComboBox EditableCombo(string? text, IEnumerable<string> choices, string placeholder, Action<string> changed)
    {
        var combo = new ComboBox { IsEditable = true, MinWidth = 240, PlaceholderText = placeholder };
        var list = choices.ToList();
        foreach (var choice in list)
            combo.Items.Add(choice);

        // An editable ComboBox drops a Text set before it is loaded: select the matching choice instead, and set a
        // free text (a profile that no longer exists, say) once loaded. Done before subscribing, so nothing is "changed".
        var current = text ?? "";
        if (list.FirstOrDefault(c => string.Equals(c, current, StringComparison.CurrentCultureIgnoreCase)) is { } match)
            combo.SelectedItem = match;
        else if (current.Length > 0)
            combo.Loaded += (_, _) => combo.Text = current;

        void Changed(string value)
        {
            // Losing focus with an unchanged (or not yet shown) text must not overwrite the value.
            if (value == current)
                return;
            current = value;
            changed(value);
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string choice)
                Changed(choice);
        };
        combo.TextSubmitted += (_, e) => Changed(e.Text);
        combo.LostFocus += (_, _) => Changed(combo.Text);
        return combo;
    }

    public static NumberBox NumberBox(double value, double minimum, double maximum, Action<int> changed)
    {
        var box = new NumberBox
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            MinWidth = 120,
        };
        box.ValueChanged += (_, e) =>
        {
            if (!double.IsNaN(e.NewValue))
                changed((int)e.NewValue);
        };
        return box;
    }

    public static Button IconButton(string glyph, string tooltipKey, Action click)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 } };
        ToolTipService.SetToolTip(button, Loc.Get(tooltipKey));
        button.Click += (_, _) => click();
        return button;
    }

    public static Button DeleteButton(Action click) => IconButton("", "Form_Remove", click);

    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    /// <summary>Seven toggle buttons for days of the week, in the user's culture and first-day order.</summary>
    public static StackPanel DaysPicker(List<DayOfWeek> days, Action changed)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (var i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)culture.FirstDayOfWeek + i) % 7);
            var toggle = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
            {
                Content = culture.AbbreviatedDayNames[(int)day],
                IsChecked = days.Contains(day),
                MinWidth = 44,
            };
            toggle.Click += (_, _) =>
            {
                days.Remove(day);
                if (toggle.IsChecked == true)
                    days.Add(day);
                days.Sort();
                changed();
            };
            row.Children.Add(toggle);
        }
        return row;
    }
}

/// <summary>Edits a list of strings (addresses, gateways, DNS servers): one text box per item plus "Add".</summary>
internal sealed class StringListEditor : StackPanel
{
    private readonly List<string> _items;
    private readonly string _placeholder;
    private readonly Action _changed;

    public StringListEditor(List<string> items, string placeholder, Action changed)
    {
        _items = items;
        _placeholder = placeholder;
        _changed = changed;
        Spacing = 4;
        HorizontalAlignment = HorizontalAlignment.Right;
        Render();
    }

    private void Render()
    {
        Children.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var index = i;
            var box = FormControls.TextBox(_items[i], _placeholder, text =>
            {
                _items[index] = text.Trim();
                _changed();
            }, width: 220);
            Children.Add(FormControls.Row(box, FormControls.DeleteButton(() =>
            {
                _items.RemoveAt(index);
                Render();
                _changed();
            })));
        }

        var add = new HyperlinkButton { Content = Loc.Get("Form_Add"), HorizontalAlignment = HorizontalAlignment.Right };
        add.Click += (_, _) =>
        {
            _items.Add("");
            Render();
            // Focus the new, empty box so the user can type right away.
            if (Children.Count >= 2 && Children[^2] is StackPanel { Children: [TextBox newBox, ..] })
                newBox.Focus(FocusState.Programmatic);
        };
        Children.Add(add);
    }
}
