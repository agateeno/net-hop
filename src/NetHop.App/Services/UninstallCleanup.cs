using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NetHop.App.Services;

/// <summary>
/// Run by the MSI during uninstall (<c>NetHop.App.exe --uninstall</c>, as the user, before files are removed).
/// Always removes the start-with-Windows entry and the Windows 11 context menu package; asks whether to delete
/// %USERPROFILE%\NetHop Profiles too, except with <c>--quiet</c> (silent uninstall), where the folder is kept.
/// </summary>
public static partial class UninstallCleanup
{
    private const uint MbYesNo = 0x04;
    private const uint MbIconQuestion = 0x20;
    private const uint MbDefaultButton2 = 0x100;
    private const uint MbSetForeground = 0x10000;
    private const uint MbTopMost = 0x40000;
    private const int IdYes = 6;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr owner, string text, string caption, uint type);

    public static void Run(bool quiet)
    {
        // Registered per user with the install folder as its content: it would point at deleted files.
        ContextMenuPackage.Remove();

        try
        {
            // The app is going away: a Run entry would point at a missing file.
            ShellIntegration.StartsWithWindows = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not remove the startup entry: {ex.Message}");
        }

        if (quiet || !Directory.Exists(ProfileFolder.Path))
            return;

        // "No" is the default button: deleting files must be a deliberate choice.
        var answer = MessageBox(IntPtr.Zero, Loc.Format("Uninstall_Question", ProfileFolder.Path), Loc.Get("Uninstall_Title"),
            MbYesNo | MbIconQuestion | MbDefaultButton2 | MbSetForeground | MbTopMost);
        if (answer != IdYes)
            return;

        ProfileFolder.Delete();
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\NetHop", throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Run by the MSI as SYSTEM (<c>NetHop.App.exe --uninstall-machine</c>), not on upgrades: removes the service, which
    /// the app registers when it is turned on in Settings, and every user's startup task.
    /// </summary>
    public static int RunForMachine()
    {
        var result = 0;
        foreach (var step in new Action[] { ServiceSetup.Remove, ShellIntegration.RemoveAllStartupTasks })
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                CrashLog.Write("Uninstall", ex);
                result = 1;
            }
        }
        return result;
    }
}
