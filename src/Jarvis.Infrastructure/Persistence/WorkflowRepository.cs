using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Approvals;
using Jarvis.Domain.Audit;
using Jarvis.Application.Profiles;
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
        CancellationToken cancellationToken, ProfileBinding? profile = null, Guid? projectId = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var conversation = new Conversation(ownerId, title);
        if (profile is not null)
            conversation.BindProfile(profile.ProfileId, profile.Version, profile.SnapshotJson);
        if (projectId is { } project)
        {
            if (!await db.Projects.AnyAsync(x => x.Id == project && x.OwnerId == ownerId, cancellationToken))
                throw new ArgumentException("Project was not found.", nameof(projectId));
            conversation.MoveToProject(project);
        }
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
        await CancelIncompleteApprovalsAsync(task.Id, task.OwnerId, cancellationToken);
        if (!await db.Notifications.AnyAsync(x => x.Id == task.Id, cancellationToken))
        {
            var notification = new Notification(task.Id, task.OwnerId, "task.completed",
                "Task finished", TaskFinishedBody(task.Title, summary), task.Id);
            db.Notifications.Add(notification);
            await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The task title plus the first line of its result, so the notification is useful on its own.</summary>
    public static string TaskFinishedBody(string title, string summary)
    {
        const int maxPreview = 160;
        var firstLine = (summary ?? string.Empty)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.TrimStart('-', '*', '>', ' ').Replace("**", string.Empty))
            .FirstOrDefault(line => line.Length > 0);
        if (firstLine is null) return title;
        if (firstLine.Length > maxPreview) firstLine = firstLine[..(maxPreview - 1)].TrimEnd() + "…";
        return $"{title}: {firstLine}";
    }

    public async Task FailAsync(Guid id, string summary, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var task = await GetLockedTaskAsync(id, cancellationToken);
        if (task is null || task.Status is "completed" or "failed" or "cancelled") return;
        task.Fail(summary);
        AddAuditEvent(task.OwnerId, "temporal", "task.failed", "high", false, task.Id);
        await CancelIncompleteApprovalsAsync(task.Id, task.OwnerId, cancellationToken);
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
        await CancelIncompleteApprovalsAsync(task.Id, ownerId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task CancelIncompleteApprovalsAsync(Guid taskId, Guid ownerId, CancellationToken cancellationToken)
    {
        var approvals = await db.ToolApprovals.Where(x =>
                x.TaskId == taskId && x.OwnerId == ownerId &&
                (x.Status == "pending" ||
                 ((x.Status == "approved" || x.Status == "rejected") &&
                  x.ResumeStatus != "completed" && x.ResumeStatus != "cancelled")))
            .ToListAsync(cancellationToken);
        foreach (var approval in approvals)
        {
            await db.Entry(approval).ReloadAsync(cancellationToken);
            approval.Cancel();
            approval.AbortResume();
        }
        await ApprovalInboxCleanup.RemoveAsync(db, approvals.Select(x => x.Id).ToArray(), cancellationToken);
    }

    public async Task<ReminderRecord> CreateAsync(Guid ownerId, CreateReminderRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reminder = request.Place is { } place
            ? Reminder.ForPlace(ownerId, request.Title, place.Name, place.Latitude, place.Longitude,
                place.RadiusMeters, place.Trigger, place.Repeats, request.TimeZoneId ?? "UTC")
            : new Reminder(ownerId, request.Title, request.DueAt, request.Recurrence ?? Reminder.RecurrenceNone,
                request.Weekdays, request.TimeZoneId ?? "UTC", request.LocalTime, request.Until);
        if (reminder.IsLocationBased)
            await SeedPlaceStateAsync(reminder, cancellationToken);
        var (conversation, intro) = LinkedConversationFactory.Create(ownerId, reminder.Title,
            LinkedConversationCopy.ReminderIntro(reminder.Title));
        reminder.AttachConversation(conversation.Id);
        db.Conversations.Add(conversation);
        db.Reminders.Add(reminder);
        db.Messages.Add(intro);
        conversation.Touch();
        AddAuditEvent(ownerId, "reminders", "reminder.created", "low", true, reminder.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reminder.ToRecord();
    }

    public async Task<ReminderRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var reminder = await db.Reminders.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (reminder is null) return null;
        if (reminder.ConversationId is null)
            await AttachReminderConversationAsync(reminder, cancellationToken);
        return reminder.ToRecord();
    }

    public async Task<ReminderRecord?> GetByConversationIdAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.Reminders.AsNoTracking().SingleOrDefaultAsync(x =>
            x.ConversationId == conversationId && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<ReminderRecord>> ListRemindersAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Reminders.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.Status == "pending").ThenBy(x => x.DueAt).Take(300)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToList();

    public async Task<IReadOnlyList<ReminderRecord>> ListPendingForSchedulingAsync(CancellationToken cancellationToken) =>
        (await db.Reminders.AsNoTracking().Where(x => x.Status == "pending" && x.ScheduleDispatchedAt == null &&
                x.LocationLatitude == null)
            .OrderBy(x => x.CreatedAt).Take(300).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task<int> RequeueOverdueDispatchedAsync(DateTimeOffset utcNow, CancellationToken cancellationToken) =>
        await db.Reminders.Where(x => x.Status == "pending" && x.ScheduleDispatchedAt != null &&
                x.LocationLatitude == null && x.DueAt < utcNow.AddMinutes(-Reminder.OverdueRescheduleGraceMinutes))
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

    public async Task<(ReminderRecord Reminder, string PreviousWorkflowId)?> SnoozeAsync(Guid id, Guid ownerId,
        DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reminder = await GetLockedReminderAsync(id, cancellationToken);
        if (reminder is null || reminder.OwnerId != ownerId || reminder.IsRecurring || reminder.LocationRepeats ||
            reminder.Status is not ("pending" or "completed")) return null;
        var previousWorkflowId = reminder.WorkflowId;
        var zone = LocalClock.TryFind(reminder.TimeZoneId, out var found) ? found : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(dueAt, zone);
        reminder.Snooze(dueAt, TimeOnly.FromTimeSpan(local.TimeOfDay));
        AddAuditEvent(ownerId, "reminders", "reminder.snoozed", "low", true, reminder.Id);
        await PostReminderMessageAsync(reminder, LinkedConversationCopy.ReminderSnoozed(reminder.Title,
            local.ToString("ddd d MMM, HH:mm", System.Globalization.CultureInfo.InvariantCulture)), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (reminder.ToRecord(), previousWorkflowId);
    }

    public async Task<ReminderRecord?> MarkDoneAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reminder = await GetLockedReminderAsync(id, cancellationToken);
        if (reminder is null || reminder.OwnerId != ownerId || reminder.IsRecurring || reminder.LocationRepeats ||
            reminder.Status != "pending")
            return null;
        reminder.MarkDone();
        AddAuditEvent(ownerId, "reminders", "reminder.completed", "low", true, reminder.Id);
        await PostReminderMessageAsync(reminder, LinkedConversationCopy.ReminderMarkedDone(reminder.Title),
            cancellationToken);
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
        await PostReminderMessageAsync(reminder, LinkedConversationCopy.ReminderFailed(reminder.Title),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ReminderDeliveryResult> CompleteAndNotifyAsync(ReminderWorkflowInput input, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reminder = await GetLockedReminderAsync(input.ReminderId, cancellationToken);
        if (reminder is null || reminder.OwnerId != input.OwnerId || reminder.IsLocationBased)
            return new ReminderDeliveryResult(false, input.DueAt, input.Title);
        var deliveredEarlier = reminder.LastDeliveredAt is { } lastDelivered &&
                               lastDelivered >= input.DueAt.AddMinutes(-1);
        if (reminder.Status != "pending")
            return new ReminderDeliveryResult(false, reminder.DueAt, reminder.Title,
                reminder.Status == "completed" && deliveredEarlier);
        if (reminder.DueAt > input.DueAt.AddMinutes(1))
            return new ReminderDeliveryResult(reminder.IsRecurring, reminder.DueAt, reminder.Title, deliveredEarlier);

        // The title carries what to do, because that is the line a phone shows on the lock screen.
        var notification = new Notification(Guid.CreateVersion7(), input.OwnerId,
            "reminder.due", LimitTitle(reminder.Title), ReminderDueBody(reminder), input.ReminderId);
        db.Notifications.Add(notification);
        await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);

        DateTimeOffset? nextDueAt = null;
        if (reminder.IsRecurring)
        {
            var rule = ReminderSchedule.FromRecord(reminder.ToRecord());
            nextDueAt = ReminderSchedule.NextAfter(input.DueAt, rule);
        }

        reminder.CompleteOccurrence(DateTimeOffset.UtcNow, nextDueAt);
        AddAuditEvent(input.OwnerId, "temporal", "reminder.due", "low", true, input.ReminderId);
        await PostReminderMessageAsync(reminder, LinkedConversationCopy.ReminderDue(reminder.Title), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ReminderDeliveryResult(reminder.Status == "pending" && nextDueAt is not null,
            nextDueAt ?? reminder.DueAt, reminder.Title, true);
    }

    public async Task<IReadOnlyList<FiredPlaceReminder>> ObservePositionAsync(Guid ownerId, double latitude,
        double longitude, double? accuracyMeters, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        // Cheap unlocked check first: most fixes arrive for owners without any place reminder.
        var candidateIds = await db.Reminders.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.Status == "pending" && x.LocationLatitude != null)
            .OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(200).ToListAsync(cancellationToken);
        if (candidateIds.Count == 0) return [];

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reminders = await db.Reminders.FromSqlInterpolated(
                $"SELECT * FROM reminders WHERE \"Id\" = ANY({candidateIds.ToArray()}) ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(cancellationToken);
        var fired = new List<FiredPlaceReminder>();
        foreach (var reminder in reminders.Where(x => x.OwnerId == ownerId && x.IsLocationBased))
        {
            var distance = GeoDistance.Meters(latitude, longitude, reminder.LocationLatitude!.Value,
                reminder.LocationLongitude!.Value);
            if (!reminder.ObservePosition(distance, accuracyMeters, observedAt)) continue;

            var notification = new Notification(Guid.CreateVersion7(), ownerId, "reminder.due",
                LimitTitle(reminder.Title), PlaceReminderBody(reminder), reminder.Id);
            db.Notifications.Add(notification);
            await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
            reminder.CompleteLocationOccurrence(observedAt);
            AddAuditEvent(ownerId, "reminders", "reminder.due", "low", true, reminder.Id);
            await PostReminderMessageAsync(reminder, LinkedConversationCopy.ReminderAtPlace(reminder.Title,
                reminder.LocationName ?? "the place", reminder.LocationTrigger == Reminder.LocationLeave),
                cancellationToken);
            fired.Add(new FiredPlaceReminder(reminder.Id, ownerId, reminder.Title, observedAt));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return fired;
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

    public async Task<NotificationRecord> CreateAsync(Guid ownerId, string type, string title, string body,
        Guid? sourceId, CancellationToken cancellationToken)
    {
        var notification = new Notification(Guid.CreateVersion7(), ownerId, type,
            title.Length <= 300 ? title : title[..300], body.Length <= 2_000 ? body : body[..2_000], sourceId);
        db.Notifications.Add(notification);
        await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new NotificationRecord(notification.Id, notification.Type, notification.Title, notification.Body,
            notification.SourceId, notification.CreatedAt, notification.ReadAt);
    }

    private static string LimitTitle(string title) => title.Length <= 300 ? title : title[..299] + "…";

    private static string ReminderDueBody(Reminder reminder)
    {
        if (!reminder.IsRecurring) return "Reminder · due now";
        var rule = ReminderSchedule.Describe(reminder.ToRecord());
        return string.IsNullOrEmpty(rule) ? "Reminder · due now" : $"Reminder · repeats {rule}";
    }

    private static string PlaceReminderBody(Reminder reminder)
    {
        var place = reminder.LocationName ?? "the place";
        var edge = reminder.LocationTrigger == Reminder.LocationLeave ? $"You left {place}" : $"You're at {place}";
        return reminder.LocationRepeats ? $"Reminder · {edge} · every visit" : $"Reminder · {edge}";
    }

    /// <summary>Uses a fresh phone position, when there is one, so a reminder made at the place does not fire there.</summary>
    private async Task SeedPlaceStateAsync(Reminder reminder, CancellationToken cancellationToken)
    {
        var fix = await db.DeviceTelemetry.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == reminder.OwnerId,
            cancellationToken);
        if (fix is not { Latitude: double latitude, Longitude: double longitude } ||
            fix.ReportedAt < DateTimeOffset.UtcNow.AddMinutes(-15)) return;
        reminder.SeedLocationState(GeoDistance.Meters(latitude, longitude, reminder.LocationLatitude!.Value,
            reminder.LocationLongitude!.Value), fix.AccuracyMeters);
    }

    private Task<JarvisTask?> GetLockedTaskAsync(Guid id, CancellationToken cancellationToken) =>
        db.Tasks.FromSqlInterpolated($"SELECT * FROM tasks WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private Task<Reminder?> GetLockedReminderAsync(Guid id, CancellationToken cancellationToken) =>
        db.Reminders.FromSqlInterpolated($"SELECT * FROM reminders WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task AttachReminderConversationAsync(Reminder reminder, CancellationToken cancellationToken)
    {
        if (reminder.ConversationId is not null) return;
        var (conversation, intro) = LinkedConversationFactory.Create(reminder.OwnerId, reminder.Title,
            LinkedConversationCopy.ReminderIntro(reminder.Title));
        reminder.AttachConversation(conversation.Id);
        db.Conversations.Add(conversation);
        db.Messages.Add(intro);
        conversation.Touch();
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task PostReminderMessageAsync(Reminder reminder, string content, CancellationToken cancellationToken)
    {
        await AttachReminderConversationAsync(reminder, cancellationToken);
        if (reminder.ConversationId is not Guid conversationId) return;
        db.Messages.Add(new Message(conversationId, "assistant", content));
        var conversation = await db.Conversations.SingleOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
        conversation?.Touch();
    }

    private void AddAuditEvent(Guid ownerId, string tool, string action, string riskClass,
        bool success, Guid resourceId) =>
        db.AuditEvents.Add(new AuditEvent(ownerId, tool, action, riskClass, success,
            metadataJson: JsonSerializer.Serialize(new { resourceId })));
}
