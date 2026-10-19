using System.Text.Json;
using Microsoft.Win32;
using NetHop.Core;
using NetHop.Ipc;

namespace NetHop.App.Services;

/// <summary>
/// %USERPROFILE%\NetHop: a folder with the NetHop icon, pinned to Quick Access, holding a copy of every profile
/// as a .nethop file. Double-click opens a profile in the editor, "Apply with NetHop" applies it.
/// The originals stay in the service's protected folder; this copy is rewritten when profiles change.
/// </summary>
public static class ProfileFolder
{
    private const string SettingsKey = @"Software\NetHop";
    private const string IndexFileName = ".nethop-index.json";

    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    public static string Path { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NetHop");

    /// <summary>Creates the folder with its icon, and pins it to Quick Access the first time.</summary>
    public static void EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(Path);
            WriteDesktopIni();
            PinToQuickAccessOnce();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Profile folder setup failed: {ex.Message}");
        }
    }

    /// <summary>Writes one file per profile and removes files of profiles that no longer exist.</summary>
    public static async Task SyncAsync(INetHopService service, IReadOnlyList<ProfileSummary> profiles)
    {
        if (!await SyncLock.WaitAsync(0))
            return; // a sync is already running; the next change triggers another one

        try
        {
            Directory.CreateDirectory(Path);
            var previous = ReadIndex();
            var current = new Dictionary<Guid, string>();

            // Files the user put here themselves are never overwritten.
            var userFiles = Directory.EnumerateFiles(Path, "*" + NetHopConstants.ProfileExtension)
                .Select(f => System.IO.Path.GetFileName(f)!)
                .Where(f => !previous.ContainsValue(f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var profile in profiles)
            {
                var fileName = previous.TryGetValue(profile.Id, out var known) && known == FileNameFor(profile.Name, [])
                    ? known
                    : FileNameFor(profile.Name, current.Values.Concat(userFiles));
                var yaml = await service.GetProfileYamlAsync(profile.Id);
                var target = System.IO.Path.Combine(Path, fileName);
                if (!File.Exists(target) || await File.ReadAllTextAsync(target) != yaml)
                    await File.WriteAllTextAsync(target, yaml);

                current[profile.Id] = fileName;
                // Renamed profile: remove the file with the old name.
                if (previous.TryGetValue(profile.Id, out var oldName) && oldName != fileName)
                    DeleteQuietly(oldName);
            }

            // Deleted profiles. Only files NetHop wrote are touched, never the user's own.
            foreach (var (id, fileName) in previous)
            {
                if (!current.ContainsKey(id) && !current.ContainsValue(fileName))
                    DeleteQuietly(fileName);
            }

            WriteIndex(current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or StreamJsonRpc.RemoteInvocationException or StreamJsonRpc.ConnectionLostException)
        {
            System.Diagnostics.Debug.WriteLine($"Profile folder sync failed: {ex.Message}");
        }
        finally
        {
            SyncLock.Release();
        }
    }

    /// <summary>Unpins the folder from Quick Access and deletes it with everything in it (used on uninstall).</summary>
    public static void Delete()
    {
        if (!Directory.Exists(Path))
            return;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is not null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    shell.Namespace(Path)?.Self.InvokeVerb("unpinfromhome");
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unpinning failed: {ex.Message}");
        }

        // desktop.ini is hidden/system and the folder read-only: clear them, or deleting fails.
        foreach (var entry in new DirectoryInfo(Path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            entry.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
        new DirectoryInfo(Path).Attributes &= ~FileAttributes.ReadOnly;
        Directory.Delete(Path, recursive: true);
    }

    public static void Open()
    {
        EnsureCreated();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Path}\"") { UseShellExecute = true });
    }

    private static void WriteDesktopIni()
    {
        // Explorer reads desktop.ini only in read-only (or system) folders; read-only does not stop writing files.
        var ini = System.IO.Path.Combine(Path, "desktop.ini");
        var content = $"""
            [.ShellClassInfo]
            IconResource={ShellIntegration.IconPath},0
            InfoTip={Loc.Get("Folder_InfoTip")}
            """;
        if (File.Exists(ini))
        {
            if (File.ReadAllText(ini) == content)
                return;
            File.SetAttributes(ini, FileAttributes.Normal);
        }
        File.WriteAllText(ini, content);
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);

        var folder = new DirectoryInfo(Path);
        folder.Attributes |= FileAttributes.ReadOnly;
    }

    private static void PinToQuickAccessOnce()
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        if (key.GetValue("QuickAccessPinned") is not null)
            return;

        // "pintohome" is the verb behind Explorer's "Pin to Quick access".
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
            return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            shell.Namespace(Path).Self.InvokeVerb("pintohome");
            key.SetValue("QuickAccessPinned", 1, RegistryValueKind.DWord);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string FileNameFor(string profileName, IEnumerable<string> taken)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(profileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (clean.Length == 0)
            clean = "profile";

        var name = clean + NetHopConstants.ProfileExtension;
        for (var i = 2; taken.Contains(name, StringComparer.OrdinalIgnoreCase); i++)
            name = $"{clean} ({i}){NetHopConstants.ProfileExtension}";
        return name;
    }

    private static Dictionary<Guid, string> ReadIndex()
    {
        try
        {
            var path = System.IO.Path.Combine(Path, IndexFileName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void WriteIndex(Dictionary<Guid, string> index)
    {
        var path = System.IO.Path.Combine(Path, IndexFileName);
        if (File.Exists(path))
            File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllText(path, JsonSerializer.Serialize(index));
        File.SetAttributes(path, FileAttributes.Hidden);
    }

    private static void DeleteQuietly(string fileName)
    {
        try
        {
            File.Delete(System.IO.Path.Combine(Path, fileName));
        }
        catch (IOException)
        {
        }
    }
}
