using Jarvis.Application.Approvals;
using Jarvis.Application.Audit;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;

namespace Jarvis.Api.Endpoints;

internal static class ApiMappers
{
    public static MessageDto ToDto(this Message message) => new(message.Id, message.Role, message.Content,
        message.CreatedAt);

    public static ToolApprovalDto ToDto(this ToolApprovalRecord approval) => new(approval.Id,
        approval.ConversationId, approval.ToolName, approval.ArgumentsJson, approval.Status, approval.Approved,
        approval.ResumeStatus, approval.CreatedAt);

    public static ReminderDto ToDto(this ReminderRecord reminder) => new(reminder.Id, reminder.Title,
        reminder.DueAt, reminder.Status, reminder.CreatedAt, reminder.CompletedAt, reminder.Recurrence,
        reminder.Weekdays, reminder.TimeZoneId, reminder.LocalTime, reminder.Until, reminder.LastDeliveredAt);

    public static ConditionWatchDto ToDto(this ConditionWatchRecord watch) => new(watch.Id, watch.Title,
        watch.Url, watch.JsonPath, watch.Comparison, watch.Threshold, watch.IntervalMinutes, watch.Status,
        watch.CreatedAt, watch.LastCheckedAt, watch.LastValue);

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

    public static IEnumerable<ToolApprovalDto> ToDtos(this IEnumerable<ToolApprovalRecord> approvals) =>
        approvals.Select(ToDto);
}
