using System.Globalization;
using NetHop.Core.Networking;

namespace NetHop.Core.Rules;

/// <summary>Something that happened on the machine and may fire rules.</summary>
public sealed record NetworkEvent(TriggerType Type, DateTimeOffset Timestamp)
{
    public string? Ssid { get; init; }

    public AdapterInfo? Adapter { get; init; }
}

/// <summary>Everything conditions may look at, captured when the event is evaluated.</summary>
public sealed record RuleContext
{
    public required NetworkSnapshot Network { get; init; }

    public string? ActiveProfile { get; init; }

    public bool? OnBattery { get; init; }
}

public sealed record RuleMatch(Rule Rule, IReadOnlyList<RuleAction> Actions);

public sealed record RuleEvaluation(IReadOnlyList<RuleMatch> Matches, IReadOnlyList<string> Skipped)
{
    public static RuleEvaluation Empty { get; } = new([], []);
}

/// <summary>
/// Decides which rules fire for an event. Pure logic with explicit time, so it is fully unit-testable.
/// The guards against automation loops live here: applying a profile changes the network, which raises
/// new events, which must not re-trigger rules.
/// </summary>
public sealed class RuleEngine(RuleSet rules)
{
    private readonly Dictionary<Guid, DateTimeOffset> _lastRun = [];

    /// <summary>After NetHop applies a profile, events within this window are ignored.</summary>
    public TimeSpan SuppressionWindow { get; init; } = TimeSpan.FromSeconds(10);

    public DateTimeOffset? PausedUntil { get; private set; }

    public DateTimeOffset? LastProfileAppliedAt { get; private set; }

    public RuleSet Rules { get; } = rules;

    /// <summary>The user picked a profile by hand: automations step aside for a while.</summary>
    public void Pause(DateTimeOffset until) => PausedUntil = until;

    public void Resume() => PausedUntil = null;

    /// <summary>Call whenever a profile is applied, by a rule or by the user.</summary>
    public void NotifyProfileApplied(DateTimeOffset at) => LastProfileAppliedAt = at;

    public RuleEvaluation Evaluate(NetworkEvent e, RuleContext context)
    {
        if (PausedUntil is { } paused && e.Timestamp < paused)
            return new([], [$"Automations are paused until {paused:t}."]);

        // Startup/logon/resume/schedule are not caused by NetHop itself, so they are never suppressed.
        var networkCaused = e.Type is TriggerType.WifiConnected or TriggerType.WifiDisconnected
            or TriggerType.AdapterConnected or TriggerType.AdapterDisconnected or TriggerType.NetworkChanged;
        if (networkCaused && LastProfileAppliedAt is { } applied && e.Timestamp - applied < SuppressionWindow)
            return new([], ["Event ignored: it follows a profile change made by NetHop."]);

        var matches = new List<RuleMatch>();
        var skipped = new List<string>();
        var profileApplied = false;

        foreach (var rule in Rules.Rules.Where(r => r.Enabled).OrderByDescending(r => r.Priority))
        {
            if (!TriggerMatches(rule.Trigger, e))
                continue;

            if (rule.CooldownSeconds > 0 && _lastRun.TryGetValue(rule.Id, out var last)
                && e.Timestamp - last < TimeSpan.FromSeconds(rule.CooldownSeconds))
            {
                skipped.Add($"'{rule.Name}': in cooldown.");
                continue;
            }

            var failed = rule.Conditions.FirstOrDefault(c => !ConditionHolds(c, e, context));
            if (failed is not null)
            {
                skipped.Add($"'{rule.Name}': condition {failed.Type} not met.");
                continue;
            }

            var actions = new List<RuleAction>();
            foreach (var action in rule.Actions)
            {
                if (action.Type == RuleActionType.ApplyProfile)
                {
                    if (profileApplied)
                    {
                        skipped.Add($"'{rule.Name}': profile '{action.Profile}' not applied, a higher-priority rule already applies one.");
                        continue;
                    }
                    profileApplied = true;
                }
                actions.Add(action);
            }

            _lastRun[rule.Id] = e.Timestamp;
            matches.Add(new RuleMatch(rule, actions));
        }

        return new(matches, skipped);
    }

    internal static bool TriggerMatches(Trigger trigger, NetworkEvent e)
    {
        if (trigger.Type != e.Type)
            return false;

        return trigger.Type switch
        {
            TriggerType.WifiConnected or TriggerType.WifiDisconnected => SsidMatches(trigger.Ssid, e.Ssid),
            TriggerType.AdapterConnected or TriggerType.AdapterDisconnected =>
                trigger.Adapter is null || (e.Adapter is not null && AdapterMatcher.Resolve(trigger.Adapter, [e.Adapter]).Found),
            TriggerType.Schedule => ScheduleMatches(trigger, e.Timestamp),
            _ => true,
        };
    }

    internal static bool ConditionHolds(Condition condition, NetworkEvent e, RuleContext context)
    {
        var now = e.Timestamp;
        return condition.Type switch
        {
            ConditionType.TimeBetween => IsTimeBetween(TimeOnly.FromDateTime(now.LocalDateTime), condition.From, condition.To),
            ConditionType.DaysOfWeek => condition.Days.Count == 0 || condition.Days.Contains(now.LocalDateTime.DayOfWeek),
            ConditionType.AdapterConnected => FindAdapter(condition, context)?.Connected == true,
            ConditionType.AdapterDisconnected => FindAdapter(condition, context) is not { Connected: true },
            ConditionType.WifiSsid => context.Network.Adapters.Any(a => a.Connected && SsidMatches(condition.Ssid, a.Ssid) && a.Ssid is not null),
            ConditionType.ActiveProfile => string.Equals(context.ActiveProfile, condition.Profile, StringComparison.OrdinalIgnoreCase),
            ConditionType.NotActiveProfile => !string.Equals(context.ActiveProfile, condition.Profile, StringComparison.OrdinalIgnoreCase),
            ConditionType.OnBattery => context.OnBattery == true,
            ConditionType.OnAcPower => context.OnBattery == false,
            _ => false,
        };
    }

    private static AdapterSnapshot? FindAdapter(Condition condition, RuleContext context)
    {
        if (condition.Adapter is null)
            return null;
        var match = AdapterMatcher.Resolve(condition.Adapter, context.Network.Adapters.Select(a => a.Info).ToList());
        return match.Found ? context.Network.Find(match.Adapter!.Id) : null;
    }

    internal static bool SsidMatches(string? pattern, string? ssid)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;
        if (ssid is null)
            return false;
        // SSIDs are case-sensitive in 802.11, so the comparison is ordinal.
        return pattern.EndsWith('*')
            ? ssid.StartsWith(pattern[..^1], StringComparison.Ordinal)
            : ssid == pattern;
    }

    internal static bool IsTimeBetween(TimeOnly time, string? from, string? to)
    {
        if (!TryParseTime(from, out var start) || !TryParseTime(to, out var end))
            return false;
        return start <= end
            ? time >= start && time < end
            : time >= start || time < end; // wraps past midnight, e.g. 22:00-06:00
    }

    private static bool ScheduleMatches(Trigger trigger, DateTimeOffset timestamp)
    {
        if (!TryParseTime(trigger.At, out var at))
            return false;
        var local = timestamp.LocalDateTime;
        var sameMinute = local.Hour == at.Hour && local.Minute == at.Minute;
        return sameMinute && (trigger.Days.Count == 0 || trigger.Days.Contains(local.DayOfWeek));
    }

    internal static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
