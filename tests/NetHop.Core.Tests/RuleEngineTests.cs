using NetHop.Core.Profiles;
using NetHop.Core.Rules;
using static NetHop.Core.Tests.TestData;

namespace NetHop.Core.Tests;

public class RuleEngineTests
{
    // A Wednesday at 09:00 local time.
    private static readonly DateTimeOffset Morning = new(2026, 10, 21, 9, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 21, 9, 0, 0)));

    private static RuleContext Context(string? activeProfile = null) =>
        new() { Network = Snapshot(Up(Ethernet), Up(WiFi)), ActiveProfile = activeProfile };

    private static NetworkEvent Wifi(string ssid, DateTimeOffset at) =>
        new(TriggerType.WifiConnected, at) { Ssid = ssid };

    private static Rule ApplyOn(string ssid, string profile, int priority = 0) => new()
    {
        Name = $"{ssid} -> {profile}",
        Priority = priority,
        Trigger = new Trigger { Type = TriggerType.WifiConnected, Ssid = ssid },
        Actions = [new RuleAction { Type = RuleActionType.ApplyProfile, Profile = profile }],
    };

    [Fact]
    public void Sample_rules_load_without_errors()
    {
        var set = RuleSetSerializer.Deserialize(File.ReadAllText(SamplePath("rules.nethop.yml")));

        Assert.Equal(2, set.Rules.Count);
        Assert.Empty(RuleSetSerializer.Validate(set));
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday], set.Rules[0].Conditions[1].Days);
    }

    [Fact]
    public void Sample_office_rule_fires_on_a_weekday_morning()
    {
        var set = RuleSetSerializer.Deserialize(File.ReadAllText(SamplePath("rules.nethop.yml")));
        var engine = new RuleEngine(set);

        var result = engine.Evaluate(Wifi("CORP-WiFi", Morning), Context());

        var match = Assert.Single(result.Matches);
        Assert.Equal("Office", match.Actions[0].Profile);
    }

    [Theory]
    [InlineData("Casa*", "Casa-5G", true)]
    [InlineData("Casa*", "casa-5G", false)] // SSIDs are case-sensitive
    [InlineData("CORP", "CORP-Guest", false)]
    [InlineData(null, "anything", true)]
    public void Ssid_patterns(string? pattern, string ssid, bool expected) =>
        Assert.Equal(expected, RuleEngine.SsidMatches(pattern, ssid));

    [Theory]
    [InlineData("08:00", "18:00", "09:00", true)]
    [InlineData("08:00", "18:00", "18:00", false)]
    [InlineData("22:00", "06:00", "23:30", true)]
    [InlineData("22:00", "06:00", "05:59", true)]
    [InlineData("22:00", "06:00", "12:00", false)]
    public void Time_ranges_wrap_past_midnight(string from, string to, string now, bool expected) =>
        Assert.Equal(expected, RuleEngine.IsTimeBetween(TimeOnly.Parse(now), from, to));

    [Fact]
    public void Only_the_highest_priority_rule_applies_a_profile()
    {
        var engine = new RuleEngine(new RuleSet { Rules = [ApplyOn("CORP", "Low", 1), ApplyOn("CORP", "High", 5)] });

        var result = engine.Evaluate(Wifi("CORP", Morning), Context());

        Assert.Equal(2, result.Matches.Count);
        Assert.Equal("High", result.Matches[0].Actions.Single().Profile);
        Assert.Empty(result.Matches[1].Actions);
    }

    [Fact]
    public void Events_right_after_a_profile_change_are_suppressed()
    {
        var engine = new RuleEngine(new RuleSet { Rules = [ApplyOn("CORP", "Office")] });
        engine.NotifyProfileApplied(Morning);

        var tooSoon = engine.Evaluate(Wifi("CORP", Morning.AddSeconds(3)), Context());
        var later = engine.Evaluate(Wifi("CORP", Morning.AddSeconds(30)), Context());

        Assert.Empty(tooSoon.Matches);
        Assert.Single(later.Matches);
    }

    [Fact]
    public void Pause_blocks_everything_until_it_expires()
    {
        var engine = new RuleEngine(new RuleSet { Rules = [ApplyOn("CORP", "Office")] });
        engine.Pause(Morning.AddHours(1));

        Assert.Empty(engine.Evaluate(Wifi("CORP", Morning.AddMinutes(10)), Context()).Matches);
        Assert.Single(engine.Evaluate(Wifi("CORP", Morning.AddHours(2)), Context()).Matches);
    }

    [Fact]
    public void Cooldown_prevents_repeated_runs()
    {
        var rule = ApplyOn("CORP", "Office");
        rule.CooldownSeconds = 300;
        var engine = new RuleEngine(new RuleSet { Rules = [rule] });

        Assert.Single(engine.Evaluate(Wifi("CORP", Morning), Context()).Matches);
        Assert.Empty(engine.Evaluate(Wifi("CORP", Morning.AddMinutes(1)), Context()).Matches);
        Assert.Single(engine.Evaluate(Wifi("CORP", Morning.AddMinutes(6)), Context()).Matches);
    }

    [Fact]
    public void Conditions_on_active_profile_and_adapter()
    {
        var rule = ApplyOn("CORP", "Office");
        rule.Conditions =
        [
            new Condition { Type = ConditionType.NotActiveProfile, Profile = "Office" },
            new Condition { Type = ConditionType.AdapterConnected, Adapter = new AdapterMatch { Name = "Ethernet" } },
        ];
        var engine = new RuleEngine(new RuleSet { Rules = [rule] });

        Assert.Empty(engine.Evaluate(Wifi("CORP", Morning), Context(activeProfile: "office")).Matches);
        Assert.Single(engine.Evaluate(Wifi("CORP", Morning), Context(activeProfile: "Home")).Matches);
    }

    [Fact]
    public void Invalid_rules_are_reported()
    {
        var set = new RuleSet
        {
            Rules =
            [
                new Rule
                {
                    Name = "",
                    Trigger = new Trigger { Type = TriggerType.Schedule, At = "25:00" },
                    Actions = [new RuleAction { Type = RuleActionType.RunPowerAutomateFlow, Url = "https://example.com" }],
                },
            ],
        };

        var paths = RuleSetSerializer.Validate(set).Select(i => i.Path).ToList();

        Assert.Contains("rules[0].name", paths);
        Assert.Contains("rules[0].trigger.at", paths);
        Assert.Contains("rules[0].actions[0].url", paths);
    }

    [Theory]
    [InlineData("https://intranet.example.com/status", true)]
    [InlineData("http://192.168.1.1", true)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("C:\\Windows\\System32\\calc.exe", false)]
    [InlineData("ms-settings:network", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Open_link_accepts_only_web_addresses(string url, bool valid)
    {
        var set = new RuleSet
        {
            Rules =
            [
                new Rule
                {
                    Name = "Link",
                    Trigger = new Trigger { Type = TriggerType.NetworkChanged },
                    Actions = [new RuleAction { Type = RuleActionType.OpenUrl, Url = url }],
                },
            ],
        };

        Assert.Equal(valid, !RuleSetSerializer.Validate(set).Any(i => i.Path == "rules[0].actions[0].url"));
    }
}
