using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetHop.Core.Updates;

/// <summary>A published NetHop release, as listed by GitHub.</summary>
public sealed record AppRelease(Version Version, string Tag, bool IsPrerelease, DateTimeOffset? PublishedAt)
{
    /// <summary>
    /// The release page. Built from the tag rather than taken from the response, so NetHop only ever opens its own
    /// releases on github.com.
    /// </summary>
    public string PageUrl => $"{NetHopConstants.RepositoryUrl}/releases/tag/{Uri.EscapeDataString(Tag)}";
}

/// <summary>Reads GitHub's list of NetHop releases and finds the newest one.</summary>
public static partial class ReleaseFeed
{
    public const string ApiUrl = "https://api.github.com/repos/agateeno/net-hop/releases?per_page=30";

    /// <summary>
    /// Parses GitHub's "list releases" response. Drafts are skipped, and so are tags other than vX.Y.Z: CI builds
    /// only those as releases.
    /// </summary>
    /// <exception cref="JsonException">The response is not a list of releases.</exception>
    public static IReadOnlyList<AppRelease> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected a list of releases.");

        var releases = new List<AppRelease>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || IsTrue(item, "draft"))
                continue;
            if (!item.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
                || !TryParseTag(tag.GetString()!, out var version))
                continue;

            DateTimeOffset? published = item.TryGetProperty("published_at", out var date) && date.ValueKind == JsonValueKind.String
                && date.TryGetDateTimeOffset(out var value) ? value : null;
            releases.Add(new AppRelease(version, tag.GetString()!, IsTrue(item, "prerelease"), published));
        }
        return releases;
    }

    /// <summary>The highest version, by number rather than by date.</summary>
    public static AppRelease? Latest(IEnumerable<AppRelease> releases, bool includePrereleases) =>
        releases.Where(r => includePrereleases || !r.IsPrerelease).MaxBy(r => r.Version);

    public static bool IsNewer(AppRelease release, Version current) => release.Version > Normalize(current);

    /// <summary>Three parts, as tags have: assembly versions carry a fourth (0.2.0.0).</summary>
    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    public static bool TryParseTag(string tag, [NotNullWhen(true)] out Version? version)
    {
        var match = TagPattern().Match(tag);
        version = match.Success && Version.TryParse(match.Groups[1].Value, out var parsed) ? parsed : null;
        return version is not null;
    }

    private static bool IsTrue(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^v(\d{1,9}\.\d{1,9}\.\d{1,9})$")]
    private static partial Regex TagPattern();
}
