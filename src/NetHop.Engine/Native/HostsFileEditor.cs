using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using NetHop.Core.Networking;

namespace NetHop.Engine.Native;

/// <summary>
/// Reads and rewrites NetHop's block of the Windows hosts file (<see cref="HostsFile"/>). Everything outside the block
/// is kept byte for byte, and the file is replaced atomically, keeping its owner, permissions and attributes.
/// </summary>
internal static partial class HostsFileEditor
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static string FilePath => Path.Combine(DatabaseFolder(), "hosts");

    // In the service's data folder, which only administrators and SYSTEM can open (see ServicePaths).
    private static string BackupPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetHop", "hosts.backup");

    /// <summary>Null when the file cannot be read, or NetHop's block in it is broken.</summary>
    public static HostsSnapshot? Read()
    {
        try
        {
            var path = FilePath;
            var content = File.Exists(path) ? Decode(File.ReadAllBytes(path)).Text : "";
            return new HostsSnapshot { Mappings = HostsFile.ReadBlock(content) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    public static void Write(IReadOnlyList<HostsMapping> mappings)
    {
        var path = FilePath;
        var info = new FileInfo(path);
        var exists = info.Exists;
        if (exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException($"{path} is a link, not the hosts file itself: NetHop does not change it.");
        // A read-only hosts file is a deliberate lock: respected, not worked around.
        if (exists && info.IsReadOnly)
            throw new InvalidOperationException($"{path} is read-only. Remove the read-only attribute to let NetHop change it.");

        var (content, encoding) = exists ? Decode(File.ReadAllBytes(path)) : ("", Encoding.Latin1);
        var updated = HostsFile.WriteBlock(content, mappings);
        if (updated == content)
            return;

        // The file as it was before NetHop first changed it. Skipped if the data folder is missing rather than
        // creating it here without the protection the service gives it.
        if (exists && Directory.Exists(Path.GetDirectoryName(BackupPath)) && !File.Exists(BackupPath))
            File.Copy(path, BackupPath);

        var temp = path + ".nethop.tmp";
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(updated)];
        File.WriteAllBytes(temp, bytes);
        try
        {
            if (exists)
                File.Replace(temp, path, destinationBackupFileName: null); // ReplaceFile keeps the original's owner, DACL and attributes
            else
                File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }

        // Cached lookups would otherwise keep the old answers for a while.
        DnsFlushResolverCache();
    }

    /// <summary>
    /// Text and encoding of the file. Without a byte order mark it is read as Latin-1, which maps every byte to one
    /// character and back: comments in any code page (or UTF-8) survive the rewrite unchanged.
    /// </summary>
    private static (string Text, Encoding Encoding) Decode(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), Utf8WithBom);
        if (bytes is [0xFF, 0xFE, ..])
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        return (Encoding.Latin1.GetString(bytes), Encoding.Latin1);
    }

    /// <summary>Where Windows reads the hosts file from: usually %SystemRoot%\System32\drivers\etc.</summary>
    private static string DatabaseFolder() =>
        Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "DataBasePath", null) is string folder
        && !string.IsNullOrWhiteSpace(folder)
            ? Environment.ExpandEnvironmentVariables(folder)
            : Path.Combine(Environment.SystemDirectory, "drivers", "etc");

    [LibraryImport("dnsapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DnsFlushResolverCache();
}
