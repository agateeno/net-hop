using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using NetHop.Core.Networking;
using NetHop.Core.Planning;
using NetHop.Engine;
using NetHop.Host;
using NetHop.Ipc;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace NetHop.App.Services;

/// <summary>
/// NetHop without its service: the engine the service hosts (<see cref="NetHopController"/>), running in the app,
/// which must be elevated. Nothing watches the network for rules, so automations do not run; network changes only
/// refresh what the app shows. Calls run on the thread pool, as they would at the other end of the pipe: reading
/// the network takes a moment and must not block the UI.
/// </summary>
public sealed class LocalService : INetHopService, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);

    private readonly ILoggerFactory _loggers;
    private readonly CimNetworkBackend _backend;
    private readonly NetHopController _controller;
    private readonly Timer _networkChanged;

    public LocalService()
    {
        // The same entries in the Application event log as the service writes (see its appsettings.json).
        _loggers = LoggerFactory.Create(builder => builder
            .AddEventLog(new EventLogSettings { SourceName = "NetHop" })
            .AddFilter<EventLogLoggerProvider>(null, LogLevel.Warning)
            .AddFilter<EventLogLoggerProvider>(typeof(NetHopController).FullName, LogLevel.Information));

        var paths = new DataPaths(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        _backend = new CimNetworkBackend();
        _controller = new NetHopController(_backend, new ProfileApplier(_backend),
            new ProfileStore(paths, _loggers.CreateLogger<ProfileStore>()),
            new RuleStore(paths, _loggers.CreateLogger<RuleStore>()),
            new StateStore(paths),
            new HistoryStore(paths, _loggers.CreateLogger<HistoryStore>()),
            paths, _loggers.CreateLogger<NetHopController>());

        _networkChanged = new Timer(_ => _controller.OnNetworkChanged());
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    public event EventHandler<StatusChangedEventArgs>? StatusChanged
    {
        add => _controller.StatusChanged += value;
        remove => _controller.StatusChanged -= value;
    }

    // Raised by rules only, which do not run here.
    public event EventHandler<UserActionRequestedEventArgs>? UserActionRequested
    {
        add { }
        remove { }
    }

    // A burst of notifications (a Wi-Fi join) becomes one refresh.
    private void OnNetworkChanged(object? sender, EventArgs e) => _networkChanged.Change(Debounce, Timeout.InfiniteTimeSpan);

    public Task<ServiceInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
        Call(() => _controller.GetInfoAsync(cancellationToken));

    public Task<ServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Call(() => _controller.GetStatusAsync(cancellationToken));

    public Task<NetworkSnapshot> GetNetworkAsync(CancellationToken cancellationToken = default) =>
        Call(() => _controller.GetNetworkAsync(cancellationToken));

    public Task<ProfileSummary[]> ListProfilesAsync(CancellationToken cancellationToken = default) =>
        Call(() => _controller.ListProfilesAsync(cancellationToken));

    public Task<string> GetProfileYamlAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Call(() => _controller.GetProfileYamlAsync(profileId, cancellationToken));

    public Task<SaveResult> SaveProfileYamlAsync(string yaml, CancellationToken cancellationToken = default) =>
        Call(() => _controller.SaveProfileYamlAsync(yaml, cancellationToken));

    public Task DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Call(() => _controller.DeleteProfileAsync(profileId, cancellationToken));

    public Task<string> CaptureProfileYamlAsync(string name, Guid[] adapterIds, CancellationToken cancellationToken = default) =>
        Call(() => _controller.CaptureProfileYamlAsync(name, adapterIds, cancellationToken));

    public Task<ApplyResult> ApplyProfileAsync(string profile, bool dryRun = false, CancellationToken cancellationToken = default) =>
        Call(() => _controller.ApplyProfileAsync(profile, dryRun, cancellationToken));

    public Task SetProfileOrderAsync(Guid[] profileIds, CancellationToken cancellationToken = default) =>
        Call(() => _controller.SetProfileOrderAsync(profileIds, cancellationToken));

    public Task<HistoryEntry[]> GetHistoryAsync(CancellationToken cancellationToken = default) =>
        Call(() => _controller.GetHistoryAsync(cancellationToken));

    public Task RenameAdapterAsync(Guid adapterId, string newName, CancellationToken cancellationToken = default) =>
        Call(() => _controller.RenameAdapterAsync(adapterId, newName, cancellationToken));

    public Task SetAdapterEnabledAsync(Guid adapterId, bool enabled, CancellationToken cancellationToken = default) =>
        Call(() => _controller.SetAdapterEnabledAsync(adapterId, enabled, cancellationToken));

    public Task<string> GetRulesYamlAsync(CancellationToken cancellationToken = default) =>
        Call(() => _controller.GetRulesYamlAsync(cancellationToken));

    public Task<SaveResult> SaveRulesYamlAsync(string yaml, CancellationToken cancellationToken = default) =>
        Call(() => _controller.SaveRulesYamlAsync(yaml, cancellationToken));

    public Task PauseAutomationsAsync(int minutes, CancellationToken cancellationToken = default) =>
        Call(() => _controller.PauseAutomationsAsync(minutes, cancellationToken));

    /// <summary>
    /// Runs a call on the thread pool. Errors are reported as the pipe reports them, so the pages handle both the same.
    /// </summary>
    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await Task.Run(call);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RemoteInvocationException(ex.Message, (int)JsonRpcErrorCode.InvocationError, errorData: null);
        }
    }

    private static Task Call(Func<Task> call) => Call<bool>(async () =>
    {
        await call();
        return true;
    });

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _networkChanged.Dispose();
        _backend.Dispose();
        _loggers.Dispose();
    }
}
