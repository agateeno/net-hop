using System.Text.Json;
using NetHop.Core.Updates;

namespace NetHop.Core.Tests;

public class ReleaseFeedTests
{
    private const string Json = """
        [
          { "tag_name": "v0.4.0", "draft": true, "prerelease": false },
          { "tag_name": "v0.10.0", "draft": false, "prerelease": true, "published_at": "2026-11-02T10:00:00Z",
            "html_url": "https://example.com/not-nethop" },
          { "tag_name": "nightly", "draft": false, "prerelease": true },
          { "tag_name": "v0.3.0-rc1", "draft": false, "prerelease": true },
          { "tag_name": "v0.9.0", "draft": false, "prerelease": false, "published_at": "2026-10-30T10:00:00Z" },
          { "tag_name": "v0.2.0", "draft": false, "prerelease": true }
        ]
        """;

    [Fact]
    public void Parse_skips_drafts_and_tags_that_are_not_versions()
    {
        var releases = ReleaseFeed.Parse(Json);

        Assert.Equal(new[] { "v0.10.0", "v0.9.0", "v0.2.0" }, releases.Select(r => r.Tag));
        Assert.True(releases[0].IsPrerelease);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), releases[0].PublishedAt);
        Assert.Null(releases[2].PublishedAt);
    }

    [Fact]
    public void Latest_compares_version_numbers()
    {
        var releases = ReleaseFeed.Parse(Json);

        Assert.Equal("v0.10.0", ReleaseFeed.Latest(releases, includePrereleases: true)?.Tag);
        Assert.Equal("v0.9.0", ReleaseFeed.Latest(releases, includePrereleases: false)?.Tag);
    }

    [Fact]
    public void Latest_is_null_without_matching_releases() =>
        Assert.Null(ReleaseFeed.Latest(ReleaseFeed.Parse("""[{ "tag_name": "v0.2.0", "prerelease": true }]"""), includePrereleases: false));

    [Theory]
    [InlineData("0.2.0.0", false)]
    [InlineData("0.2.0", false)]
    [InlineData("0.1.57.0", true)]
    [InlineData("0.3.0.0", false)]
    public void IsNewer_ignores_the_fourth_assembly_version_part(string current, bool expected)
    {
        var release = new AppRelease(new Version(0, 2, 0), "v0.2.0", IsPrerelease: false, PublishedAt: null);

        Assert.Equal(expected, ReleaseFeed.IsNewer(release, Version.Parse(current)));
    }

    [Fact]
    public void Page_url_is_built_from_the_tag_not_taken_from_the_response()
    {
        var release = ReleaseFeed.Parse(Json)[0];

        Assert.Equal("https://github.com/agateeno/net-hop/releases/tag/v0.10.0", release.PageUrl);
    }

    [Theory]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("<html>")]
    public void Parse_rejects_responses_that_are_not_release_lists(string json) =>
        Assert.ThrowsAny<JsonException>(() => ReleaseFeed.Parse(json));
}
