using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NetHop.Core.Profiles;

namespace NetHop.App;

/// <summary>Small functions used from x:Bind expressions.</summary>
public static class XamlHelpers
{
    /// <summary>Segoe Fluent Icons "network" glyph, used when a profile has no icon.</summary>
    public const string DefaultProfileGlyph = "";

    private static readonly Dictionary<string, ImageSource> ImageCache = [];

    public static string GlyphOrDefault(string? glyph) =>
        string.IsNullOrEmpty(glyph) || Profile.IsImageIcon(glyph) ? DefaultProfileGlyph : glyph;

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfNotEmpty(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    // A profile with a color shows its icon in white on a badge of that color; without one, a plain icon.

    public static Brush ProfileBrush(string? color) =>
        new SolidColorBrush(TryParseColor(color, out var parsed) ? parsed : Microsoft.UI.Colors.Transparent);

    // The icon is either a glyph (plain, or white on a colored badge) or a custom image;
    // an unreadable image falls back to the default glyph.

    public static Visibility PlainGlyphVisibility(string? icon, string? color) =>
        VisibleIf(ProfileImage(icon) is null && !TryParseColor(color, out _));

    public static Visibility WhiteGlyphVisibility(string? icon, string? color) =>
        VisibleIf(ProfileImage(icon) is null && TryParseColor(color, out _));

    public static Visibility ImageVisibility(string? icon) => VisibleIf(ProfileImage(icon) is not null);

    /// <summary>The custom image of a profile icon (a PNG data URI), or null for a glyph or unreadable data.</summary>
    public static ImageSource? ProfileImage(string? icon)
    {
        if (!Profile.IsImageIcon(icon) || !icon!.StartsWith(Profile.ImageIconPrefix, StringComparison.Ordinal))
            return null;
        if (ImageCache.TryGetValue(icon, out var cached))
            return cached;
        try
        {
            var bytes = Convert.FromBase64String(icon[Profile.ImageIconPrefix.Length..]);
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
                bitmap.SetSource(stream.AsRandomAccessStream());
            if (ImageCache.Count > 64)
                ImageCache.Clear();
            return ImageCache[icon] = bitmap;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

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
