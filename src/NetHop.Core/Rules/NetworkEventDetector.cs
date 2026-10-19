using NetHop.Core.Networking;

namespace NetHop.Core.Rules;

/// <summary>Compares two snapshots and derives the events that rules can trigger on.</summary>
public static class NetworkEventDetector
{
    public static IReadOnlyList<NetworkEvent> Detect(NetworkSnapshot? before, NetworkSnapshot after, DateTimeOffset now)
    {
        var events = new List<NetworkEvent>();
        if (before is null)
            return events;

        foreach (var current in after.Adapters)
        {
            var previous = before.Find(current.Info.Id);
            var wasConnected = previous?.Connected == true;
            var previousSsid = wasConnected ? previous!.Ssid : null;
            var currentSsid = current.Connected ? current.Ssid : null;

            if (previousSsid is not null && previousSsid != currentSsid)
                events.Add(new NetworkEvent(TriggerType.WifiDisconnected, now) { Adapter = current.Info, Ssid = previousSsid });

            if (wasConnected && !current.Connected)
                events.Add(new NetworkEvent(TriggerType.AdapterDisconnected, now) { Adapter = current.Info });

            if (!wasConnected && current.Connected)
                events.Add(new NetworkEvent(TriggerType.AdapterConnected, now) { Adapter = current.Info });

            if (currentSsid is not null && currentSsid != previousSsid)
                events.Add(new NetworkEvent(TriggerType.WifiConnected, now) { Adapter = current.Info, Ssid = currentSsid });
        }

        // An adapter that vanished (USB NIC unplugged, dock removed) counts as disconnected.
        foreach (var gone in before.Adapters.Where(b => after.Find(b.Info.Id) is null && b.Connected))
        {
            if (gone.Ssid is not null)
                events.Add(new NetworkEvent(TriggerType.WifiDisconnected, now) { Adapter = gone.Info, Ssid = gone.Ssid });
            events.Add(new NetworkEvent(TriggerType.AdapterDisconnected, now) { Adapter = gone.Info });
        }

        if (events.Count > 0 || !SameAddresses(before, after))
            events.Add(new NetworkEvent(TriggerType.NetworkChanged, now));

        return events;
    }

    private static bool SameAddresses(NetworkSnapshot before, NetworkSnapshot after)
    {
        static IEnumerable<string> Flatten(NetworkSnapshot s) => s.Adapters
            .SelectMany(a => (a.Ipv4?.AllAddresses ?? []).Concat(a.Ipv6?.AllAddresses ?? []).Select(x => $"{a.Info.Id}|{x}"))
            .Order();

        return Flatten(before).SequenceEqual(Flatten(after));
    }
}
