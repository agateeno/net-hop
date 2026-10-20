using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetHop.App.Services;
using NetHop.Core.Profiles;
using NetHop.Core.Rules;
using static NetHop.App.Forms.FormControls;

namespace NetHop.App.Forms;

/// <summary>
/// Form view of the automation rules: for each rule a trigger ("When"), optional conditions ("Only if")
/// and actions ("Then"). Edits change the <see cref="RuleSet"/> directly.
/// </summary>
internal sealed class RuleForm
{
    private static readonly TriggerType[] Triggers = Enum.GetValues<TriggerType>().Where(t => t != TriggerType.None).ToArray();
    private static readonly ConditionType[] Conditions = Enum.GetValues<ConditionType>().Where(t => t != ConditionType.None).ToArray();
    private static readonly RuleActionType[] Actions = Enum.GetValues<RuleActionType>().Where(t => t != RuleActionType.None).ToArray();

    // Condition and action cards hold a type, its fields and a remove button: below this width they go under the header.
    private const double RowWrapWidth = 760;

    private readonly RuleSet _rules;
    private readonly IReadOnlyList<string> _profileNames;
    private readonly IReadOnlyList<string> _adapterNames;
    private readonly StackPanel _root = new() { Spacing = 4 };
    private readonly HashSet<Guid> _expanded = [];

    public RuleForm(RuleSet rules, IReadOnlyList<string> profileNames, IReadOnlyList<string> adapterNames)
    {
        _rules = rules;
        _profileNames = profileNames;
        _adapterNames = adapterNames;
        Render();
    }

    public event EventHandler? Changed;

    public UIElement View => _root;

    public void AddRule()
    {
        var rule = new Rule
        {
            Name = Loc.Get("RuleForm_NewRule"),
            Trigger = new Trigger { Type = TriggerType.WifiConnected },
            Actions = [new RuleAction { Type = RuleActionType.ApplyProfile, Profile = _profileNames.FirstOrDefault() }],
        };
        _rules.Rules.Add(rule);
        _expanded.Add(rule.Id);
        Rebuild();
    }

    private void Render()
    {
        _root.Children.Clear();
        if (_rules.Rules.Count == 0)
        {
            _root.Children.Add(new TextBlock
            {
                Text = Loc.Get("RuleForm_NoRules"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }
        foreach (var rule in _rules.Rules)
            _root.Children.Add(RuleExpander(rule));
    }

    private SettingsExpander RuleExpander(Rule rule)
    {
        var enabled = new ToggleSwitch { IsOn = rule.Enabled, OnContent = Loc.Get("RuleForm_On"), OffContent = Loc.Get("RuleForm_Off") };
        enabled.Toggled += (_, _) => Edit(() => rule.Enabled = enabled.IsOn);

        var expander = new SettingsExpander
        {
            Header = string.IsNullOrWhiteSpace(rule.Name) ? Loc.Get("RuleForm_Unnamed") : rule.Name,
            Description = Summary(rule),
            HeaderIcon = new FontIcon { Glyph = "" },
            Content = Row(enabled, DeleteButton(() => { _rules.Rules.Remove(rule); Rebuild(); })),
            IsExpanded = _expanded.Contains(rule.Id),
        };
        expander.RegisterPropertyChangedCallback(SettingsExpander.IsExpandedProperty, (_, _) =>
        {
            if (expander.IsExpanded)
                _expanded.Add(rule.Id);
            else
                _expanded.Remove(rule.Id);
        });

        expander.Items.Add(Card(Loc.Get("RuleForm_Name"), TextBox(rule.Name, Loc.Get("RuleForm_NamePlaceholder"), text => Edit(() => rule.Name = text.Trim()))));
        expander.Items.Add(Card(Loc.Get("RuleForm_Priority"), NumberBox(rule.Priority, -100, 100, value => Edit(() => rule.Priority = value)),
            Loc.Get("RuleForm_PriorityHelp")));
        expander.Items.Add(Card(Loc.Get("RuleForm_Cooldown"), NumberBox(rule.CooldownSeconds, 0, 86400, value => Edit(() => rule.CooldownSeconds = value)),
            Loc.Get("RuleForm_CooldownHelp")));

        AddTrigger(expander, rule.Trigger);

        foreach (var condition in rule.Conditions)
            expander.Items.Add(ConditionCard(rule, condition));
        expander.Items.Add(AddLink("RuleForm_AddCondition", () => rule.Conditions.Add(new Condition { Type = ConditionType.TimeBetween, From = "08:00", To = "18:00" })));

        foreach (var action in rule.Actions)
            expander.Items.Add(ActionCard(rule, action));
        expander.Items.Add(AddLink("RuleForm_AddAction", () => rule.Actions.Add(new RuleAction { Type = RuleActionType.Notify, Message = rule.Name })));

        return expander;
    }

    private void AddTrigger(SettingsExpander expander, Trigger trigger)
    {
        expander.Items.Add(Card(Loc.Get("RuleForm_When"), EnumCombo(trigger.Type, "Trigger_", type =>
        {
            trigger.Type = type;
            trigger.Ssid = null;
            trigger.Adapter = null;
            trigger.At = type == TriggerType.Schedule ? "08:00" : null;
            trigger.Days.Clear();
            Rebuild();
        }, Triggers)));

        switch (trigger.Type)
        {
            case TriggerType.WifiConnected or TriggerType.WifiDisconnected:
                expander.Items.Add(Card(Loc.Get("RuleForm_Ssid"),
                    TextBox(trigger.Ssid, Loc.Get("RuleForm_SsidPlaceholder"), text => Edit(() => trigger.Ssid = NullIfEmpty(text))),
                    Loc.Get("RuleForm_SsidHelp")));
                break;
            case TriggerType.AdapterConnected or TriggerType.AdapterDisconnected:
                expander.Items.Add(Card(Loc.Get("RuleForm_Adapter"),
                    EditableCombo(trigger.Adapter?.Name, _adapterNames, Loc.Get("RuleForm_AnyAdapter"),
                        text => Edit(() => trigger.Adapter = NullIfEmpty(text) is { } name ? new AdapterMatch { Name = name } : null))));
                break;
            case TriggerType.Schedule:
                expander.Items.Add(Card(Loc.Get("RuleForm_At"), TextBox(trigger.At, "08:30", text => Edit(() => trigger.At = text.Trim()), width: 100)));
                expander.Items.Add(Card(Loc.Get("RuleForm_Days"), DaysPicker(trigger.Days, RaiseChanged), Loc.Get("RuleForm_DaysHelp")));
                break;
        }
    }

    private SettingsCard ConditionCard(Rule rule, Condition condition)
    {
        var type = EnumCombo(condition.Type, "Condition_", value =>
        {
            condition.Type = value;
            condition.From = value == ConditionType.TimeBetween ? "08:00" : null;
            condition.To = value == ConditionType.TimeBetween ? "18:00" : null;
            condition.Days.Clear();
            condition.Adapter = null;
            condition.Ssid = null;
            condition.Profile = null;
            Rebuild();
        }, Conditions);

        UIElement? fields = condition.Type switch
        {
            ConditionType.TimeBetween => Row(
                TextBox(condition.From, "08:00", text => Edit(() => condition.From = text.Trim()), width: 90),
                new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center },
                TextBox(condition.To, "18:00", text => Edit(() => condition.To = text.Trim()), width: 90)),
            ConditionType.DaysOfWeek => DaysPicker(condition.Days, RaiseChanged),
            ConditionType.AdapterConnected or ConditionType.AdapterDisconnected =>
                EditableCombo(condition.Adapter?.Name, _adapterNames, Loc.Get("RuleForm_Adapter"),
                    text => Edit(() => condition.Adapter = NullIfEmpty(text) is { } name ? new AdapterMatch { Name = name } : null)),
            ConditionType.WifiSsid => TextBox(condition.Ssid, Loc.Get("RuleForm_SsidPlaceholder"), text => Edit(() => condition.Ssid = NullIfEmpty(text)), width: 200),
            ConditionType.ActiveProfile or ConditionType.NotActiveProfile =>
                EditableCombo(condition.Profile, _profileNames, Loc.Get("RuleForm_Profile"), text => Edit(() => condition.Profile = NullIfEmpty(text))),
            _ => null,
        };

        var remove = DeleteButton(() => { rule.Conditions.Remove(condition); Rebuild(); });
        return Card(Loc.Get("RuleForm_OnlyIf"), fields is null ? Row(type, remove) : WrapRow(type, fields, remove), wrapBelow: fields is null ? null : RowWrapWidth);
    }

    private SettingsCard ActionCard(Rule rule, RuleAction action)
    {
        var type = EnumCombo(action.Type, "Action_", value =>
        {
            action.Type = value;
            action.Profile = value == RuleActionType.ApplyProfile ? _profileNames.FirstOrDefault() : null;
            action.Adapter = null;
            action.Message = value == RuleActionType.Notify ? rule.Name : null;
            action.Command = action.Arguments = action.Url = null;
            action.Seconds = value == RuleActionType.Delay ? 5 : 0;
            Rebuild();
        }, Actions);

        UIElement? fields = action.Type switch
        {
            RuleActionType.ApplyProfile =>
                EditableCombo(action.Profile, _profileNames, Loc.Get("RuleForm_Profile"), text => Edit(() => action.Profile = NullIfEmpty(text))),
            RuleActionType.EnableAdapter or RuleActionType.DisableAdapter =>
                EditableCombo(action.Adapter?.Name, _adapterNames, Loc.Get("RuleForm_Adapter"),
                    text => Edit(() => action.Adapter = NullIfEmpty(text) is { } name ? new AdapterMatch { Name = name } : null)),
            RuleActionType.Notify => TextBox(action.Message, Loc.Get("RuleForm_Message"), text => Edit(() => action.Message = text), width: 260),
            RuleActionType.RunCommand => WrapRow(
                TextBox(action.Command, Loc.Get("RuleForm_Command"), text => Edit(() => action.Command = NullIfEmpty(text)), width: 200),
                TextBox(action.Arguments, Loc.Get("RuleForm_Arguments"), text => Edit(() => action.Arguments = NullIfEmpty(text)), width: 160)),
            RuleActionType.OpenUrl => TextBox(action.Url, "https://", text => Edit(() => action.Url = NullIfEmpty(text)), width: 320),
            RuleActionType.RunPowerAutomateFlow => TextBox(action.Url, "ms-powerautomate:/console/flow/run?workflowId=...", text => Edit(() => action.Url = NullIfEmpty(text)), width: 320),
            RuleActionType.Delay => NumberBox(action.Seconds, 1, 600, value => Edit(() => action.Seconds = value)),
            _ => null,
        };

        var remove = DeleteButton(() => { rule.Actions.Remove(action); Rebuild(); });
        return Card(Loc.Get("RuleForm_Then"), fields is null ? Row(type, remove) : WrapRow(type, fields, remove),
            action.Type == RuleActionType.RunCommand ? Loc.Get("RuleForm_RunCommandHelp") : null,
            wrapBelow: fields is null ? null : RowWrapWidth);
    }

    private SettingsCard AddLink(string key, Action add)
    {
        var link = new HyperlinkButton { Content = Loc.Get(key) };
        link.Click += (_, _) => { add(); Rebuild(); };
        return Card("", link);
    }

    /// <summary>One line describing the rule, shown under its name.</summary>
    private static string Summary(Rule rule)
    {
        var trigger = Loc.Get("Trigger_" + rule.Trigger.Type);
        var detail = rule.Trigger.Ssid ?? rule.Trigger.Adapter?.Name ?? rule.Trigger.At;
        if (detail is not null)
            trigger += $" ({detail})";
        var actions = string.Join(", ", rule.Actions.Select(a => Loc.Get("Action_" + a.Type) + (a.Profile is { } p ? $" {p}" : "")));
        return Loc.Format("RuleForm_Summary", trigger, actions.Length > 0 ? actions : "-");
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

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
