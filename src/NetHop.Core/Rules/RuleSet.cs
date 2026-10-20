using NetHop.Core.Profiles;

namespace NetHop.Core.Rules;

/// <summary>Content of <c>rules.nethop.yml</c>: the internal automation engine's rules.</summary>
public sealed class RuleSet
{
    public int Schema { get; set; } = NetHopConstants.CurrentSchemaVersion;

    public List<Rule> Rules { get; set; } = [];
}

/// <summary>When <see cref="Trigger"/> fires and every condition holds, the actions run in order.</summary>
public sealed class Rule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Higher wins. When several matching rules apply a profile, only the highest one does.</summary>
    public int Priority { get; set; }

    /// <summary>Minimum time between two runs of this rule.</summary>
    public int CooldownSeconds { get; set; }

    public Trigger Trigger { get; set; } = new();

    public List<Condition> Conditions { get; set; } = [];

    public List<RuleAction> Actions { get; set; } = [];
}

public enum TriggerType
{
    None,
    WifiConnected,
    WifiDisconnected,
    AdapterConnected,
    AdapterDisconnected,
    NetworkChanged,
    Startup,
    Logon,
    Resume,
    Schedule,
}

public sealed class Trigger
{
    public TriggerType Type { get; set; }

    /// <summary>For Wi-Fi triggers; null or empty means any network. A trailing <c>*</c> is a prefix match.</summary>
    public string? Ssid { get; set; }

    /// <summary>For adapter triggers; null means any adapter.</summary>
    public AdapterMatch? Adapter { get; set; }

    /// <summary>For <see cref="TriggerType.Schedule"/>: local time as <c>HH:mm</c>.</summary>
    public string? At { get; set; }

    /// <summary>For <see cref="TriggerType.Schedule"/>: empty means every day.</summary>
    public List<DayOfWeek> Days { get; set; } = [];
}

public enum ConditionType
{
    None,
    TimeBetween,
    DaysOfWeek,
    AdapterConnected,
    AdapterDisconnected,
    WifiSsid,
    ActiveProfile,
    NotActiveProfile,
    OnBattery,
    OnAcPower,
}

public sealed class Condition
{
    public ConditionType Type { get; set; }

    /// <summary><c>HH:mm</c>; when <see cref="To"/> is earlier than <see cref="From"/> the range wraps past midnight.</summary>
    public string? From { get; set; }

    public string? To { get; set; }

    public List<DayOfWeek> Days { get; set; } = [];

    public AdapterMatch? Adapter { get; set; }

    public string? Ssid { get; set; }

    public string? Profile { get; set; }
}

public enum RuleActionType
{
    None,
    ApplyProfile,
    EnableAdapter,
    DisableAdapter,
    Notify,
    RunCommand,
    OpenUrl,

    /// <summary>Runs a Power Automate Desktop flow through its <c>ms-powerautomate:</c> run URL.</summary>
    RunPowerAutomateFlow,

    Delay,
}

public sealed class RuleAction
{
    public RuleActionType Type { get; set; }

    public string? Profile { get; set; }

    public AdapterMatch? Adapter { get; set; }

    public string? Message { get; set; }

    /// <summary>Executable for <see cref="RuleActionType.RunCommand"/>. Always runs as the logged-on user, never as SYSTEM.</summary>
    public string? Command { get; set; }

    public string? Arguments { get; set; }

    public string? Url { get; set; }

    public int Seconds { get; set; }

    /// <summary>Actions that run in the user's session (UI agent) rather than in the service.</summary>
    public bool RunsInUserSession => Type is RuleActionType.Notify or RuleActionType.RunCommand
        or RuleActionType.OpenUrl or RuleActionType.RunPowerAutomateFlow;

    /// <summary>
    /// <see cref="RuleActionType.OpenUrl"/> opens web pages only: the shell runs whatever a file:// (or other) URL
    /// points to, which would make it a second, less visible <see cref="RuleActionType.RunCommand"/>.
    /// </summary>
    public static bool IsWebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>A Power Automate Desktop run URL (<c>ms-powerautomate:</c>).</summary>
    public static bool IsFlowAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme.Equals("ms-powerautomate", StringComparison.OrdinalIgnoreCase);
}
