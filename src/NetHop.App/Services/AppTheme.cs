using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace NetHop.App.Services;

/// <summary>
/// Light or dark: Windows' app mode unless the user picked one in Settings. Applied live to every open window
/// (content, Mica/acrylic backdrop and title bar buttons).
/// </summary>
public static class AppTheme
{
    private const string SettingsKey = @"Software\NetHop";
    private const string ValueName = "Theme";

    private static event EventHandler? Changed;

    /// <summary><see cref="ElementTheme.Default"/> follows Windows.</summary>
    public static ElementTheme Saved
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            return key?.GetValue(ValueName) is string name && Enum.TryParse<ElementTheme>(name, out var theme) ? theme : ElementTheme.Default;
        }
        set
        {
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
            {
                if (value == ElementTheme.Default)
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                else
                    key.SetValue(ValueName, value.ToString());
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Applies the theme to the window now and whenever it changes, until the window is closed.</summary>
    public static void Track(Window window)
    {
        Apply(window);
        EventHandler handler = (_, _) => Apply(window);
        Changed += handler;
        window.Closed += (_, _) => Changed -= handler;
    }

    private static void Apply(Window window)
    {
        var theme = Saved;
        // The backdrop follows the theme of the window's root element.
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = theme;

        window.AppWindow.TitleBar.PreferredTheme = theme switch
        {
            ElementTheme.Light => TitleBarTheme.Light,
            ElementTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }
}
