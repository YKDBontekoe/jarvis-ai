using Jarvis.Api.Notifications;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class NotificationQuickActionTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("reminder.due", "jarvis.reminder")]
    [InlineData("approval.required", "jarvis.approval")]
    [InlineData("automation.approval", "jarvis.approval")]
    [InlineData("task.completed", null)]
    [InlineData("reminder.failed", null)]
    public void Push_payload_names_the_category_for_the_buttons(string type, string? category)
    {
        var aps = NotificationPushWorker.BuildApsPayload(type);
        Assert.Equal("default", aps["sound"]);
        Assert.Equal(category, aps.GetValueOrDefault("category"));
    }

    [Fact]
    public async Task Snooze_moves_the_reminder_ten_minutes_and_marks_the_notification_read()
    {
        var (service, notification, reminders, notifications) = Arrange("reminder.due", status: "completed");

        var result = await service.RunAsync(notification.Id, Owner, "snooze", null, CancellationToken.None);

        Assert.Equal(NotificationQuickActionOutcome.Snoozed, result.Outcome);
        Assert.Equal(Now.AddMinutes(10), reminders.SnoozedUntil);
        Assert.Contains(notification.Id, notifications.Read);
    }

    [Fact]
    public async Task Done_on_a_delivered_reminder_only_acknowledges_it()
    {
        var (service, notification, reminders, notifications) = Arrange("reminder.due", status: "completed");

        var result = await service.RunAsync(notification.Id, Owner, "done", null, CancellationToken.None);

        Assert.Equal(NotificationQuickActionOutcome.Done, result.Outcome);
        Assert.False(reminders.MarkedDone);
        Assert.Contains(notification.Id, notifications.Read);
    }

    [Fact]
    public async Task Done_finishes_a_one_time_reminder_that_is_pending_again()
    {
        var (service, notification, reminders, _) = Arrange("reminder.due", status: "pending");

        await service.RunAsync(notification.Id, Owner, "done", null, CancellationToken.None);

        Assert.True(reminders.MarkedDone);
    }

    [Fact]
    public async Task Done_never_ends_a_repeating_reminder()
    {
        var (service, notification, reminders, _) = Arrange("reminder.due", status: "pending", recurrence: "daily");

        await service.RunAsync(notification.Id, Owner, "done", null, CancellationToken.None);

        Assert.False(reminders.MarkedDone);
    }

    [Theory]
    [InlineData("approval.required")]
    [InlineData("automation.approval")]
    [InlineData("task.completed")]
    public async Task Approvals_and_other_notifications_have_no_quick_actions(string type)
    {
        var (service, notification, reminders, notifications) = Arrange(type, status: "completed");

        var result = await service.RunAsync(notification.Id, Owner, "done", null, CancellationToken.None);

        Assert.Equal(NotificationQuickActionOutcome.NotSupported, result.Outcome);
        Assert.False(reminders.MarkedDone);
        Assert.Empty(notifications.Read);
    }

    [Fact]
    public async Task Another_owners_notification_is_not_found()
    {
        var (service, notification, _, _) = Arrange("reminder.due", status: "completed");

        var result = await service.RunAsync(notification.Id, Guid.NewGuid(), "snooze", null, CancellationToken.None);

        Assert.Equal(NotificationQuickActionOutcome.NotFound, result.Outcome);
    }

    [Theory]
    [InlineData("snooze", 0, NotificationQuickActionOutcome.InvalidSnooze)]
    [InlineData("snooze", 24 * 60 + 1, NotificationQuickActionOutcome.InvalidSnooze)]
    [InlineData("approve", null, NotificationQuickActionOutcome.UnknownAction)]
    [InlineData(null, null, NotificationQuickActionOutcome.UnknownAction)]
    public async Task Bad_requests_change_nothing(string? action, int? minutes, NotificationQuickActionOutcome outcome)
    {
        var (service, notification, reminders, notifications) = Arrange("reminder.due", status: "completed");

        var result = await service.RunAsync(notification.Id, Owner, action, minutes, CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(reminders.SnoozedUntil);
        Assert.Empty(notifications.Read);
    }

    [Fact]
    public async Task Snoozing_a_cancelled_reminder_reports_it_is_gone()
    {
        var (service, notification, _, notifications) = Arrange("reminder.due", status: "cancelled");

        var result = await service.RunAsync(notification.Id, Owner, "snooze", null, CancellationToken.None);

        Assert.Equal(NotificationQuickActionOutcome.ReminderGone, result.Outcome);
        Assert.Empty(notifications.Read);
    }

    private static (NotificationQuickActionService, NotificationRecord, StubReminders, StubNotifications) Arrange(
        string type, string status, string recurrence = "none")
    {
        var reminder = new ReminderRecord(Guid.NewGuid(), Owner, "Water the plants", Now.AddMinutes(-1),
            "wf", status, Now.AddDays(-1), null, recurrence);
        var notification = new NotificationRecord(Guid.NewGuid(), type, "Reminder", reminder.Title, reminder.Id,
            Now, null);
        var reminders = new StubReminders(reminder);
        var notifications = new StubNotifications(notification);
        return (new NotificationQuickActionService(notifications, reminders, new FixedClock(Now)), notification,
            reminders, notifications);
    }

    private sealed class StubNotifications(NotificationRecord notification) : INotificationRepository
    {
        public List<Guid> Read { get; } = [];

        public Task<IReadOnlyList<NotificationRecord>> ListNotificationsAsync(Guid ownerId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotificationRecord>>([notification]);

        public Task<NotificationRecord?> GetNotificationAsync(Guid id, Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(id == notification.Id && ownerId == Owner ? notification : null);

        public Task<bool> MarkReadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            Read.Add(id);
            return Task.FromResult(true);
        }

        public Task<NotificationRecord> CreateAsync(Guid ownerId, string type, string title, string body,
            Guid? sourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubReminders(ReminderRecord reminder) : IReminderService
    {
        public DateTimeOffset? SnoozedUntil { get; private set; }
        public bool MarkedDone { get; private set; }

        public Task<ReminderRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(id == reminder.Id && ownerId == reminder.OwnerId ? reminder : null);

        public Task<ReminderRecord?> SnoozeAsync(Guid id, Guid ownerId, DateTimeOffset dueAt,
            CancellationToken cancellationToken)
        {
            if (reminder.Status is not ("pending" or "completed")) return Task.FromResult<ReminderRecord?>(null);
            SnoozedUntil = dueAt;
            return Task.FromResult<ReminderRecord?>(reminder with { Status = "pending", DueAt = dueAt });
        }

        public Task<ReminderRecord?> MarkDoneAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            if (reminder.Status != "pending" || reminder.Recurrence != "none")
                return Task.FromResult<ReminderRecord?>(null);
            MarkedDone = true;
            return Task.FromResult<ReminderRecord?>(reminder with { Status = "completed" });
        }

        public Task<ReminderRecord> CreateAsync(Guid ownerId, CreateReminderRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ReminderRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReminderRecord?> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
