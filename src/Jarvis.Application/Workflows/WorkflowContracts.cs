using Jarvis.Domain.Workflows;
using Jarvis.Application.Profiles;

namespace Jarvis.Application.Workflows;

public sealed record ReminderRecord(Guid Id, Guid OwnerId, string Title, DateTimeOffset DueAt,
    string WorkflowId, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    string Recurrence = "none", int Weekdays = 0, string TimeZoneId = "UTC", TimeOnly? LocalTime = null,
    DateOnly? Until = null, DateTimeOffset? LastDeliveredAt = null);

public sealed record CreateReminderRequest(string Title, DateTimeOffset DueAt, string? Recurrence = null,
    int Weekdays = 0, string? TimeZoneId = null, DateOnly? Until = null, TimeOnly? LocalTime = null);

public sealed record NotificationRecord(Guid Id, string Type, string Title, string Body,
    Guid? SourceId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record JarvisTaskRecord(Guid Id, Guid OwnerId, string Title, string Prompt, string Status,
    string WorkflowId, Guid ConversationId, Guid UserMessageId, Guid ResultMessageId,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? Summary);

public sealed record JarvisTaskWorkflowInput(Guid TaskId);
public sealed record JarvisTaskApprovalInput(Guid TaskId, string Summary);
public sealed record CreateJarvisTaskRequest(string Title, string Prompt, Guid? ProfileId = null);

public sealed record ReminderWorkflowInput(Guid ReminderId, Guid OwnerId, string Title, DateTimeOffset DueAt);

public sealed record ReminderDeliveryResult(bool Continue, DateTimeOffset NextDueAt, string Title);

public sealed record ConditionWatchRecord(Guid Id, Guid OwnerId, string Title, string Url, string JsonPath,
    string Comparison, double Threshold, int IntervalMinutes, string WorkflowId, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? LastCheckedAt, double? LastValue,
    string Kind = WatchKinds.PublicJson, string? CredentialProvider = null, double? Latitude = null,
    double? Longitude = null, double? RadiusMeters = null, int? MinutesBefore = null);

public sealed record ConditionWatchWorkflowInput(Guid WatchId);
public sealed record ConditionWatchCheckResult(bool Continue, int IntervalMinutes);
public sealed record CreateConditionWatchRequest(string Title, string Url, string JsonPath,
    string Comparison, double Threshold, int IntervalMinutes = 15, string? Kind = null,
    string? CredentialProvider = null, double? Latitude = null, double? Longitude = null,
    double? RadiusMeters = null, int? MinutesBefore = null);

public sealed record DailyBriefingPreferenceRecord(Guid OwnerId, bool Enabled, TimeOnly LocalTime,
    string TimeZoneId, string WorkflowId, DateTimeOffset? ScheduleDispatchedAt, DateOnly? LastDeliveredDate);
public sealed record SaveDailyBriefingRequest(bool Enabled, TimeOnly LocalTime, string TimeZoneId);
public sealed record DailyBriefingWorkflowInput(Guid OwnerId, string WorkflowId, TimeOnly LocalTime, string TimeZoneId);
public sealed record DailyBriefingActivityInput(Guid OwnerId, string WorkflowId, DateOnly LocalDate,
    string TimeZoneId, DateTimeOffset LocalDayStart, DateTimeOffset NextLocalDayStart);
public sealed record DailyBriefingSchedule(DateTimeOffset FireAt, DateOnly LocalDate,
    DateTimeOffset LocalDayStart, DateTimeOffset NextLocalDayStart);

public interface IDailyBriefingRepository
{
    Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<(DailyBriefingPreferenceRecord Preference, string PreviousWorkflowId)> SaveAsync(Guid ownerId,
        SaveDailyBriefingRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<DailyBriefingPreferenceRecord>> ListPendingForSchedulingAsync(CancellationToken cancellationToken);
    Task<int> RequeueStaleEnabledAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);
    Task MarkScheduleDispatchedAsync(Guid ownerId, string workflowId, CancellationToken cancellationToken);
    Task<bool> DeliverAsync(DailyBriefingActivityInput input, CancellationToken cancellationToken);
}

public interface IDailyBriefingScheduler
{
    Task ScheduleAsync(DailyBriefingWorkflowInput input, CancellationToken cancellationToken);
    Task CancelAsync(string workflowId, CancellationToken cancellationToken);
}

public interface IDailyBriefingService
{
    Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<DailyBriefingPreferenceRecord> SaveAsync(Guid ownerId, SaveDailyBriefingRequest request,
        CancellationToken cancellationToken);
}

public interface IConditionWatchRepository
{
    Task<ConditionWatchRecord> CreateAsync(Guid ownerId, CreateConditionWatchRequest request,
        CancellationToken cancellationToken);
    Task<ConditionWatchRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<ConditionWatchRecord?> GetForExecutionAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConditionWatchRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConditionWatchRecord>> ListPendingForSchedulingAsync(CancellationToken cancellationToken);
    Task<int> RequeueStaleActiveAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);
    Task MarkScheduleDispatchedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<ConditionWatchCheckResult> RecordCheckAsync(Guid id, double value, DateTimeOffset checkedAt,
        CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid id, CancellationToken cancellationToken);
}

public interface IConditionWatchService
{
    Task<ConditionWatchRecord> CreateAsync(Guid ownerId, CreateConditionWatchRequest request,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ConditionWatchRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<ConditionWatchRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public interface IConditionWatchScheduler
{
    Task ScheduleAsync(ConditionWatchRecord watch, CancellationToken cancellationToken);
    Task CancelAsync(string workflowId, CancellationToken cancellationToken);
}

public interface IReminderRepository
{
    Task<ReminderRecord> CreateAsync(Guid ownerId, CreateReminderRequest request, CancellationToken cancellationToken);
    Task<ReminderRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReminderRecord>> ListRemindersAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReminderRecord>> ListPendingForSchedulingAsync(CancellationToken cancellationToken);
    Task<int> RequeueOverdueDispatchedAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);
    Task MarkReminderScheduleDispatchedAsync(Guid id, CancellationToken cancellationToken);
    Task<ReminderRecord?> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task MarkScheduleFailedAsync(Guid id, CancellationToken cancellationToken);
    Task<ReminderDeliveryResult> CompleteAndNotifyAsync(ReminderWorkflowInput reminder, CancellationToken cancellationToken);
}

public interface INotificationRepository
{
    Task<IReadOnlyList<NotificationRecord>> ListNotificationsAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<bool> MarkReadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Stores an in-app notification and queues push delivery to the owner's devices.</summary>
    Task<NotificationRecord> CreateAsync(Guid ownerId, string type, string title, string body, Guid? sourceId,
        CancellationToken cancellationToken);
}

public sealed record PushDeviceRecord(Guid Id, string Platform, DateTimeOffset UpdatedAt);

public interface IPushDeviceRepository
{
    Task<PushDeviceRecord> RegisterAsync(Guid ownerId, string token, string platform, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(Guid ownerId, string token, CancellationToken cancellationToken);
}

public interface IJarvisTaskRepository
{
    Task<JarvisTaskRecord> CreateAsync(Guid ownerId, string title, string prompt, Guid conversationId, CancellationToken cancellationToken);
    Task<JarvisTaskRecord> CreateWithConversationAsync(Guid ownerId, string title, string prompt, CancellationToken cancellationToken,
        ProfileBinding? profile = null);
    Task<JarvisTaskRecord?> GetTaskAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<JarvisTaskRecord?> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<JarvisTaskRecord?> GetTaskByConversationIdAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JarvisTaskRecord>> ListQueuedForSchedulingAsync(CancellationToken cancellationToken);
    Task<int> RequeueStaleQueuedAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
    Task<IReadOnlyList<JarvisTaskRecord>> ListRecentlyTerminalAsync(DateTimeOffset completedAfter,
        CancellationToken cancellationToken);
    Task MarkTaskScheduleDispatchedAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<JarvisTaskRecord>> ListActiveAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JarvisTaskRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task MarkTaskScheduleFailedAsync(Guid id, CancellationToken cancellationToken);
    Task MarkRunningAsync(Guid id, CancellationToken cancellationToken);
    Task MarkNeedsApprovalAsync(Guid id, CancellationToken cancellationToken);
    Task CompleteAndNotifyAsync(Guid id, string summary, CancellationToken cancellationToken);
    Task FailAsync(Guid id, string summary, CancellationToken cancellationToken);
    Task<bool> CancelTaskAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public interface IJarvisTaskService
{
    Task<JarvisTaskRecord> CreateAsync(Guid ownerId, string title, string prompt, CancellationToken cancellationToken,
        Guid? profileId = null);
    Task<IReadOnlyList<JarvisTaskRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task CompleteAfterApprovalAsync(Guid? taskId, Guid ownerId, string summary, CancellationToken cancellationToken);
    Task FailAfterRejectedApprovalAsync(Guid? taskId, Guid ownerId, string summary, CancellationToken cancellationToken);
}

public interface IReminderService
{
    Task<ReminderRecord> CreateAsync(Guid ownerId, CreateReminderRequest request, CancellationToken cancellationToken);
    Task<ReminderRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReminderRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<ReminderRecord?> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public static class WorkflowRecordMapping
{
    public static JarvisTaskRecord ToRecord(this JarvisTask task) => new(task.Id, task.OwnerId,
        task.Title, task.Prompt, task.Status, task.WorkflowId, task.ConversationId,
        task.UserMessageId, task.ResultMessageId, task.CreatedAt, task.StartedAt,
        task.CompletedAt, task.Summary);

    public static ReminderRecord ToRecord(this Reminder reminder) => new(reminder.Id, reminder.OwnerId,
        reminder.Title, reminder.DueAt, reminder.WorkflowId, reminder.Status, reminder.CreatedAt, reminder.CompletedAt,
        reminder.Recurrence, reminder.Weekdays, reminder.TimeZoneId, reminder.LocalTime, reminder.Until,
        reminder.LastDeliveredAt);

    public static NotificationRecord ToRecord(this Notification notification) => new(notification.Id,
        notification.Type, notification.Title, notification.Body, notification.SourceId,
        notification.CreatedAt, notification.ReadAt);
}
