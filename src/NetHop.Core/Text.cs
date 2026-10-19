using System.Globalization;
using System.Resources;

namespace NetHop.Core;

/// <summary>
/// Localized user-facing messages (Strings.resx in English, Strings.it.resx in Italian),
/// in the current UI culture: the app's language for local validation, the system language in the service.
/// </summary>
internal static class Text
{
    private static readonly ResourceManager Resources = new("NetHop.Core.Strings", typeof(Text).Assembly);

    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
