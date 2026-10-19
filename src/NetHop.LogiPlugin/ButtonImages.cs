using System.Globalization;

namespace Loupedeck.NetHopPlugin;

/// <summary>Draws key images: profile color as background, a bar at the bottom when active.</summary>
internal static class ButtonImages
{
    private static readonly BitmapColor DefaultBackground = new(32, 32, 32);
    private static readonly BitmapColor ActiveBar = new(255, 255, 255);

    public static BitmapImage Render(PluginImageSize imageSize, string title, string subtitle, string color, bool active)
    {
        var size = Dimension(imageSize);
        var background = ParseColor(color) ?? DefaultBackground;

        using var builder = new BitmapBuilder(imageSize);
        builder.Clear(background);
        if (active)
            builder.FillRectangle(0, size - size / 12, size, size / 12, ActiveBar);

        // Font size is left to Logi Plugin Service, which picks the best one per device.
        builder.DrawText(subtitle is null ? title : $"{title}\n{subtitle}");
        return builder.ToImage();
    }

    private static int Dimension(PluginImageSize imageSize) => imageSize switch
    {
        PluginImageSize.Width116Pixels or PluginImageSize.Width116 => 116,
        PluginImageSize.Width90Pixels or PluginImageSize.Width90 => 90,
        _ => 80,
    };

    /// <summary>Profile colors are stored as #RRGGBB; returns null when missing or malformed.</summary>
    private static BitmapColor? ParseColor(string color)
    {
        if (color is not { Length: 7 } || color[0] != '#'
            || !int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return null;
        return new BitmapColor((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }
}
