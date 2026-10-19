using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NetHop.Core.Networking;

public static class MacAddress
{
    /// <summary>
    /// Normalizes <c>00:1a:2b:3c:4d:5e</c>, <c>00-1A-2B-3C-4D-5E</c> or <c>001A2B3C4D5E</c>
    /// to the Windows form <c>00-1A-2B-3C-4D-5E</c>.
    /// </summary>
    public static bool TryNormalize([NotNullWhen(true)] string? text, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var hex = text.Replace("-", "").Replace(":", "").Replace(".", "").Trim();
        if (hex.Length != 12)
            return false;

        var bytes = new string[6];
        for (var i = 0; i < 6; i++)
        {
            var pair = hex.Substring(i * 2, 2);
            if (!byte.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                return false;
            bytes[i] = pair.ToUpperInvariant();
        }

        normalized = string.Join('-', bytes);
        return true;
    }

    public static bool AreEqual(string? a, string? b) =>
        TryNormalize(a, out var na) && TryNormalize(b, out var nb) && na == nb;
}
