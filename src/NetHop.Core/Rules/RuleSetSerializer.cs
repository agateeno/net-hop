using NetHop.Core.Profiles;
using NetHop.Core.Serialization;

namespace NetHop.Core.Rules;

public static class RuleSetSerializer
{
    public static string Serialize(RuleSet rules) => NetHopYaml.Serialize(rules);

    /// <exception cref="NetHopFormatException">The YAML is malformed or contains unknown keys.</exception>
    public static RuleSet Deserialize(string yaml) => NetHopYaml.Deserialize<RuleSet>(yaml);

    public static IReadOnlyList<Issue> Validate(RuleSet set)
    {
        var issues = new List<Issue>();
        if (set.Schema < 1 || set.Schema > NetHopConstants.CurrentSchemaVersion)
            issues.Add(Issue.Error("schema", Text.Format("Rules_SchemaUnsupported", set.Schema)));

        for (var i = 0; i < set.Rules.Count; i++)
        {
            var rule = set.Rules[i];
            var path = $"rules[{i}]";

            if (string.IsNullOrWhiteSpace(rule.Name))
                issues.Add(Issue.Error($"{path}.name", Text.Get("Rules_NameRequired")));
            if (rule.Trigger.Type == TriggerType.None)
                issues.Add(Issue.Error($"{path}.trigger.type", Text.Get("Rules_TriggerRequired")));
            if (rule.Trigger.Type == TriggerType.Schedule && !RuleEngine.TryParseTime(rule.Trigger.At, out _))
                issues.Add(Issue.Error($"{path}.trigger.at", Text.Get("Rules_TimeFormat")));
            if (rule.Actions.Count == 0)
                issues.Add(Issue.Error($"{path}.actions", Text.Get("Rules_NoActions")));

            for (var c = 0; c < rule.Conditions.Count; c++)
                ValidateCondition(rule.Conditions[c], $"{path}.conditions[{c}]", issues);
            for (var a = 0; a < rule.Actions.Count; a++)
                ValidateAction(rule.Actions[a], $"{path}.actions[{a}]", issues);
        }

        return issues;
    }

    private static void ValidateCondition(Condition condition, string path, List<Issue> issues)
    {
        switch (condition.Type)
        {
            case ConditionType.None:
                issues.Add(Issue.Error($"{path}.type", Text.Get("Rules_ConditionTypeMissing")));
                break;
            case ConditionType.TimeBetween when !RuleEngine.TryParseTime(condition.From, out _) || !RuleEngine.TryParseTime(condition.To, out _):
                issues.Add(Issue.Error(path, Text.Get("Rules_FromToFormat")));
                break;
            case ConditionType.AdapterConnected or ConditionType.AdapterDisconnected when condition.Adapter is null or { IsEmpty: true }:
                issues.Add(Issue.Error($"{path}.adapter", Text.Get("Rules_AdapterRequired")));
                break;
            case ConditionType.ActiveProfile or ConditionType.NotActiveProfile when string.IsNullOrWhiteSpace(condition.Profile):
                issues.Add(Issue.Error($"{path}.profile", Text.Get("Rules_ProfileNameRequired")));
                break;
            case ConditionType.WifiSsid when string.IsNullOrEmpty(condition.Ssid):
                issues.Add(Issue.Error($"{path}.ssid", Text.Get("Rules_SsidRequired")));
                break;
        }
    }

    private static void ValidateAction(RuleAction action, string path, List<Issue> issues)
    {
        switch (action.Type)
        {
            case RuleActionType.None:
                issues.Add(Issue.Error($"{path}.type", Text.Get("Rules_ActionTypeMissing")));
                break;
            case RuleActionType.ApplyProfile when string.IsNullOrWhiteSpace(action.Profile):
                issues.Add(Issue.Error($"{path}.profile", Text.Get("Rules_ProfileToApplyRequired")));
                break;
            case RuleActionType.EnableAdapter or RuleActionType.DisableAdapter when action.Adapter is null or { IsEmpty: true }:
                issues.Add(Issue.Error($"{path}.adapter", Text.Get("Rules_AdapterRequired")));
                break;
            case RuleActionType.Notify when string.IsNullOrWhiteSpace(action.Message):
                issues.Add(Issue.Error($"{path}.message", Text.Get("Rules_MessageRequired")));
                break;
            case RuleActionType.RunCommand when string.IsNullOrWhiteSpace(action.Command):
                issues.Add(Issue.Error($"{path}.command", Text.Get("Rules_CommandRequired")));
                break;
            case RuleActionType.OpenUrl when !Uri.TryCreate(action.Url, UriKind.Absolute, out _):
                issues.Add(Issue.Error($"{path}.url", Text.Get("Rules_UrlRequired")));
                break;
            case RuleActionType.RunPowerAutomateFlow when action.Url is null || !action.Url.StartsWith("ms-powerautomate:", StringComparison.OrdinalIgnoreCase):
                issues.Add(Issue.Error($"{path}.url", Text.Get("Rules_FlowUrlRequired")));
                break;
            case RuleActionType.Delay when action.Seconds is < 1 or > 600:
                issues.Add(Issue.Error($"{path}.seconds", Text.Get("Rules_DelayRange")));
                break;
        }
    }
}
