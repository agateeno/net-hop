using NetHop.Ipc;

namespace Loupedeck.NetHopPlugin;

/// <summary>Base for NetHop commands: redraws whenever the service reports a change.</summary>
public abstract class NetHopCommand : PluginDynamicCommand
{
    protected NetHopCommand(string displayName, string description)
        : base(displayName, description, "NetHop")
    {
    }

    internal ServiceLink Link => ((NetHopPlugin)this.Plugin).Link;

    // The link lives as long as the plugin, so there is nothing to unsubscribe from.
    protected override bool OnLoad()
    {
        this.Link.Changed += this.OnLinkChanged;
        return base.OnLoad();
    }

    // null redraws every button that shows this command, whatever its parameter.
    private void OnLinkChanged(object sender, EventArgs e) => this.ActionImageChanged(null);
}

/// <summary>
/// One button per profile. The profile is chosen in Options+ from a list built at that moment,
/// so new profiles appear without restarting anything.
/// </summary>
public class ApplyProfileCommand : NetHopCommand
{
    public ApplyProfileCommand()
        : base("Apply profile", "Applies a NetHop network profile")
    {
        this.MakeProfileAction("tree");
    }

    protected override PluginProfileActionData GetProfileActionData()
    {
        var tree = new PluginProfileActionTree("Select a NetHop profile");
        tree.AddLevel("Group");
        tree.AddLevel("Profile");

        var node = tree.Root.AddNode("Profiles");
        foreach (var profile in this.Link.Profiles)
            node.AddItem(profile.Id.ToString(), profile.Name, profile.Description);

        return tree;
    }

    protected override void RunCommand(string actionParameter)
    {
        if (!string.IsNullOrEmpty(actionParameter))
            _ = this.Link.ApplyAsync(actionParameter);
    }

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) =>
        this.Link.Find(actionParameter)?.Name ?? "NetHop profile";

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        var profile = this.Link.Find(actionParameter);
        if (profile is null)
            return ButtonImages.Render(imageSize, "?", subtitle: this.Link.State == LinkState.Connected ? "missing" : "offline", color: null, active: false);

        return ButtonImages.Render(imageSize, profile.Name,
            subtitle: this.Link.IsBusy(actionParameter) ? "applying..." : profile.IsActive ? "active" : null,
            color: profile.Color, active: profile.IsActive);
    }
}

/// <summary>Toggles NetHop's automation rules (paused for one hour).</summary>
public class PauseAutomationsCommand : NetHopCommand
{
    public PauseAutomationsCommand()
        : base("Pause automations", "Pauses or resumes NetHop automation rules")
    {
    }

    private bool Paused => this.Link.Status?.AutomationsPausedUntil is not null;

    protected override void RunCommand(string actionParameter) => _ = this.Link.SetAutomationsPausedAsync(!this.Paused);

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) =>
        ButtonImages.Render(imageSize, "Auto", subtitle: this.Paused ? "paused" : "on", color: null, active: !this.Paused);
}
