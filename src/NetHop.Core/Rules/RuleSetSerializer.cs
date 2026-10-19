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
            issues.Add(Issue.Error("schema", $"Unsupported schema version {set.Schema}."));

        for (var i = 0; i < set.Rules.Count; i++)
        {
            var rule = set.Rules[i];
            var path = $"rules[{i}]";

            if (string.IsNullOrWhiteSpace(rule.Name))
                issues.Add(Issue.Error($"{path}.name", "The rule needs a name."));
            if (rule.Trigger.Type == TriggerType.None)
                issues.Add(Issue.Error($"{path}.trigger.type", "The rule needs a trigger."));
            if (rule.Trigger.Type == TriggerType.Schedule && !RuleEngine.TryParseTime(rule.Trigger.At, out _))
                issues.Add(Issue.Error($"{path}.trigger.at", "Use the HH:mm format."));
            if (rule.Actions.Count == 0)
                issues.Add(Issue.Error($"{path}.actions", "The rule has no actions."));

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
                issues.Add(Issue.Error($"{path}.type", "Missing condition type."));
                break;
            case ConditionType.TimeBetween when !RuleEngine.TryParseTime(condition.From, out _) || !RuleEngine.TryParseTime(condition.To, out _):
                issues.Add(Issue.Error(path, "'from' and 'to' must use the HH:mm format."));
                break;
            case ConditionType.AdapterConnected or ConditionType.AdapterDisconnected when condition.Adapter is null or { IsEmpty: true }:
                issues.Add(Issue.Error($"{path}.adapter", "Specify the adapter."));
                break;
            case ConditionType.ActiveProfile or ConditionType.NotActiveProfile when string.IsNullOrWhiteSpace(condition.Profile):
                issues.Add(Issue.Error($"{path}.profile", "Specify the profile name."));
                break;
            case ConditionType.WifiSsid when string.IsNullOrEmpty(condition.Ssid):
                issues.Add(Issue.Error($"{path}.ssid", "Specify the SSID."));
                break;
        }
    }

    private static void ValidateAction(RuleAction action, string path, List<Issue> issues)
    {
        switch (action.Type)
        {
            case RuleActionType.None:
                issues.Add(Issue.Error($"{path}.type", "Missing action type."));
                break;
            case RuleActionType.ApplyProfile when string.IsNullOrWhiteSpace(action.Profile):
                issues.Add(Issue.Error($"{path}.profile", "Specify the profile to apply."));
                break;
            case RuleActionType.EnableAdapter or RuleActionType.DisableAdapter when action.Adapter is null or { IsEmpty: true }:
                issues.Add(Issue.Error($"{path}.adapter", "Specify the adapter."));
                break;
            case RuleActionType.Notify when string.IsNullOrWhiteSpace(action.Message):
                issues.Add(Issue.Error($"{path}.message", "Specify the notification text."));
                break;
            case RuleActionType.RunCommand when string.IsNullOrWhiteSpace(action.Command):
                issues.Add(Issue.Error($"{path}.command", "Specify the command to run."));
                break;
            case RuleActionType.OpenUrl when !Uri.TryCreate(action.Url, UriKind.Absolute, out _):
                issues.Add(Issue.Error($"{path}.url", "Specify an absolute URL."));
                break;
            case RuleActionType.RunPowerAutomateFlow when action.Url is null || !action.Url.StartsWith("ms-powerautomate:", StringComparison.OrdinalIgnoreCase):
                issues.Add(Issue.Error($"{path}.url", "Paste the flow's run URL (it starts with ms-powerautomate:)."));
                break;
            case RuleActionType.Delay when action.Seconds is < 1 or > 600:
                issues.Add(Issue.Error($"{path}.seconds", "The delay must be between 1 and 600 seconds."));
                break;
        }
    }
}
