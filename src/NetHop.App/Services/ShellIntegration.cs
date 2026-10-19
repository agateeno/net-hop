using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using NetHop.Core;

namespace NetHop.App.Services;

/// <summary>
/// Registers the <c>.nethop</c> file type and the <c>nethop://</c> protocol for the current user,
/// and the optional start with Windows.
/// </summary>
public static class ShellIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "NetHop";
    private const string SettingsKey = @"Software\NetHop";

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");

    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "NetHop.ico");

    private static string FileIconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "NetHopFile.ico");

    public static void Register()
    {
        // The MSI registers .nethop and nethop:// for all users; a per-user copy would outlive an uninstall.
        if (IsInstalledByMsi)
            return;

        try
        {
            // Unpackaged apps register through the Windows App SDK; activations then arrive as File/Protocol kinds.
            ActivationRegistrationManager.RegisterForFileTypeActivation(
                [NetHopConstants.ProfileExtension], FileIconPath, "NetHop profile", ["open"], ExePath);
            ActivationRegistrationManager.RegisterForProtocolActivation(
                NetHopConstants.UriScheme, IconPath, "NetHop", ExePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shell registration failed: {ex}");
        }
    }

    private static bool IsInstalledByMsi
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(SettingsKey);
            return key?.GetValue("InstallFolder") is string folder
                && Path.GetFullPath(folder).TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>First launch for this user: start with Windows by default, like PowerToys. The user can turn it off in Settings.</summary>
    public static void ApplyFirstRunDefaults()
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        if (key.GetValue("FirstRunDone") is not null)
            return;

        StartsWithWindows = true;
        key.SetValue("FirstRunDone", 1, RegistryValueKind.DWord);
    }

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, $"\"{ExePath}\" --tray");
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}
