using Microsoft.UI.Xaml;

namespace NetHop.App;

/// <summary>Small functions used from x:Bind expressions.</summary>
public static class XamlHelpers
{
    /// <summary>Segoe Fluent Icons "network" glyph, used when a profile has no icon.</summary>
    public const string DefaultProfileGlyph = "";

    public static string GlyphOrDefault(string? glyph) => string.IsNullOrEmpty(glyph) ? DefaultProfileGlyph : glyph;

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfNotEmpty(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
}
