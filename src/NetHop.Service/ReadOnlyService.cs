using NetHop.Core.Networking;
using NetHop.Ipc;
using StreamJsonRpc;

namespace NetHop.Service;

/// <summary>
/// What accounts that are not administrators get: they can see profiles, rules and the network, and preview
/// (dry-run) a profile, but every change is refused. Rule actions (toasts, commands, flows) are not sent to them.
/// </summary>
public sealed class ReadOnlyService(INetHopService inner) : INetHopService
{
    public event EventHandler<StatusChangedEventArgs>? StatusChanged
    {
        add => inner.StatusChanged += value;
        remove => inner.StatusChanged -= value;
    }

    public event EventHandler<UserActionRequestedEventArgs>? UserActionRequested
    {
        add { }
        remove { }
    }

    public async Task<ServiceInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
        await inner.GetInfoAsync(cancellationToken) with { ReadOnly = true };

    public Task<ServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default) => inner.GetStatusAsync(cancellationToken);

    public Task<NetworkSnapshot> GetNetworkAsync(CancellationToken cancellationToken = default) => inner.GetNetworkAsync(cancellationToken);

    public Task<ProfileSummary[]> ListProfilesAsync(CancellationToken cancellationToken = default) => inner.ListProfilesAsync(cancellationToken);

    public Task<string> GetProfileYamlAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        inner.GetProfileYamlAsync(profileId, cancellationToken);

    public Task<SaveResult> SaveProfileYamlAsync(string yaml, CancellationToken cancellationToken = default) => throw Refused();

    public Task DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken = default) => throw Refused();

    public Task<string> CaptureProfileYamlAsync(string name, Guid[] adapterIds, CancellationToken cancellationToken = default) =>
        inner.CaptureProfileYamlAsync(name, adapterIds, cancellationToken);

    public Task<ApplyResult> ApplyProfileAsync(string profile, bool dryRun = false, CancellationToken cancellationToken = default) =>
        dryRun ? inner.ApplyProfileAsync(profile, dryRun: true, cancellationToken) : throw Refused();

    public Task SetProfileOrderAsync(Guid[] profileIds, CancellationToken cancellationToken = default) => throw Refused();

    public Task<HistoryEntry[]> GetHistoryAsync(CancellationToken cancellationToken = default) => inner.GetHistoryAsync(cancellationToken);

    public Task RenameAdapterAsync(Guid adapterId, string newName, CancellationToken cancellationToken = default) => throw Refused();

    public Task SetAdapterEnabledAsync(Guid adapterId, bool enabled, CancellationToken cancellationToken = default) => throw Refused();

    public Task<string> GetRulesYamlAsync(CancellationToken cancellationToken = default) => inner.GetRulesYamlAsync(cancellationToken);

    public Task<SaveResult> SaveRulesYamlAsync(string yaml, CancellationToken cancellationToken = default) => throw Refused();

    public Task PauseAutomationsAsync(int minutes, CancellationToken cancellationToken = default) => throw Refused();

    private static LocalRpcException Refused() =>
        new("NetHop is read-only for accounts that are not administrators. Run NetHop as administrator to make changes.")
        {
            ErrorCode = NetHopErrors.ReadOnly,
        };
}
