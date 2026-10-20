using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using NetHop.Core.Updates;

namespace NetHop.App.Services;

public enum UpdateState
{
    NotChecked,
    Checking,
    UpToDate,
    Available,
    Failed,
}

/// <summary>
/// Looks for a newer NetHop release on GitHub: when the app starts, then once a day, or from Settings. It only
/// reports it: the release page opens in the browser, and nothing is downloaded or installed (builds are not
/// signed yet). The app does this, never the service, which has no reason to reach the internet.
/// </summary>
public static class UpdateCheck
{
    private const string SettingsKey = @"Software\NetHop";
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    // Before Http, which uses it.
    public static Version CurrentVersion { get; } = ReleaseFeed.Normalize(typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0, 0));

    private static readonly HttpClient Http = CreateClient();
    private static IReadOnlyList<AppRelease>? _releases;

    /// <summary>Raised on the UI thread whenever the state or the latest release changes.</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// Local builds and CI builds that are not releases (0.1.&lt;run&gt;): they show the latest release, but never offer
    /// it as an update, since their version number says nothing about how recent they are.
    /// </summary>
    public static bool IsDevelopmentBuild { get; } = typeof(UpdateCheck).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "NetHopBuildKind")?.Value != "release";

    public static UpdateState State { get; private set; }

    public static AppRelease? Latest { get; private set; }

    public static DateTimeOffset? LastChecked { get; private set; }

    public static string? Error { get; private set; }

    public static bool CheckAutomatically
    {
        get => ReadFlag("CheckForUpdates", defaultValue: true);
        set => WriteFlag("CheckForUpdates", value);
    }

    /// <summary>On by default while NetHop is in preview, as all its releases are pre-releases.</summary>
    public static bool IncludePrereleases
    {
        get => ReadFlag("IncludePrereleases", defaultValue: true);
        set
        {
            WriteFlag("IncludePrereleases", value);
            if (_releases is not null)
                Evaluate(notify: false);
        }
    }

    /// <summary>Starts the automatic checks. Call on the UI thread: every await resumes there.</summary>
    public static void Start() => _ = RunScheduleAsync();

    public static async Task CheckAsync(bool notify = false)
    {
        if (State == UpdateState.Checking)
            return;
        SetState(UpdateState.Checking);
        try
        {
            _releases = ReleaseFeed.Parse(await Http.GetStringAsync(ReleaseFeed.ApiUrl));
            LastChecked = DateTimeOffset.Now;
            Error = null;
            Evaluate(notify);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Error = ex.Message;
            SetState(UpdateState.Failed);
        }
    }

    public static void OpenReleasePage()
    {
        if (Latest is not null)
            Process.Start(new ProcessStartInfo(Latest.PageUrl) { UseShellExecute = true });
    }

    private static async Task RunScheduleAsync()
    {
        // Let the start-up (service connection, tray) go first.
        await Task.Delay(TimeSpan.FromSeconds(30));
        // Hourly, so a check that failed (offline at sign-in) is retried soon, and sleep does not delay the daily one.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            if (CheckAutomatically && (LastChecked is not { } last || DateTimeOffset.Now - last >= Interval))
                await CheckAsync(notify: true);
        }
        while (await timer.WaitForNextTickAsync());
    }

    private static void Evaluate(bool notify)
    {
        Latest = ReleaseFeed.Latest(_releases!, IncludePrereleases);
        var available = Latest is not null && !IsDevelopmentBuild && ReleaseFeed.IsNewer(Latest, CurrentVersion);
        SetState(available ? UpdateState.Available : UpdateState.UpToDate);

        // Once per version, not at every start or daily check.
        if (available && notify && ReadString("UpdateNotified") != Latest!.Tag)
        {
            WriteString("UpdateNotified", Latest.Tag);
            UserActionRunner.Notify(Loc.Format("Settings_UpdateAvailable", Latest.Version), Loc.Get("Toast_UpdateAvailable"), openSettings: true);
        }
    }

    private static void SetState(UpdateState state)
    {
        State = state;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
        // GitHub's API refuses requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NetHop", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static bool ReadFlag(string name, bool defaultValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return key?.GetValue(name) is int value ? value != 0 : defaultValue;
    }

    private static void WriteFlag(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }

    private static string? ReadString(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return key?.GetValue(name) as string;
    }

    private static void WriteString(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key.SetValue(name, value);
    }
}
