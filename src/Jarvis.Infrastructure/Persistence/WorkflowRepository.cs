using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Approvals;
using Jarvis.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Jarvis.Infrastructure.Persistence;

public sealed class WorkflowRepository(JarvisDbContext db) : IReminderRepository, INotificationRepository,
    IPushDeviceRepository, IJarvisTaskRepository
{
    public async Task<PushDeviceRecord> RegisterAsync(Guid ownerId, string token, string platform,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var device = await db.PushDevices.FromSqlInterpolated(
                $"SELECT * FROM push_devices WHERE \"Token\" = {token} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (device is null)
        {
            device = new PushDevice(ownerId, token, platform);
            db.PushDevices.Add(device);
        }
        else
        {
            if (device.OwnerId != ownerId)
            {
                await db.PushDeliveries.Where(x => x.DeviceId == device.Id).ExecuteDeleteAsync(cancellationToken);
                device.AssignTo(ownerId, platform);
            }
            else device.Refresh(platform);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PushDeviceRecord(device.Id, device.Platform, device.UpdatedAt);
    }

    public async Task<bool> RemoveAsync(Guid ownerId, string token, CancellationToken cancellationToken)
    {
        var device = await db.PushDevices.SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Token == token,
            cancellationToken);
        if (device is null) return false;
        db.PushDevices.Remove(device);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<JarvisTaskRecord> CreateAsync(Guid ownerId, string title, string prompt, Guid conversationId, CancellationToken cancellationToken)
    {
        var task = new JarvisTask(ownerId, title, prompt);
        task.AttachConversation(conversationId);
        db.Tasks.Add(task);
        AddAuditEvent(ownerId, "tasks", "task.created", "low", true, task.Id);
        await db.SaveChangesAsync(cancellationToken);
        return task.ToRecord();
    }

    public async Task<JarvisTaskRecord> CreateWithConversationAsync(Guid ownerId, string title, string prompt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var conversation = new Conversation(ownerId, title);
        db.Conversations.Add(conversation);
        var task = new JarvisTask(ownerId, title, prompt);
        task.AttachConversation(conversation.Id);
        db.Tasks.Add(task);
        db.Messages.Add(new Message(conversation.Id, "user", prompt, task.UserMessageId));
        conversation.Touch();
        AddAuditEvent(ownerId, "tasks", "task.created", "low", true, task.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return task.ToRecord();
    }

    public async Task<JarvisTaskRecord?> GetTaskAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<JarvisTaskRecord?> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken))?.ToRecord();

    public async Task<JarvisTaskRecord?> GetTaskByConversationIdAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.ConversationId == conversationId && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<JarvisTaskRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Tasks.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task<IReadOnlyList<JarvisTaskRecord>> ListActiveAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Tasks.AsNoTracking().Where(x => x.OwnerId == ownerId &&
                (x.Status == "queued" || x.Status == "running" || x.Status == "needs_approval"))
            .OrderByDescending(x => x.CreatedAt).Take(8).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task<IReadOnlyList<JarvisTaskRecord>> ListQueuedForSchedulingAsync(CancellationToken cancellationToken) =>
        (await db.Tasks.AsNoTracking().Where(x => x.Status == "queued" && x.ScheduleDispatchedAt == null)
            .OrderBy(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task<int> RequeueStaleQueuedAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) =>
        await db.Tasks.Where(x => x.Status == "queued" && x.ScheduleDispatchedAt != null &&
                x.ScheduleDispatchedAt < olderThan)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ScheduleDispatchedAt, (DateTimeOffset?)null),
                cancellationToken);

    public async Task<IReadOnlyList<JarvisTaskRecord>> ListRecentlyTerminalAsync(DateTimeOffset completedAfter,
        CancellationToken cancellationToken) =>
        (await db.Tasks.AsNoTracking().Where(x =>
                (x.Status == "cancelled" || x.Status == "failed" || x.Status == "completed") &&
                x.CompletedAt != null && x.CompletedAt >= completedAfter)
            .OrderByDescending(x => x.CompletedAt).Take(100).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task MarkTaskScheduleDispatchedAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (task is null) return;
        task.MarkScheduleDispatched();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkRunningAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var task = await GetLockedTaskAsync(id, cancellationToken);
        if (task is null || task.Status is "running" or "completed" or "failed" or "cancelled" or "needs_approval")
            return;
        task.MarkRunning();
        AddAuditEvent(task.OwnerId, "temporal", "task.started", "low", true, task.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkTaskScheduleFailedAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var task = await GetLockedTaskAsync(id, cancellationToken);
        if (task is null || task.Status is "completed" or "failed" or "cancelled") return;
        task.Fail("Task could not be scheduled.");
        AddAuditEvent(task.OwnerId, "temporal", "task.schedule_failed", "high", false, task.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkNeedsApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var task = await GetLockedTaskAsync(id, cancellationToken);
        if (task is null || task.Status is "needs_approval" or "completed" or "failed" or "cancelled") return;
        task.MarkNeedsApproval();
        AddAuditEvent(task.OwnerId, "temporal", "task.approval_required", "high", true, task.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteAndNotifyAsync(Guid id, string summary, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var task = await GetLockedTaskAsync(id, cancellationToken);
        if (task is null || task.Status is "completed" or "failed" or "cancelled") return;
        task.Complete(summary);
        AddAuditEvent(task.OwnerId, "temporal", "task.completed", "low", true, task.Id);
        await CancelPendingApprovalsAsync(task.Id, task.OwnerId, cancellationToken);
        if (!await db.Notifications.AnyAsync(x => x.Id == task.Id, cancellationToken))
        {
            var notification = new Notification(task.Id, task.OwnerId, "task.completed",
                "Task finished", task.Title, task.Id);
            db.Notifications.Add(notification);
            await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FailAsync(Guid id, string summary, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var task = await GetLockedTaskAsync(id, cancellationToken);
        if (task is null || task.Status is "completed" or "failed" or "cancelled") return;
        task.Fail(summary);
        AddAuditEvent(task.OwnerId, "temporal", "task.failed", "high", false, task.Id);
        await CancelPendingApprovalsAsync(task.Id, task.OwnerId, cancellationToken);
        if (!await db.Notifications.AnyAsync(x => x.Id == task.Id, cancellationToken))
        {
            var notification = new Notification(task.Id, task.OwnerId, "task.failed",
                "Task failed", summary, task.Id);
            db.Notifications.Add(notification);
            await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> CancelTaskAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var task = await GetLockedTaskAsync(id, cancellationToken);
        if (task is null || task.OwnerId != ownerId || task.Status is "completed" or "failed" or "cancelled")
            return false;
        task.Cancel();
        AddAuditEvent(ownerId, "tasks", "task.cancelled", "moderate", true, task.Id);
        await CancelPendingApprovalsAsync(task.Id, ownerId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task CancelPendingApprovalsAsync(Guid taskId, Guid ownerId, CancellationToken cancellationToken)
    {
        var approvals = await db.ToolApprovals.Where(x =>
                x.TaskId == taskId && x.OwnerId == ownerId && x.Status == "pending")
            .ToListAsync(cancellationToken);
        foreach (var approval in approvals)
            approval.Cancel();
        await ApprovalInboxCleanup.RemoveAsync(db, approvals.Select(x => x.Id).ToArray(), cancellationToken);
    }

    public async Task<ReminderRecord> CreateAsync(Guid ownerId, string title, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var reminder = new Reminder(ownerId, title, dueAt);
        db.Reminders.Add(reminder);
        AddAuditEvent(ownerId, "reminders", "reminder.created", "low", true, reminder.Id);
        await db.SaveChangesAsync(cancellationToken);
        return reminder.ToRecord();
    }

    public async Task<ReminderRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Reminders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<ReminderRecord>> ListRemindersAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Reminders.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.Status == "pending").ThenBy(x => x.DueAt).Take(300)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToList();

    public async Task<IReadOnlyList<ReminderRecord>> ListPendingForSchedulingAsync(CancellationToken cancellationToken) =>
        (await db.Reminders.AsNoTracking().Where(x => x.Status == "pending" && x.ScheduleDispatchedAt == null)
            .OrderBy(x => x.CreatedAt).Take(300).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task<int> RequeueOverdueDispatchedAsync(DateTimeOffset utcNow, CancellationToken cancellationToken) =>
        await db.Reminders.Where(x => x.Status == "pending" && x.ScheduleDispatchedAt != null &&
                x.DueAt < utcNow.AddMinutes(-Reminder.OverdueRescheduleGraceMinutes))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ScheduleDispatchedAt, (DateTimeOffset?)null),
                cancellationToken);

    public async Task MarkReminderScheduleDispatchedAsync(Guid id, CancellationToken cancellationToken)
    {
        var reminder = await db.Reminders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (reminder is null) return;
        reminder.MarkScheduleDispatched();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReminderRecord?> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reminder = await GetLockedReminderAsync(id, cancellationToken);
        if (reminder is null || reminder.OwnerId != ownerId || reminder.Status != "pending") return null;
        reminder.Cancel();
        AddAuditEvent(ownerId, "reminders", "reminder.cancelled", "low", true, reminder.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reminder.ToRecord();
    }

    public async Task MarkScheduleFailedAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reminder = await GetLockedReminderAsync(id, cancellationToken);
        if (reminder is null || reminder.Status != "pending") return;
        reminder.FailScheduling();
        AddAuditEvent(reminder.OwnerId, "temporal", "reminder.schedule_failed", "high", false, reminder.Id);
        if (!await db.Notifications.AnyAsync(x => x.Id == reminder.Id, cancellationToken))
        {
            var notification = new Notification(reminder.Id, reminder.OwnerId, "reminder.failed",
                "Reminder could not be delivered", reminder.Title, reminder.Id);
            db.Notifications.Add(notification);
            await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteAndNotifyAsync(ReminderWorkflowInput input, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reminder = await GetLockedReminderAsync(input.ReminderId, cancellationToken);
        if (reminder is null || reminder.OwnerId != input.OwnerId || reminder.Status != "pending") return;

        if (!await db.Notifications.AnyAsync(x => x.Id == input.ReminderId, cancellationToken))
        {
            var notification = new Notification(input.ReminderId, input.OwnerId,
                "reminder.due", "Reminder", input.Title, input.ReminderId);
            db.Notifications.Add(notification);
            await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
        }

        reminder.Complete();
        AddAuditEvent(input.OwnerId, "temporal", "reminder.due", "low", true, input.ReminderId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationRecord>> ListNotificationsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Notifications.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task<bool> MarkReadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(
            x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (notification is null) return false;
        notification.MarkRead();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<JarvisTask?> GetLockedTaskAsync(Guid id, CancellationToken cancellationToken) =>
        db.Tasks.FromSqlInterpolated($"SELECT * FROM tasks WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private Task<Reminder?> GetLockedReminderAsync(Guid id, CancellationToken cancellationToken) =>
        db.Reminders.FromSqlInterpolated($"SELECT * FROM reminders WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private void AddAuditEvent(Guid ownerId, string tool, string action, string riskClass,
        bool success, Guid resourceId) =>
        db.AuditEvents.Add(new AuditEvent(ownerId, tool, action, riskClass, success,
            metadataJson: JsonSerializer.Serialize(new { resourceId })));
}
