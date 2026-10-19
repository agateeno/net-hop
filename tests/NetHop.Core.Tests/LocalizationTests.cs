using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NetHop.Core.Profiles;

namespace NetHop.Core.Tests;

public partial class LocalizationTests
{
    private static Dictionary<string, string> Load(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "src", "NetHop.Core", file);
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
    }

    [Fact]
    public void Italian_has_every_key_with_the_same_placeholders()
    {
        var english = Load("Strings.resx");
        var italian = Load("Strings.it.resx");

        Assert.Equal(english.Keys.Order(), italian.Keys.Order());
        foreach (var (key, value) in english)
            Assert.True(Placeholders(value).SetEquals(Placeholders(italian[key])), $"Placeholders differ for {key}");
    }

    [Fact]
    public void Messages_follow_the_ui_culture()
    {
        var profile = new Profile { Name = "" , Adapters = [] };
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("it-IT");
            var italian = ProfileValidator.Validate(profile).First(i => i.Path == "name").Message;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var english = ProfileValidator.Validate(profile).First(i => i.Path == "name").Message;

            Assert.Equal("Il profilo deve avere un nome.", italian);
            Assert.Equal("The profile needs a name.", english);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderRegex().Matches(text).Select(m => m.Value).ToHashSet();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderRegex();
}
