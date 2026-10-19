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

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");

    private static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "NetHop.ico");

    public static void Register()
    {
        try
        {
            // Unpackaged apps register through the Windows App SDK; activations then arrive as File/Protocol kinds.
            ActivationRegistrationManager.RegisterForFileTypeActivation(
                [NetHopConstants.ProfileExtension], IconPath, "NetHop profile", ["open"], ExePath);
            ActivationRegistrationManager.RegisterForProtocolActivation(
                NetHopConstants.UriScheme, IconPath, "NetHop", ExePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shell registration failed: {ex}");
        }
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
