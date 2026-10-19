using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.PowerPlatform.PowerAutomate.Desktop.Actions.SDK;
using Microsoft.PowerPlatform.PowerAutomate.Desktop.Actions.SDK.Attributes;

namespace Modules.NetHop
{
    [Action(Id = "ApplyProfile", Order = 1, FriendlyName = "Apply network profile",
        Description = "Applies a NetHop profile (adapters, IP addresses, gateways and DNS)")]
    [Throws(NetHopCli.FailedError)]
    [Throws(NetHopCli.ServiceUnavailableError)]
    [Throws(NetHopCli.AccessDeniedError)]
    [Throws(NetHopCli.NotInstalledError)]
    public class ApplyProfile : ActionBase
    {
        [InputArgument(Order = 1, FriendlyName = "Profile", Description = "Name or id of the profile to apply")]
        public string Profile { get; set; }

        [InputArgument(Order = 2, FriendlyName = "Preview only", Description = "List the changes without making them"), DefaultValue(false)]
        public bool DryRun { get; set; }

        [OutputArgument(FriendlyName = "Changes", Description = "The changes made (or planned), one per line")]
        public string Changes { get; set; }

        [OutputArgument(FriendlyName = "Warnings", Description = "Warnings from the profile check, one per line")]
        public string Warnings { get; set; }

        public override void Execute(ActionContext context)
        {
            var arguments = DryRun ? new[] { "apply", Profile, "--dry-run" } : new[] { "apply", Profile };
            var result = NetHopCli.Run<ApplyResultJson>(arguments);
            if (!result.Success)
            {
                var restored = result.RolledBack ? " The previous configuration was restored." : "";
                throw new ActionException(NetHopCli.FailedError, $"Profile '{Profile}' failed: {result.Error}{restored}");
            }

            Changes = string.Join(Environment.NewLine, result.Steps ?? new string[0]);
            Warnings = string.Join(Environment.NewLine, (result.Issues ?? new IssueJson[0]).Select(i => $"{i.Path}: {i.Message}"));
        }
    }

    [Action(Id = "GetStatus", Order = 3, FriendlyName = "Get network profile status",
        Description = "Reads the active NetHop profile and the automation state")]
    [Throws(NetHopCli.FailedError)]
    [Throws(NetHopCli.ServiceUnavailableError)]
    [Throws(NetHopCli.AccessDeniedError)]
    [Throws(NetHopCli.NotInstalledError)]
    public class GetStatus : ActionBase
    {
        [OutputArgument(FriendlyName = "Active profile", Description = "Empty when the network matches no profile")]
        public string ActiveProfile { get; set; }

        [OutputArgument(FriendlyName = "Changed by hand", Description = "The network was changed after the last profile was applied")]
        public bool Drifted { get; set; }

        [OutputArgument(FriendlyName = "Automations paused")]
        public bool AutomationsPaused { get; set; }

        public override void Execute(ActionContext context)
        {
            var status = NetHopCli.Run<StatusJson>("status");
            ActiveProfile = status.ActiveProfileName ?? "";
            Drifted = status.ActiveProfileDrifted;
            AutomationsPaused = !string.IsNullOrEmpty(status.AutomationsPausedUntil);
        }
    }

    [Action(Id = "GetProfiles", Order = 4, FriendlyName = "Get network profiles",
        Description = "Lists the NetHop profile names")]
    [Throws(NetHopCli.FailedError)]
    [Throws(NetHopCli.ServiceUnavailableError)]
    [Throws(NetHopCli.AccessDeniedError)]
    [Throws(NetHopCli.NotInstalledError)]
    public class GetProfiles : ActionBase
    {
        [OutputArgument(FriendlyName = "Profiles", Description = "Profile names, one per line (use 'Split text' to get a list)")]
        public string Profiles { get; set; }

        [OutputArgument(FriendlyName = "Count")]
        public int Count { get; set; }

        public override void Execute(ActionContext context)
        {
            var profiles = NetHopCli.Run<ProfileSummaryJson[]>("list");
            Profiles = string.Join(Environment.NewLine, profiles.Select(p => p.Name));
            Count = profiles.Length;
        }
    }

    [ConditionAction(Id = "IfProfileIsActive", ResultPropertyName = nameof(Result), Order = 5,
        FriendlyName = "If network profile is active", Description = "Checks whether a NetHop profile matches the current network")]
    [Throws(NetHopCli.FailedError)]
    [Throws(NetHopCli.ServiceUnavailableError)]
    [Throws(NetHopCli.AccessDeniedError)]
    [Throws(NetHopCli.NotInstalledError)]
    public class IfProfileIsActive : ActionBase
    {
        public bool Result { get; private set; }

        [InputArgument(FriendlyName = "Profile", Description = "Name of the profile")]
        public string Profile { get; set; }

        public override void Execute(ActionContext context)
        {
            var status = NetHopCli.Run<StatusJson>("status");
            Result = string.Equals(status.ActiveProfileName, Profile, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    [Action(Id = "PauseAutomations", Order = 6, FriendlyName = "Pause network automations",
        Description = "Pauses NetHop's own automation rules; 0 minutes resumes them")]
    [Throws(NetHopCli.FailedError)]
    [Throws(NetHopCli.ServiceUnavailableError)]
    [Throws(NetHopCli.AccessDeniedError)]
    [Throws(NetHopCli.NotInstalledError)]
    public class PauseAutomations : ActionBase
    {
        [InputArgument(FriendlyName = "Minutes", Description = "How long to pause (0 resumes)"), DefaultValue(60)]
        public int Minutes { get; set; }

        public override void Execute(ActionContext context)
        {
            if (Minutes <= 0)
                NetHopCli.RunPlain("resume");
            else
                NetHopCli.RunPlain("pause", Minutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
