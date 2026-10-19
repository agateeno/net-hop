using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NetHop.Ipc;

namespace NetHop.App.Services;

public enum ConnectionState
{
    Connecting,
    Connected,
    ServiceNotRunning,
    AccessDenied,
}

/// <summary>
/// Keeps a connection to the NetHop service alive (reconnecting after restarts) and
/// re-raises service events on the UI thread.
/// </summary>
public sealed partial class ServiceConnection : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stop = new();
    private NetHopClient? _client;

    public ServiceConnection()
    {
        Dispatcher = DispatcherQueue.GetForCurrentThread();
        State = ConnectionState.Connecting;
        Profiles = [];
    }

    public DispatcherQueue Dispatcher { get; }

    [ObservableProperty]
    public partial ConnectionState State { get; private set; }

    [ObservableProperty]
    public partial ServiceStatus? Status { get; private set; }

    [ObservableProperty]
    public partial ProfileSummary[] Profiles { get; private set; }

    /// <summary>Connected, but the account is not an administrator: NetHop can be viewed, not changed.</summary>
    [ObservableProperty]
    public partial bool IsReadOnly { get; private set; }

    public bool IsConnected => State == ConnectionState.Connected;

    /// <summary>Connected with full access: profiles, rules and adapters can be changed.</summary>
    public bool CanEdit => IsConnected && !IsReadOnly;

    /// <summary>Service events, raised on the UI thread.</summary>
    public event EventHandler<StatusChangedEventArgs>? StatusChanged;

    /// <summary>A profile was applied (from anywhere): the tray shows a notification.</summary>
    public event EventHandler<ApplyResult>? ProfileApplied;

    public event EventHandler<UserActionRequestedEventArgs>? UserActionRequested;

    /// <exception cref="InvalidOperationException">Not connected.</exception>
    public INetHopService Service => _client?.Service ?? throw new InvalidOperationException("NetHop service is not connected.");

    partial void OnStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanEdit));
    }

    partial void OnIsReadOnlyChanged(bool value) => OnPropertyChanged(nameof(CanEdit));

    public void Start() => _ = RunAsync(_stop.Token);

    public async Task<ApplyResult?> ApplyAsync(string profile)
    {
        if (!IsConnected)
            return null;
        try
        {
            return await Service.ApplyProfileAsync(profile);
        }
        catch (Exception ex) when (ex is StreamJsonRpc.RemoteInvocationException or StreamJsonRpc.ConnectionLostException)
        {
            return new ApplyResult { Error = ex.Message, ProfileName = profile };
        }
    }

    public async Task RefreshAsync()
    {
        if (_client is null)
            return;
        var status = await _client.Service.GetStatusAsync();
        var profiles = await _client.Service.ListProfilesAsync();
        OnUi(() =>
        {
            Status = status;
            Profiles = profiles;
        });
    }

    private async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var established = false;
            try
            {
                OnUi(() => State = ConnectionState.Connecting);
                _client = await NetHopClient.ConnectAsync(TimeSpan.FromSeconds(3), stop);
                _client.Service.StatusChanged += OnStatusChanged;
                _client.Service.UserActionRequested += OnUserActionRequested;

                var info = await _client.Service.GetInfoAsync();
                await RefreshAsync();
                established = true;
                OnUi(() =>
                {
                    IsReadOnly = info.ReadOnly;
                    State = ConnectionState.Connected;
                });

                await _client.Completion.WaitAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException)
            {
                OnUi(() => State = ConnectionState.ServiceNotRunning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or StreamJsonRpc.ConnectionLostException)
            {
                // NetHop 0.1.8 and earlier dropped connections from non-administrators right after accepting them.
                OnUi(() => State = established ? ConnectionState.ServiceNotRunning : ConnectionState.AccessDenied);
            }
            finally
            {
                if (_client is not null)
                {
                    await _client.DisposeAsync();
                    _client = null;
                }
            }

            try
            {
                await Task.Delay(RetryDelay, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnStatusChanged(object? sender, StatusChangedEventArgs e) => OnUi(async () =>
    {
        Status = e.Status;
        if (e.Kind is StatusChangeKind.ProfilesChanged or StatusChangeKind.ProfileApplied or StatusChangeKind.NetworkChanged)
            Profiles = _client is null ? Profiles : await _client.Service.ListProfilesAsync();

        StatusChanged?.Invoke(this, e);
        if (e is { Kind: StatusChangeKind.ProfileApplied, ApplyResult: { DryRun: false } result })
            ProfileApplied?.Invoke(this, result);
    });

    private void OnUserActionRequested(object? sender, UserActionRequestedEventArgs e) =>
        OnUi(() => UserActionRequested?.Invoke(this, e));

    private void OnUi(Action action)
    {
        if (Dispatcher.HasThreadAccess)
            action();
        else
            Dispatcher.TryEnqueue(() => action());
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_client is not null)
            await _client.DisposeAsync();
    }
}
