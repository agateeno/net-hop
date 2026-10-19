using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace NetHop.App;

/// <summary>Small functions used from x:Bind expressions.</summary>
public static class XamlHelpers
{
    /// <summary>Segoe Fluent Icons "network" glyph, used when a profile has no icon.</summary>
    public const string DefaultProfileGlyph = "";

    public static string GlyphOrDefault(string? glyph) => string.IsNullOrEmpty(glyph) ? DefaultProfileGlyph : glyph;

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfNotEmpty(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    // A profile with a color shows its icon in white on a badge of that color; without one, a plain icon.

    public static Brush ProfileBrush(string? color) =>
        new SolidColorBrush(TryParseColor(color, out var parsed) ? parsed : Microsoft.UI.Colors.Transparent);

    public static Visibility VisibleIfColor(string? color) => VisibleIf(TryParseColor(color, out _));

    public static Visibility VisibleIfNoColor(string? color) => VisibleIf(!TryParseColor(color, out _));

    /// <summary>Parses "#RRGGBB", the format of <c>Profile.Color</c>.</summary>
    public static bool TryParseColor(string? hex, out Windows.UI.Color color)
    {
        color = default;
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return false;
        color = Microsoft.UI.ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
