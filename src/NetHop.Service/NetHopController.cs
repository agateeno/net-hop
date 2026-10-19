using System.Reflection;
using NetHop.Core;
using NetHop.Core.Networking;
using NetHop.Core.Planning;
using NetHop.Core.Profiles;
using NetHop.Core.Rules;
using NetHop.Core.Serialization;
using NetHop.Engine;
using NetHop.Ipc;

namespace NetHop.Service;

/// <summary>The service's single brain: implements the RPC API and runs automation rules.</summary>
public sealed class NetHopController : INetHopService
{
    private static readonly TimeSpan SnapshotMaxAge = TimeSpan.FromSeconds(2);

    // Network changes are serialized: two profiles must never be applied at the same time.
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private readonly CimNetworkBackend _backend;
    private readonly ProfileApplier _applier;
    private readonly ProfileStore _profiles;
    private readonly RuleStore _ruleStore;
    private readonly StateStore _stateStore;
    private readonly ServicePaths _paths;
    private readonly ILogger<NetHopController> _logger;

    private RuleEngine _rules;
    private StateStore.State _state;
    private NetworkSnapshot? _snapshot;

    public NetHopController(CimNetworkBackend backend, ProfileApplier applier, ProfileStore profiles, RuleStore ruleStore,
        StateStore stateStore, ServicePaths paths, ILogger<NetHopController> logger)
    {
        _backend = backend;
        _applier = applier;
        _profiles = profiles;
        _ruleStore = ruleStore;
        _stateStore = stateStore;
        _paths = paths;
        _logger = logger;
        _rules = new RuleEngine(ruleStore.Load());
        _state = stateStore.Load();
        _applier.Log += message => _logger.LogInformation("{Step}", message);
    }

    public event EventHandler<StatusChangedEventArgs>? StatusChanged;

    public event EventHandler<UserActionRequestedEventArgs>? UserActionRequested;

    public Task<ServiceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        return Task.FromResult(new ServiceInfo(version, NetHopPipe.ProtocolVersion, _paths.Profiles));
    }

    public Task<ServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildStatus(GetSnapshot()));

    public Task<NetworkSnapshot> GetNetworkAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(GetSnapshot(forceRefresh: true));

    public Task<ProfileSummary[]> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        var active = BuildStatus(GetSnapshot()).ActiveProfileId;
        return Task.FromResult(_profiles.All()
            .Select(p => new ProfileSummary(p.Id, p.Name, p.Description, p.Icon, p.Color, p.Id == active))
            .ToArray());
    }

    public Task<string> GetProfileYamlAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var path = _profiles.GetPath(profileId) ?? throw new KeyNotFoundException($"Profile {profileId} not found.");
        return File.ReadAllTextAsync(path, cancellationToken);
    }

    public Task<SaveResult> SaveProfileYamlAsync(string yaml, CancellationToken cancellationToken = default)
    {
        Profile profile;
        try
        {
            profile = ProfileSerializer.Deserialize(yaml);
        }
        catch (NetHopFormatException ex)
        {
            return Task.FromResult(new SaveResult(false, null, [Issue.Error($"line {ex.Line}", ex.Message)]));
        }

        var issues = ProfileValidator.Validate(profile).ToArray();
        if (issues.HasErrors())
            return Task.FromResult(new SaveResult(false, profile.Id, issues));

        try
        {
            _profiles.Save(profile);
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(new SaveResult(false, profile.Id, [.. issues, Issue.Error("name", ex.Message)]));
        }

        Raise(StatusChangeKind.ProfilesChanged);
        return Task.FromResult(new SaveResult(true, profile.Id, issues));
    }

    public Task DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (_profiles.Delete(profileId))
            Raise(StatusChangeKind.ProfilesChanged);
        return Task.CompletedTask;
    }

    public Task<string> CaptureProfileYamlAsync(string name, Guid[] adapterIds, CancellationToken cancellationToken = default)
    {
        var profile = SnapshotProfile.Create(GetSnapshot(forceRefresh: true), adapterIds, name);
        return Task.FromResult(ProfileSerializer.Serialize(profile));
    }

    public Task<ApplyResult> ApplyProfileAsync(string profile, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var found = _profiles.Find(profile);
        return found is null
            ? Task.FromResult(new ApplyResult { Error = $"Profile '{profile}' not found.", DryRun = dryRun })
            : ApplyAsync(found, dryRun, ruleName: null, cancellationToken);
    }

    public async Task RenameAdapterAsync(Guid adapterId, string newName, CancellationToken cancellationToken = default)
    {
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Renaming adapter {Id} to {Name}", adapterId, newName);
            await Task.Run(() => _backend.RenameAdapter(adapterId, newName), cancellationToken);
            _snapshot = null;
        }
        finally
        {
            _applyLock.Release();
        }
        Raise(StatusChangeKind.NetworkChanged);
    }

    public async Task SetAdapterEnabledAsync(Guid adapterId, bool enabled, CancellationToken cancellationToken = default)
    {
        var adapter = GetSnapshot(forceRefresh: true).Find(adapterId)?.Info
            ?? throw new KeyNotFoundException($"Adapter {adapterId} not found.");

        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            PlanStep step = enabled ? new EnableAdapterStep(adapter) : new DisableAdapterStep(adapter);
            _logger.LogInformation("{Step}", step.Describe());
            await Task.Run(() => _backend.Execute(step, cancellationToken), cancellationToken);
            _snapshot = null;
        }
        finally
        {
            _applyLock.Release();
        }
        Raise(StatusChangeKind.NetworkChanged);
    }

    public Task<string> GetRulesYamlAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_ruleStore.LoadYaml());

    public Task<SaveResult> SaveRulesYamlAsync(string yaml, CancellationToken cancellationToken = default)
    {
        RuleSet set;
        try
        {
            set = RuleSetSerializer.Deserialize(yaml);
        }
        catch (NetHopFormatException ex)
        {
            return Task.FromResult(new SaveResult(false, null, [Issue.Error($"line {ex.Line}", ex.Message)]));
        }

        var issues = RuleSetSerializer.Validate(set).ToArray();
        if (issues.HasErrors())
            return Task.FromResult(new SaveResult(false, null, issues));

        _ruleStore.Save(yaml);
        var previous = _rules;
        _rules = new RuleEngine(set);
        if (previous.PausedUntil is { } paused)
            _rules.Pause(paused);
        if (previous.LastProfileAppliedAt is { } applied)
            _rules.NotifyProfileApplied(applied);

        Raise(StatusChangeKind.RulesChanged);
        return Task.FromResult(new SaveResult(true, null, issues));
    }

    public Task PauseAutomationsAsync(int minutes, CancellationToken cancellationToken = default)
    {
        if (minutes <= 0)
            _rules.Resume();
        else
            _rules.Pause(DateTimeOffset.Now.AddMinutes(minutes));
        Raise(StatusChangeKind.AutomationsPaused);
        return Task.CompletedTask;
    }

    /// <summary>Called by <see cref="NetworkMonitor"/> with the events derived from a network change.</summary>
    internal async Task OnNetworkEventsAsync(IReadOnlyList<NetworkEvent> events, NetworkSnapshot snapshot, CancellationToken cancellationToken)
    {
        _snapshot = snapshot;
        if (events.Any(e => e.Type == TriggerType.NetworkChanged))
            Raise(StatusChangeKind.NetworkChanged);

        foreach (var e in events)
        {
            var context = new RuleContext
            {
                Network = snapshot,
                ActiveProfile = BuildStatus(snapshot).ActiveProfileName,
                OnBattery = PowerStatus.IsOnBattery(),
            };

            var evaluation = _rules.Evaluate(e, context);
            foreach (var reason in evaluation.Skipped)
                _logger.LogDebug("Rule skipped: {Reason}", reason);

            foreach (var match in evaluation.Matches)
            {
                _logger.LogInformation("Rule '{Rule}' fired on {Trigger}", match.Rule.Name, e.Type);
                foreach (var action in match.Actions)
                    await RunActionAsync(match.Rule, action, snapshot, cancellationToken);
            }
        }
    }

    private async Task RunActionAsync(Rule rule, RuleAction action, NetworkSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            switch (action.Type)
            {
                case RuleActionType.ApplyProfile:
                    var profile = _profiles.Find(action.Profile!);
                    if (profile is null)
                        _logger.LogWarning("Rule '{Rule}': profile '{Profile}' not found", rule.Name, action.Profile);
                    else
                        await ApplyAsync(profile, dryRun: false, rule.Name, cancellationToken);
                    break;

                case RuleActionType.EnableAdapter or RuleActionType.DisableAdapter:
                    var match = AdapterMatcher.Resolve(action.Adapter!, snapshot.Adapters.Select(a => a.Info).ToList());
                    if (match.Found)
                        await SetAdapterEnabledAsync(match.Adapter!.Id, action.Type == RuleActionType.EnableAdapter, cancellationToken);
                    else
                        _logger.LogWarning("Rule '{Rule}': {Problem}", rule.Name, match.Problem);
                    break;

                case RuleActionType.Delay:
                    await Task.Delay(TimeSpan.FromSeconds(action.Seconds), cancellationToken);
                    break;

                default:
                    // Toasts, commands, URLs and Power Automate flows run in the user's session, never as SYSTEM.
                    if (UserActionRequested is null)
                        _logger.LogWarning("Rule '{Rule}': no NetHop app is running to perform {Action}", rule.Name, action.Type);
                    UserActionRequested?.Invoke(this, new UserActionRequestedEventArgs { RuleName = rule.Name, Action = action });
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Rule '{Rule}': action {Action} failed", rule.Name, action.Type);
        }
    }

    private async Task<ApplyResult> ApplyAsync(Profile profile, bool dryRun, string? ruleName, CancellationToken cancellationToken)
    {
        await _applyLock.WaitAsync(cancellationToken);
        ApplyOutcome outcome;
        try
        {
            _logger.LogInformation("{Mode} profile '{Profile}'{Rule}", dryRun ? "Planning" : "Applying", profile.Name,
                ruleName is null ? "" : $" (rule '{ruleName}')");
            outcome = await Task.Run(() => _applier.Apply(profile, dryRun, cancellationToken), cancellationToken);
            _snapshot = null;

            if (!dryRun && outcome.Success)
            {
                _state = new StateStore.State(profile.Id, DateTimeOffset.Now);
                _stateStore.Save(_state);
                _rules.NotifyProfileApplied(DateTimeOffset.Now);
            }
        }
        finally
        {
            _applyLock.Release();
        }

        var result = new ApplyResult
        {
            Success = outcome.Success,
            DryRun = dryRun,
            ProfileName = profile.Name,
            Steps = (dryRun ? outcome.Plan.Steps : outcome.Executed).Select(s => s.Describe()).ToArray(),
            Issues = outcome.Plan.Issues.ToArray(),
            Error = outcome.Error,
            RolledBack = outcome.RolledBack,
            Duration = outcome.Duration,
        };

        if (!dryRun)
        {
            if (!outcome.Success)
                _logger.LogError("Profile '{Profile}' failed: {Error} (rolled back: {RolledBack})", profile.Name, outcome.Error, outcome.RolledBack);
            Raise(StatusChangeKind.ProfileApplied, result, ruleName);
        }
        return result;
    }

    private NetworkSnapshot GetSnapshot(bool forceRefresh = false)
    {
        var snapshot = _snapshot;
        if (forceRefresh || snapshot is null || DateTimeOffset.Now - snapshot.TakenAt > SnapshotMaxAge)
            _snapshot = snapshot = _backend.GetSnapshot();
        return snapshot;
    }

    private ServiceStatus BuildStatus(NetworkSnapshot snapshot)
    {
        var profiles = _profiles.All();
        var last = profiles.FirstOrDefault(p => p.Id == _state.LastAppliedProfileId);

        // The last applied profile wins if it still matches; otherwise any profile that matches the network.
        var active = last is not null && ProfilePlanner.IsSatisfied(last, snapshot)
            ? last
            : profiles.FirstOrDefault(p => ProfilePlanner.IsSatisfied(p, snapshot));

        return new ServiceStatus
        {
            ActiveProfileId = active?.Id,
            ActiveProfileName = active?.Name,
            ActiveProfileDrifted = last is not null && active?.Id != last.Id,
            LastAppliedAt = _state.LastAppliedAt,
            AutomationsPausedUntil = _rules.PausedUntil > DateTimeOffset.Now ? _rules.PausedUntil : null,
            SsidUnavailable = _backend.SsidAccessDenied,
        };
    }

    private void Raise(StatusChangeKind kind, ApplyResult? result = null, string? ruleName = null)
    {
        try
        {
            StatusChanged?.Invoke(this, new StatusChangedEventArgs
            {
                Kind = kind,
                Status = BuildStatus(GetSnapshot()),
                ApplyResult = result,
                ByRule = ruleName is not null,
                RuleName = ruleName,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not notify clients about {Kind}", kind);
        }
    }
}
