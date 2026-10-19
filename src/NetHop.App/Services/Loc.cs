using System.Globalization;
using Microsoft.Win32;
using Microsoft.Windows.ApplicationModel.Resources;

namespace NetHop.App.Services;

/// <summary>
/// Strings used from code. XAML uses x:Uid with the same Strings/&lt;language&gt;/Resources.resw files.
/// </summary>
public static class Loc
{
    private static readonly ResourceLoader? Loader = CreateLoader();

    public static string Get(string key)
    {
        try
        {
            var value = Loader?.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception)
        {
            return key;
        }
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    private static ResourceLoader? CreateLoader()
    {
        try
        {
            return new ResourceLoader();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Resources unavailable: {ex.Message}");
            return null;
        }
    }
}

/// <summary>The UI language: Windows' display language unless the user picked one in Settings.</summary>
public static class AppLanguage
{
    public const string SystemDefault = "";

    public static readonly string[] Supported = ["en-US", "it-IT"];

    private const string SettingsKey = @"Software\NetHop";
    private const string ValueName = "Language";

    /// <summary><see cref="SystemDefault"/> ("") for the system language, otherwise a tag from <see cref="Supported"/>.</summary>
    public static string Saved
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            return key?.GetValue(ValueName) is string tag && Supported.Contains(tag) ? tag : SystemDefault;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
            if (string.IsNullOrEmpty(value))
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            else
                key.SetValue(ValueName, value);
        }
    }

    /// <summary>Call before any XAML or resource is loaded. Also aligns .NET's UI culture, used by NetHop.Core messages.</summary>
    public static void Apply()
    {
        var tag = Saved;
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Language override failed: {ex.Message}");
        }

        if (tag.Length > 0)
        {
            var culture = CultureInfo.GetCultureInfo(tag);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
