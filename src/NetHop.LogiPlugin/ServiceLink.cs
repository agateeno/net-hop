using NetHop.Ipc;

namespace Loupedeck.NetHopPlugin;

internal enum LinkState
{
    Connecting,
    Connected,
    ServiceNotRunning,
    AccessDenied,
}

/// <summary>
/// Keeps a connection to the NetHop service (reconnecting after restarts) and caches what buttons draw:
/// the profile list, the status and whether an apply is running.
/// </summary>
internal sealed class ServiceLink : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stop = new();
    private volatile NetHopClient _client;
    private volatile ProfileSummary[] _profiles = [];
    private volatile ServiceStatus _status;
    private volatile string _busyProfileId;

    /// <summary>Raised on any change; handlers redraw their buttons. May fire on any thread.</summary>
    public event EventHandler Changed;

    public LinkState State { get; private set; } = LinkState.Connecting;

    public IReadOnlyList<ProfileSummary> Profiles => this._profiles;

    public ServiceStatus Status => this._status;

    public bool IsBusy(string profileId) => this._busyProfileId == profileId;

    public ProfileSummary Find(string profileId) =>
        this._profiles.FirstOrDefault(p => p.Id.ToString() == profileId);

    public void Start() => _ = this.RunAsync(this._stop.Token);

    public async Task ApplyAsync(string profileId) =>
        await this.RunBusyAsync(profileId, client => client.Service.ApplyProfileAsync(profileId));

    public async Task ApplyNextAsync() =>
        await this.RunBusyAsync("next", client => client.Service.ApplyNextProfileAsync());

    public async Task SetAutomationsPausedAsync(bool paused)
    {
        if (this._client is { } client)
            await this.SafeAsync(() => client.Service.PauseAutomationsAsync(paused ? 60 : 0));
    }

    private async Task RunBusyAsync(string busyId, Func<NetHopClient, Task<ApplyResult>> action)
    {
        if (this._client is not { } client || this._busyProfileId is not null)
            return;

        this._busyProfileId = busyId;
        this.RaiseChanged();
        try
        {
            var result = await action(client);
            if (!result.Success)
                PluginLog.Info($"Apply failed: {result.Error}");
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "Apply failed");
        }
        finally
        {
            this._busyProfileId = null;
            await this.RefreshAsync();
        }
    }

    private async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var established = false;
            try
            {
                this.SetState(LinkState.Connecting);
                this._client = await NetHopClient.ConnectAsync(TimeSpan.FromSeconds(3), stop);
                this._client.Service.StatusChanged += this.OnStatusChanged;

                // Accounts that are not administrators get a read-only connection: the buttons could not apply anything.
                var info = await this._client.Service.GetInfoAsync();
                await this.RefreshAsync();
                established = true;
                this.SetState(info.ReadOnly ? LinkState.AccessDenied : LinkState.Connected);

                await this._client.Completion.WaitAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException)
            {
                this.SetState(LinkState.ServiceNotRunning);
            }
            catch (Exception ex)
            {
                // NetHop 0.1.8 and earlier dropped connections from non-administrators right after accepting them.
                PluginLog.Warning(ex, "NetHop connection lost");
                this.SetState(established ? LinkState.ServiceNotRunning : LinkState.AccessDenied);
            }
            finally
            {
                if (this._client is { } client)
                {
                    this._client = null;
                    await client.DisposeAsync();
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

    private async void OnStatusChanged(object sender, StatusChangedEventArgs e)
    {
        this._status = e.Status;
        await this.RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (this._client is not { } client)
            return;
        await this.SafeAsync(async () =>
        {
            this._profiles = await client.Service.ListProfilesAsync();
            this._status = await client.Service.GetStatusAsync();
        });
        this.RaiseChanged();
    }

    private async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "NetHop call failed");
        }
    }

    private void SetState(LinkState state)
    {
        if (this.State == state)
            return;
        this.State = state;
        this.RaiseChanged();
    }

    private void RaiseChanged()
    {
        try
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "Redraw failed");
        }
    }

    public void Dispose()
    {
        this._stop.Cancel();
        this._stop.Dispose();
    }
}
