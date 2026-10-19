namespace Loupedeck.NetHopPlugin;

/// <summary>
/// Universal plugin (not tied to an application) that talks to the NetHop service over its named pipe.
/// The plugin status in Options+ shows when the service is not running or the account is not an administrator.
/// </summary>
public class NetHopPlugin : Plugin
{
    public NetHopPlugin()
    {
        PluginLog.Init(this.Log);
    }

    public override bool UsesApplicationApiOnly => true;

    public override bool HasNoApplication => true;

    internal ServiceLink Link { get; } = new();

    public override void Load()
    {
        this.Link.Changed += this.OnLinkChanged;
        this.Link.Start();
        this.UpdateStatus();
    }

    public override void Unload()
    {
        this.Link.Changed -= this.OnLinkChanged;
        this.Link.Dispose();
    }

    private void OnLinkChanged(object sender, EventArgs e) => this.UpdateStatus();

    private void UpdateStatus()
    {
        switch (this.Link.State)
        {
            case LinkState.Connected:
                this.OnPluginStatusChanged(PluginStatus.Normal, null);
                break;
            case LinkState.ServiceNotRunning:
                this.OnPluginStatusChanged(PluginStatus.Error, "The NetHop service is not running.",
                    "https://github.com/agateeno/net-hop#building", "How to install NetHop");
                break;
            case LinkState.AccessDenied:
                this.OnPluginStatusChanged(PluginStatus.Error, "NetHop requires an administrator account.");
                break;
            default:
                this.OnPluginStatusChanged(PluginStatus.Warning, "Connecting to the NetHop service...");
                break;
        }
    }
}

internal static class PluginLog
{
    private static PluginLogFile _log;

    public static void Init(PluginLogFile log) => _log = log;

    public static void Info(string text) => _log?.Info(text);

    public static void Warning(Exception ex, string text) => _log?.Warning(ex, text);
}
