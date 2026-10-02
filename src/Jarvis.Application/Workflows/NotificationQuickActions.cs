using Jarvis.Domain.Workflows;

namespace Jarvis.Application.Workflows;

/// <summary>
/// Buttons shown on a push notification. Reminders get Done and Snooze, which run without opening the app.
/// Approvals only get Open: deciding an approval always happens inside the app, never from a notification.
/// </summary>
public static class NotificationQuickActions
{
    public const string ReminderCategory = "jarvis.reminder";
    public const string ApprovalCategory = "jarvis.approval";

    public const string Done = "done";
    public const string Snooze = "snooze";

    public const int DefaultSnoozeMinutes = 10;
    public const int MaxSnoozeMinutes = 24 * 60;

    /// <summary>The iOS notification category for a notification type, or null when it has no buttons.</summary>
    public static string? CategoryFor(string type) => type switch
    {
        "reminder.due" => ReminderCategory,
        "approval.required" or "automation.approval" => ApprovalCategory,
        _ => null
    };
}

public enum NotificationQuickActionOutcome
{
    Done,
    Snoozed,
    NotFound,
    NotSupported,
    UnknownAction,
    InvalidSnooze,
    ReminderGone
}

public sealed record NotificationQuickActionResult(NotificationQuickActionOutcome Outcome, ReminderRecord? Reminder = null);

public interface INotificationQuickActionService
{
    Task<NotificationQuickActionResult> RunAsync(Guid notificationId, Guid ownerId, string? action, int? minutes,
        CancellationToken cancellationToken);
}

public sealed class NotificationQuickActionService(
    INotificationRepository notifications,
    IReminderService reminders,
    TimeProvider timeProvider) : INotificationQuickActionService
{
    public async Task<NotificationQuickActionResult> RunAsync(Guid notificationId, Guid ownerId, string? action,
        int? minutes, CancellationToken cancellationToken)
    {
        var notification = await notifications.GetNotificationAsync(notificationId, ownerId, cancellationToken);
        if (notification is null) return new(NotificationQuickActionOutcome.NotFound);
        if (notification.Type != "reminder.due" || notification.SourceId is not Guid reminderId)
            return new(NotificationQuickActionOutcome.NotSupported);

        switch (action?.Trim().ToLowerInvariant())
        {
            case NotificationQuickActions.Done:
            {
                // A delivered one-time reminder is already complete, and a repeating one keeps its schedule, so
                // Done mostly acknowledges this occurrence. A reminder that was snoozed again is finished for good.
                var reminder = await reminders.GetAsync(reminderId, ownerId, cancellationToken);
                if (reminder is { Status: "pending", Recurrence: Reminder.RecurrenceNone })
                    reminder = await reminders.MarkDoneAsync(reminderId, ownerId, cancellationToken) ?? reminder;
                await notifications.MarkReadAsync(notificationId, ownerId, cancellationToken);
                return new(NotificationQuickActionOutcome.Done, reminder);
            }
            case NotificationQuickActions.Snooze:
            {
                var snoozeMinutes = minutes ?? NotificationQuickActions.DefaultSnoozeMinutes;
                if (snoozeMinutes is < 1 or > NotificationQuickActions.MaxSnoozeMinutes)
                    return new(NotificationQuickActionOutcome.InvalidSnooze);
                var until = timeProvider.GetUtcNow().AddMinutes(snoozeMinutes);
                ReminderRecord? snoozed;
                try
                {
                    snoozed = await reminders.SnoozeAsync(reminderId, ownerId, until, cancellationToken);
                }
                catch (ArgumentException)
                {
                    snoozed = null;
                }
                if (snoozed is null) return new(NotificationQuickActionOutcome.ReminderGone);
                await notifications.MarkReadAsync(notificationId, ownerId, cancellationToken);
                return new(NotificationQuickActionOutcome.Snoozed, snoozed);
            }
            default:
                return new(NotificationQuickActionOutcome.UnknownAction);
        }
    }
}
