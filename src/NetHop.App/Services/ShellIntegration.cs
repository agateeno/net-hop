using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using NetHop.Core;

namespace NetHop.App.Services;

/// <summary>
/// Registers the <c>.nethop</c> file type and the <c>nethop://</c> protocol for the current user,
/// and the optional start with Windows: a Run entry with the service, and without it a scheduled task, the only way
/// to start an elevated app at sign-in without a UAC prompt.
/// </summary>
public static class ShellIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "NetHop";
    private const string SettingsKey = @"Software\NetHop";

    /// <summary>One task per user, named after the account, so that every administrator can have their own.</summary>
    private const string TaskPrefix = "NetHop Startup ";

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

        try
        {
            StartsWithWindows = true;
        }
        catch (UnauthorizedAccessException)
        {
            return; // no service and not elevated (UAC prompt cancelled): next time
        }
        key.SetValue("FirstRunDone", 1, RegistryValueKind.DWord);
    }

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string || Schtasks("/Query", "/TN", TaskName) == 0;
        }
        set
        {
            // Without the service the app runs elevated, which a Run entry cannot start without a UAC prompt.
            var task = value && !ServiceSetup.IsInstalled;
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (value && !task)
                    key.SetValue(RunValue, $"\"{ExePath}\" --tray");
                else
                    key.DeleteValue(RunValue, throwOnMissingValue: false);
            }

            if (task)
                CreateTask();
            else if (Schtasks("/Query", "/TN", TaskName) == 0 && Schtasks("/Delete", "/TN", TaskName, "/F") != 0)
                throw new UnauthorizedAccessException(Loc.Get("Settings_StartupTaskDenied"));
        }
    }

    /// <summary>
    /// Opens a web page in the browser. Without the service NetHop runs elevated, and the browser would be too:
    /// Explorer, which runs as the signed-in user, opens it instead.
    /// </summary>
    public static void OpenUrl(string url)
    {
        if (ServiceSetup.IsElevated)
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{url}\""));
        else
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>After the service is turned on or off: start the same way, with the right mechanism.</summary>
    public static void UpdateStartupMechanism()
    {
        try
        {
            if (StartsWithWindows)
                StartsWithWindows = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not update the startup entry: {ex.Message}");
        }
    }

    /// <summary>Removes every user's startup task. Run as SYSTEM by the MSI when NetHop is uninstalled.</summary>
    public static void RemoveAllStartupTasks()
    {
        var list = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/Query /FO CSV /NH")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(list)!;
        var names = process.StandardOutput.ReadToEnd()
            .Split('\n')
            .Select(line => line.Split(',')[0].Trim().Trim('"'))
            .Where(name => name.StartsWith("\\" + TaskPrefix, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
        process.WaitForExit();
        foreach (var name in names)
            Schtasks("/Delete", "/TN", name, "/F");
    }

    private static string UserSid => WindowsIdentity.GetCurrent().User!.Value;

    private static string TaskName => TaskPrefix + UserSid;

    private static void CreateTask()
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts NetHop in the notification area when you sign in.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{UserSid}</UserId></LogonTrigger></Triggers>
              <Principals>
                <Principal id="Author"><UserId>{UserSid}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>"{SecurityElement.Escape(ExePath)}"</Command><Arguments>--tray</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"nethop-startup-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            if (Schtasks("/Create", "/TN", TaskName, "/XML", file, "/F") != 0)
                throw new UnauthorizedAccessException(Loc.Get("Settings_StartupTaskDenied"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static int Schtasks(params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
