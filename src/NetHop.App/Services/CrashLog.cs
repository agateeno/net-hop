namespace NetHop.App.Services;

/// <summary>
/// Writes unhandled exceptions to %LOCALAPPDATA%\NetHop\crash.log. A XAML failure otherwise only shows up in
/// Event Viewer as 0xc000027b in Microsoft.UI.Xaml.dll, without the actual error.
/// </summary>
public static class CrashLog
{
    private const long MaxSize = 512 * 1024;

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetHop", "crash.log");

    public static void Write(string source, object? exception)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (File.Exists(Path) && new FileInfo(Path).Length > MaxSize)
                File.Delete(Path);

            var version = typeof(CrashLog).Assembly.GetName().Version;
            File.AppendAllText(Path, $"[{DateTime.Now:O}] NetHop {version} - {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nothing else to do while the process is going down.
        }
    }
}
