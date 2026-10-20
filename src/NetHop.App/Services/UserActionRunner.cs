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
    private const string PageArgumentName = "page";
    private const string SettingsPageValue = "settings";

    private static bool _notificationsRegistered;
    private static bool _serviceDownNotified;
    private static bool _driftNotified;

    /// <summary>
    /// Shows a notification through the tray icon. Used when Windows App SDK notifications are not available,
    /// which happens for unpackaged apps on some systems.
    /// </summary>
    public static Action<string, string?>? FallbackNotifier { get; set; }

    public static void Initialize(ServiceConnection connection)
    {
        try
        {
            if (AppNotificationManager.IsSupported())
            {
                // Must be subscribed before Register(). Clicking a notification opens NetHop.
                AppNotificationManager.Default.NotificationInvoked += (_, e) =>
                {
                    var settings = e.Arguments.TryGetValue(PageArgumentName, out var page) && page == SettingsPageValue;
                    connection.Dispatcher.TryEnqueue(() =>
                    {
                        if (settings)
                            App.Current?.ShowSettings();
                        else
                            App.Current?.ShowMainWindow();
                    });
                };
                AppNotificationManager.Default.Register();
                _notificationsRegistered = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"App notifications unavailable, using the tray icon: {ex.Message}");
        }

        connection.UserActionRequested += (_, e) => Run(e);
        connection.StatusChanged += (_, e) => NotifyStatusChange(e);
        connection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServiceConnection.State))
                NotifyConnection(connection.State);
        };
    }

    /// <param name="openSettings">Clicking the notification opens Settings rather than the dashboard.</param>
    public static void Notify(string title, string? message = null, bool openSettings = false)
    {
        if (_notificationsRegistered)
        {
            try
            {
                var builder = new AppNotificationBuilder().AddText(title);
                if (!string.IsNullOrEmpty(message))
                    builder.AddText(message);
                if (openSettings)
                    builder.AddArgument(PageArgumentName, SettingsPageValue);
                AppNotificationManager.Default.Show(builder.BuildNotification());
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App notification failed, using the tray icon: {ex.Message}");
                _notificationsRegistered = false;
            }
        }
        FallbackNotifier?.Invoke(title, message);
    }

    /// <summary>Every change the service reports, whoever made it: the app, the tray, a rule, the CLI, Logi or Power Automate.</summary>
    private static void NotifyStatusChange(StatusChangedEventArgs e)
    {
        switch (e.Kind)
        {
            case StatusChangeKind.ProfileApplied when e.ApplyResult is { DryRun: false } result:
                NotifyApplied(result, e.ByRule ? e.RuleName : null);
                break;
            case StatusChangeKind.AutomationsPaused:
                if (e.Status?.AutomationsPausedUntil is { } until)
                    Notify(Loc.Get("Toast_AutomationsPaused"), Loc.Format("Common_PausedUntil", until.LocalDateTime));
                else
                    Notify(Loc.Get("Toast_AutomationsResumed"));
                break;
            case StatusChangeKind.ProfilesChanged:
                Notify(Loc.Get("Toast_ProfilesChanged"));
                break;
            case StatusChangeKind.RulesChanged:
                Notify(Loc.Get("Toast_RulesChanged"));
                break;
        }

        // The network was changed outside NetHop: say so once, until a profile matches again.
        var drifted = e.Status?.ActiveProfileDrifted == true;
        if (drifted && !_driftNotified)
            Notify(Loc.Format("Toast_Drifted", e.Status!.ActiveProfileName), Loc.Get("Dashboard_Drifted"));
        if (e.Status is not null)
            _driftNotified = drifted;
    }

    private static void NotifyApplied(ApplyResult result, string? ruleName)
    {
        var byRule = ruleName is null ? null : Loc.Format("Toast_Rule", ruleName);
        if (result.Success)
        {
            var changes = result.Steps.Length == 0
                ? Loc.Get("Common_NothingToChange")
                : Loc.Format("Dashboard_ChangesIn", result.Steps.Length, result.Duration.TotalSeconds);
            Notify(Loc.Format("Common_ProfileApplied", result.ProfileName), byRule is null ? changes : $"{changes}\n{byRule}");
        }
        else
        {
            var error = result.Error + (result.RolledBack ? " " + Loc.Get("Common_RolledBack") : "");
            Notify(Loc.Format("Common_ProfileFailed", result.ProfileName), byRule is null ? error : $"{error}\n{byRule}");
        }
    }

    /// <summary>Once when the service goes away (not on every retry), and once when it is back.</summary>
    private static void NotifyConnection(ConnectionState state)
    {
        if (state == ConnectionState.ServiceNotRunning && !_serviceDownNotified)
        {
            _serviceDownNotified = true;
            Notify(Loc.Get("Main_ServiceStoppedTitle"), Loc.Get("Main_ServiceStoppedMessage"));
        }
        else if (state == ConnectionState.Connected && _serviceDownNotified)
        {
            _serviceDownNotified = false;
            Notify(Loc.Get("Toast_ServiceBack"));
        }
    }

    private static void Run(UserActionRequestedEventArgs e)
    {
        var action = e.Action;
        try
        {
            switch (action.Type)
            {
                case RuleActionType.Notify:
                    Notify(action.Message ?? e.RuleName, Loc.Format("Toast_Rule", e.RuleName));
                    break;
                case RuleActionType.RunCommand:
                    Process.Start(new ProcessStartInfo(action.Command!, action.Arguments ?? "") { UseShellExecute = true });
                    break;
                // Checked again here, not only when the rules are saved: the shell runs whatever a URL points to.
                case RuleActionType.OpenUrl when RuleAction.IsWebAddress(action.Url):
                case RuleActionType.RunPowerAutomateFlow when RuleAction.IsFlowAddress(action.Url):
                    Process.Start(new ProcessStartInfo(action.Url!) { UseShellExecute = true });
                    break;
                case RuleActionType.OpenUrl or RuleActionType.RunPowerAutomateFlow:
                    throw new InvalidOperationException($"Refused to open '{action.Url}': not a web or Power Automate address.");
            }
        }
        catch (Exception ex)
        {
            Notify(Loc.Format("Toast_RuleFailed", e.RuleName), ex.Message);
        }
    }
}
