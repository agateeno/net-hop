using NetHop.Core;
using NetHop.Core.Networking;
using StreamJsonRpc;

namespace NetHop.Ipc;

/// <summary>
/// The API exposed by the NetHop Windows service over a named pipe (JSON-RPC).
/// Every client uses it: the WinUI app, the CLI, the Logi Actions plugin and the Power Automate actions.
/// Members of the Administrators group get full access; other interactive users get a read-only connection
/// (see <see cref="ServiceInfo.ReadOnly"/>): changes fail with <see cref="NetHopErrors.ReadOnly"/>.
/// </summary>
[JsonRpcContract]
public partial interface INetHopService
{
    /// <summary>Raised after a profile is applied, the network changes or the profile list changes.</summary>
    event EventHandler<StatusChangedEventArgs> StatusChanged;

    /// <summary>Raised when a rule wants something done in the user's session (toast, command, URL, Power Automate flow).</summary>
    event EventHandler<UserActionRequestedEventArgs> UserActionRequested;

    Task<ServiceInfo> GetInfoAsync(CancellationToken cancellationToken = default);

    Task<ServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<NetworkSnapshot> GetNetworkAsync(CancellationToken cancellationToken = default);

    // Profiles

    Task<ProfileSummary[]> ListProfilesAsync(CancellationToken cancellationToken = default);

    Task<string> GetProfileYamlAsync(Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces (same id) a profile. Nothing is saved when the result has errors.</summary>
    Task<SaveResult> SaveProfileYamlAsync(string yaml, CancellationToken cancellationToken = default);

    Task DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>Captures the current configuration of the given adapters as a new (unsaved) profile.</summary>
    Task<string> CaptureProfileYamlAsync(string name, Guid[] adapterIds, CancellationToken cancellationToken = default);

    /// <param name="profile">Profile id or name (case-insensitive).</param>
    /// <param name="dryRun">Only plan, change nothing.</param>
    Task<ApplyResult> ApplyProfileAsync(string profile, bool dryRun = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the order profiles are listed in (app, tray, CLI). Profiles left out follow, by name. When several profiles
    /// match the network, the first one in this order is reported as active.
    /// </summary>
    Task SetProfileOrderAsync(Guid[] profileIds, CancellationToken cancellationToken = default);

    /// <summary>The last profiles applied (not previews), newest first: at most <see cref="HistoryEntry.MaxEntries"/>.</summary>
    Task<HistoryEntry[]> GetHistoryAsync(CancellationToken cancellationToken = default);

    // Adapters

    Task RenameAdapterAsync(Guid adapterId, string newName, CancellationToken cancellationToken = default);

    Task SetAdapterEnabledAsync(Guid adapterId, bool enabled, CancellationToken cancellationToken = default);

    // Automations

    Task<string> GetRulesYamlAsync(CancellationToken cancellationToken = default);

    Task<SaveResult> SaveRulesYamlAsync(string yaml, CancellationToken cancellationToken = default);

    /// <param name="minutes">0 resumes immediately.</param>
    Task PauseAutomationsAsync(int minutes, CancellationToken cancellationToken = default);
}

public sealed record ServiceInfo(string Version, int ProtocolVersion, string ProfilesFolder)
{
    /// <summary>This connection can read but not change anything: the account is not an administrator.</summary>
    public bool ReadOnly { get; init; }
}

public static class NetHopErrors
{
    /// <summary>JSON-RPC error code of a change refused on a read-only connection.</summary>
    public const int ReadOnly = -32050;

    public static bool IsReadOnly(Exception exception) =>
        exception is RemoteInvocationException { ErrorCode: ReadOnly };
}

public sealed record ServiceStatus
{
    public Guid? ActiveProfileId { get; init; }

    public string? ActiveProfileName { get; init; }

    /// <summary>The last applied profile no longer matches the network (someone changed it by hand).</summary>
    public bool ActiveProfileDrifted { get; init; }

    public DateTimeOffset? LastAppliedAt { get; init; }

    public DateTimeOffset? AutomationsPausedUntil { get; init; }

    /// <summary>Wi-Fi SSIDs cannot be read, usually because location access is off (Windows 11 24H2+).</summary>
    public bool SsidUnavailable { get; init; }
}

public sealed record ProfileSummary(Guid Id, string Name, string? Description, string? Icon, string? Color, bool IsActive);

public sealed record SaveResult(bool Saved, Guid? ProfileId, Issue[] Issues);

public sealed record ApplyResult
{
    public bool Success { get; init; }

    public bool DryRun { get; init; }

    public string? ProfileName { get; init; }

    /// <summary>Human-readable steps, executed (or planned for a dry run) in order.</summary>
    public string[] Steps { get; init; } = [];

    public Issue[] Issues { get; init; } = [];

    public string? Error { get; init; }

    /// <summary>A step failed and the previous configuration was restored.</summary>
    public bool RolledBack { get; init; }

    public TimeSpan Duration { get; init; }
}

/// <summary>One profile change, kept by the service across restarts.</summary>
public sealed record HistoryEntry
{
    public const int MaxEntries = 10;

    public DateTimeOffset At { get; init; }

    public Guid ProfileId { get; init; }

    public string ProfileName { get; init; } = "";

    public bool Success { get; init; }

    public string? Error { get; init; }

    /// <summary>A step failed and the previous configuration was restored.</summary>
    public bool RolledBack { get; init; }

    /// <summary>Human-readable steps executed, in order; empty when the network already matched.</summary>
    public string[] Steps { get; init; } = [];

    /// <summary>The automation rule that applied the profile; null when someone applied it (app, tray, command line...).</summary>
    public string? RuleName { get; init; }
}

public enum StatusChangeKind
{
    ProfileApplied,
    NetworkChanged,
    ProfilesChanged,
    RulesChanged,
    AutomationsPaused,
}

public sealed class StatusChangedEventArgs : EventArgs
{
    public StatusChangeKind Kind { get; set; }

    public ServiceStatus? Status { get; set; }

    /// <summary>Set for <see cref="StatusChangeKind.ProfileApplied"/>.</summary>
    public ApplyResult? ApplyResult { get; set; }

    /// <summary>True when the change was made by an automation rule.</summary>
    public bool ByRule { get; set; }

    public string? RuleName { get; set; }
}

public sealed class UserActionRequestedEventArgs : EventArgs
{
    public string RuleName { get; set; } = "";

    public Core.Rules.RuleAction Action { get; set; } = new();
}
