using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using NetHop.Core.Networking;
using NetHop.Core.Rules;
using NetHop.Engine;

namespace NetHop.Service;

/// <summary>
/// Watches the network, turns changes into <see cref="NetworkEvent"/>s and feeds them to the rules.
/// Changes are debounced: a Wi-Fi join raises a burst of notifications within a few seconds.
/// </summary>
public sealed class NetworkMonitor(CimNetworkBackend backend, NetHopController controller, ILogger<NetworkMonitor> logger)
    : BackgroundService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _changed = new(0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NetworkChange.NetworkAddressChanged += OnChanged;
        NetworkChange.NetworkAvailabilityChanged += OnChanged;
        try
        {
            var previous = await Task.Run(backend.GetSnapshot, stoppingToken);
            await controller.OnNetworkEventsAsync([new NetworkEvent(TriggerType.Startup, DateTimeOffset.Now)], previous, stoppingToken);

            using var minuteTimer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            var tick = minuteTimer.WaitForNextTickAsync(stoppingToken).AsTask();

            while (!stoppingToken.IsCancellationRequested)
            {
                var change = _changed.WaitAsync(stoppingToken);
                var winner = await Task.WhenAny(change, tick);

                if (winner == tick)
                {
                    await tick;
                    tick = minuteTimer.WaitForNextTickAsync(stoppingToken).AsTask();
                    await controller.OnNetworkEventsAsync([new NetworkEvent(TriggerType.Schedule, DateTimeOffset.Now)], previous, stoppingToken);
                    continue;
                }

                await change;
                await Task.Delay(Debounce, stoppingToken);
                while (_changed.CurrentCount > 0)
                    await _changed.WaitAsync(stoppingToken); // drain the burst

                try
                {
                    var current = await Task.Run(backend.GetSnapshot, stoppingToken);
                    var events = NetworkEventDetector.Detect(previous, current, DateTimeOffset.Now);
                    previous = current;
                    if (events.Count > 0)
                    {
                        logger.LogInformation("Network events: {Events}", string.Join(", ", events.Select(e => e.Ssid is null ? e.Type.ToString() : $"{e.Type}({e.Ssid})")));
                        await controller.OnNetworkEventsAsync(events, current, stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Failed to process a network change");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= OnChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnChanged;
        }
    }

    private void OnChanged(object? sender, EventArgs e) => _changed.Release();
}

internal static partial class PowerStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>Null when unknown (desktop PC or API failure).</summary>
    public static bool? IsOnBattery() =>
        GetSystemPowerStatus(out var status) && status.ACLineStatus != 255 ? status.ACLineStatus == 0 : null;
}
