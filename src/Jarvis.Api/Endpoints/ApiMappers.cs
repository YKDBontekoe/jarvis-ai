using Jarvis.Application.Approvals;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using System.Text.Json;

namespace Jarvis.Api.Endpoints;

internal static class ApiMappers
{
    private static readonly JsonSerializerOptions CitationJsonOptions = new(JsonSerializerDefaults.Web);

    public static MessageDto ToDto(this Message message)
    {
        IReadOnlyList<FileCitationDto>? citations = null;
        if (!string.IsNullOrWhiteSpace(message.CitationsJson))
        {
            var parsed = JsonSerializer.Deserialize<IReadOnlyList<FileCitation>>(message.CitationsJson,
                CitationJsonOptions);
            if (parsed is { Count: > 0 })
                citations = parsed.Select(citation => citation.ToDto()).ToArray();
        }

        var attachments = MessageAttachments.Parse(message.AttachmentsJson);
        return new MessageDto(message.Id, message.Role, message.Content, message.CreatedAt, citations,
            attachments.Count == 0
                ? null
                : attachments.Select(item => new MessageAttachmentDto(item.FileId, item.FileName, item.ContentType))
                    .ToArray());
    }

    public static FileCitationDto ToDto(this FileCitation citation) => new(citation.FileId, citation.DisplayName,
        citation.ChunkId, citation.ChunkIndex, citation.Excerpt, citation.PageNumber, citation.SourceStatus);

    public static ToolApprovalDto ToDto(this ToolApprovalRecord approval) => new(approval.Id,
        approval.ConversationId, approval.ToolName, approval.ArgumentsJson, approval.Status, approval.Approved,
        approval.ResumeStatus, approval.CreatedAt);

    public static ReminderDto ToDto(this ReminderRecord reminder) => new(reminder.Id, reminder.Title,
        reminder.DueAt, reminder.Status, reminder.CreatedAt, reminder.CompletedAt, reminder.Recurrence,
        reminder.Weekdays, reminder.TimeZoneId, reminder.LocalTime, reminder.Until, reminder.LastDeliveredAt,
        reminder.ConversationId,
        reminder.Place is { } place
            ? new ReminderPlaceDto(place.Name, place.Latitude, place.Longitude, place.RadiusMeters, place.Trigger,
                place.Repeats)
            : null);

    public static ConditionWatchDto ToDto(this ConditionWatchRecord watch) => new(watch.Id, watch.Title,
        watch.Url, watch.JsonPath, watch.Comparison, watch.Threshold, watch.IntervalMinutes, watch.Status,
        watch.CreatedAt, watch.LastCheckedAt, watch.LastValue, watch.Kind, watch.CredentialProvider,
        watch.Latitude, watch.Longitude, watch.RadiusMeters, watch.MinutesBefore);

    public static JarvisTaskDto ToDto(this JarvisTaskRecord task) => new(task.Id, task.Title, task.Prompt,
        task.Status, task.ConversationId, task.CreatedAt, task.StartedAt, task.CompletedAt, task.Summary);

    public static NotificationDto ToDto(this NotificationRecord notification) => new(notification.Id,
        notification.Type, notification.Title, notification.Body, notification.SourceId, notification.CreatedAt,
        notification.ReadAt);

    public static AuditEventDto ToDto(this AuditEventRecord item) => new(item.Id, item.AgentRunId, item.Tool,
        item.Action, item.RiskClass, item.ApprovalId, item.Timestamp, item.Success, item.MetadataJson);

    public static FileDto ToDto(this Jarvis.Domain.Files.StoredFile file) => new(file.Id, file.FileName,
        file.ContentType, file.SizeBytes, file.Sha256, file.CreatedAt, file.ProcessingStatus);

    public static MemoryDto ToDto(this Jarvis.Domain.Memory.MemoryRecord memory) => new(memory.Id, memory.Kind,
        memory.Content, memory.Importance, memory.Confidence, memory.CreatedAt, memory.UpdatedAt, memory.ValidUntil,
        memory.IsPinned, memory.SourceType);

    public static JournalEntryDto ToDto(this Jarvis.Domain.Journal.JournalEntry entry) => new(entry.Id,
        entry.EntryDate, entry.Source, entry.Content, entry.Highlights, entry.Gratitude, entry.Rating, entry.Mood,
        entry.Energy, entry.Stress, entry.Tags, entry.MemoryId, entry.CreatedAt, entry.UpdatedAt);

    public static HabitDto ToDto(this Jarvis.Application.Habits.HabitSummary summary)
    {
        var habit = summary.Habit;
        var stats = summary.Stats;
        return new HabitDto(habit.Id, habit.Name, habit.Icon, habit.Cadence, habit.TargetPerWeek, habit.IsArchived,
            habit.ArchivedAt, new HabitStatsDto(stats.Today, stats.CurrentStreak, stats.BestStreak, stats.StreakUnit,
                stats.DoneToday, stats.ThisWeekCount, stats.TotalCheckIns, summary.IsOpenToday, stats.RecentDates),
            habit.CreatedAt, habit.UpdatedAt);
    }

    public static IEnumerable<ToolApprovalDto> ToDtos(this IEnumerable<ToolApprovalRecord> approvals) =>
        approvals.Select(ToDto);
}
