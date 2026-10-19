using System.Diagnostics;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NetHop.Core.Rules;
using NetHop.Ipc;

namespace NetHop.App.Services;

/// <summary>
/// Runs the rule actions that belong in the user's session: toasts, commands, URLs and Power Automate flows.
/// The service never runs these itself, so nothing a rule launches gets SYSTEM rights.
/// </summary>
public static class UserActionRunner
{
    private static bool _notificationsRegistered;

    public static void Initialize(ServiceConnection connection)
    {
        try
        {
            AppNotificationManager.Default.Register();
            _notificationsRegistered = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notifications unavailable: {ex.Message}");
        }

        connection.UserActionRequested += (_, e) => Run(e);
        connection.ProfileApplied += (_, result) => NotifyApplied(result);
    }

    public static void Notify(string title, string? message = null)
    {
        if (!_notificationsRegistered)
            return;

        var builder = new AppNotificationBuilder().AddText(title);
        if (!string.IsNullOrEmpty(message))
            builder.AddText(message);
        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    private static void NotifyApplied(ApplyResult result)
    {
        if (result.Success)
            Notify($"Profile '{result.ProfileName}' applied", result.Steps.Length == 0 ? "Nothing needed to change." : null);
        else
            Notify($"Profile '{result.ProfileName}' failed", result.Error + (result.RolledBack ? " The previous configuration was restored." : ""));
    }

    private static void Run(UserActionRequestedEventArgs e)
    {
        var action = e.Action;
        try
        {
            switch (action.Type)
            {
                case RuleActionType.Notify:
                    Notify(action.Message ?? e.RuleName, $"Rule: {e.RuleName}");
                    break;
                case RuleActionType.RunCommand:
                    Process.Start(new ProcessStartInfo(action.Command!, action.Arguments ?? "") { UseShellExecute = true });
                    break;
                case RuleActionType.OpenUrl or RuleActionType.RunPowerAutomateFlow:
                    Process.Start(new ProcessStartInfo(action.Url!) { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            Notify($"Rule '{e.RuleName}' failed", ex.Message);
        }
    }
}
