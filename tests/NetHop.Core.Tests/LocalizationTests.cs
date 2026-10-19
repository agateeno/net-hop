using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NetHop.Core.Profiles;

namespace NetHop.Core.Tests;

public partial class LocalizationTests
{
    private static Dictionary<string, string> Load(params string[] relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        var path = Path.Combine([dir!.FullName, "src", .. relativePath]);
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
    }

    // Core messages (validation, plan steps): Strings.<culture>.resx next to the English Strings.resx.
    [Theory]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("pt")]
    public void Core_translations_have_every_key_with_the_same_placeholders(string culture) =>
        AssertSameKeysAndPlaceholders(Load("NetHop.Core", "Strings.resx"), Load("NetHop.Core", $"Strings.{culture}.resx"));

    // App strings: one Resources.resw per culture folder; en-US is the reference.
    [Theory]
    [InlineData("it-IT")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("es-ES")]
    [InlineData("pt-BR")]
    public void App_translations_have_every_key_with_the_same_placeholders(string culture) =>
        AssertSameKeysAndPlaceholders(
            Load("NetHop.App", "Strings", "en-US", "Resources.resw"),
            Load("NetHop.App", "Strings", culture, "Resources.resw"));

    [Theory]
    [InlineData("en-US", "The profile needs a name.")]
    [InlineData("it-IT", "Il profilo deve avere un nome.")]
    [InlineData("fr-FR", "Le profil doit avoir un nom.")]
    [InlineData("de-DE", "Das Profil braucht einen Namen.")]
    [InlineData("es-ES", "El perfil necesita un nombre.")]
    [InlineData("pt-BR", "O perfil precisa de um nome.")] // falls back to the neutral "pt" resources
    public void Messages_follow_the_ui_culture(string culture, string expected)
    {
        var profile = new Profile { Name = "", Adapters = [] };
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(expected, ProfileValidator.Validate(profile).First(i => i.Path == "name").Message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static void AssertSameKeysAndPlaceholders(Dictionary<string, string> english, Dictionary<string, string> translated)
    {
        Assert.Equal(english.Keys.Order(), translated.Keys.Order());
        foreach (var (key, value) in english)
            Assert.True(Placeholders(value).SetEquals(Placeholders(translated[key])), $"Placeholders differ for {key}");
    }

    // Placeholder indexes ({0}, {1:t}...): a translation may reorder them but not drop or add any.
    private static HashSet<string> Placeholders(string text) =>
        PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value).ToHashSet();

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex PlaceholderRegex();
}
