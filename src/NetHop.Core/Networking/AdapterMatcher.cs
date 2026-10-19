using NetHop.Core.Profiles;

namespace NetHop.Core.Networking;

public enum AdapterMatchKind
{
    NotFound,
    Guid,
    Mac,
    Name,
}

public sealed record AdapterMatchResult(AdapterInfo? Adapter, AdapterMatchKind MatchedBy, string? Problem = null)
{
    public bool Found => Adapter is not null;
}

public static class AdapterMatcher
{
    /// <summary>Resolves a profile's adapter reference: GUID first, then a unique MAC, then a unique name.</summary>
    public static AdapterMatchResult Resolve(AdapterMatch match, IReadOnlyList<AdapterInfo> adapters)
    {
        if (Guid.TryParse(match.Guid, out var id))
        {
            var byId = adapters.FirstOrDefault(a => a.Id == id);
            if (byId is not null)
                return new(byId, AdapterMatchKind.Guid);
        }

        string? problem = null;

        if (MacAddress.TryNormalize(match.Mac, out var mac))
        {
            var byMac = adapters.Where(a => a.Mac == mac).ToList();
            if (byMac.Count == 1)
                return new(byMac[0], AdapterMatchKind.Mac);
            if (byMac.Count > 1)
                problem = $"{byMac.Count} adapters share MAC {mac}.";
        }

        if (!string.IsNullOrWhiteSpace(match.Name))
        {
            var byName = adapters
                .Where(a => string.Equals(a.Name, match.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byName.Count == 1)
                return new(byName[0], AdapterMatchKind.Name);
            if (byName.Count > 1)
                problem = $"{byName.Count} adapters are named '{match.Name}'.";
        }

        return new(null, AdapterMatchKind.NotFound, problem ?? $"No adapter matches '{match}'.");
    }

    /// <summary>Builds the match block NetHop writes when a profile is created from a live adapter.</summary>
    public static AdapterMatch CreateMatch(AdapterInfo adapter) => new()
    {
        Guid = adapter.Id.ToString("B").ToUpperInvariant(),
        Mac = string.IsNullOrEmpty(adapter.Mac) ? null : adapter.Mac,
        Name = adapter.Name,
    };
}
