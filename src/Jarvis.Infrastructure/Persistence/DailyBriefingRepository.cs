using System.Text.Json;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class DailyBriefingRepository(JarvisDbContext db) : IDailyBriefingRepository
{
    public async Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.DailyBriefings.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<(DailyBriefingPreferenceRecord Preference, string PreviousWorkflowId)> SaveAsync(
        Guid ownerId, SaveDailyBriefingRequest request, CancellationToken cancellationToken)
    {
        var preference = await db.DailyBriefings.SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);
        string previousWorkflowId;
        if (preference is null)
        {
            preference = new DailyBriefingPreference(ownerId, request.Enabled, request.LocalTime, request.TimeZoneId);
            previousWorkflowId = string.Empty;
            db.DailyBriefings.Add(preference);
        }
        else
        {
            previousWorkflowId = preference.Update(request.Enabled, request.LocalTime, request.TimeZoneId);
        }

        db.AuditEvents.Add(new AuditEvent(ownerId, "briefings", "briefing.settings_updated", "low", true,
            metadataJson: JsonSerializer.Serialize(new { request.Enabled, request.LocalTime, request.TimeZoneId })));
        await db.SaveChangesAsync(cancellationToken);
        return (preference.ToRecord(), previousWorkflowId);
    }

    public async Task<IReadOnlyList<DailyBriefingPreferenceRecord>> ListPendingForSchedulingAsync(
        CancellationToken cancellationToken) =>
        (await db.DailyBriefings.AsNoTracking().Where(x => x.ScheduleDispatchedAt == null)
            .OrderBy(x => x.UpdatedAt).Take(200).ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToArray();

    public async Task<int> RequeueStaleEnabledAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var dispatched = await db.DailyBriefings.AsNoTracking()
            .Where(x => x.Enabled && x.ScheduleDispatchedAt != null)
            .Take(200).ToListAsync(cancellationToken);
        var staleIds = dispatched
            .Where(x => DailyBriefingClock.IsDispatchStale(x.Enabled, x.ScheduleDispatchedAt, x.LocalTime,
                x.TimeZoneId, x.LastDeliveredDate, utcNow))
            .Select(x => x.OwnerId)
            .ToList();
        if (staleIds.Count == 0) return 0;
        return await db.DailyBriefings.Where(x => staleIds.Contains(x.OwnerId) && x.Enabled)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ScheduleDispatchedAt, (DateTimeOffset?)null),
                cancellationToken);
    }

    public async Task MarkScheduleDispatchedAsync(Guid ownerId, string workflowId,
        CancellationToken cancellationToken)
    {
        var preference = await db.DailyBriefings.SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);
        if (preference is null || preference.WorkflowId != workflowId || preference.ScheduleDispatchedAt is not null) return;
        preference.MarkScheduleDispatched();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeliverAsync(DailyBriefingActivityInput input, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var preference = await db.DailyBriefings
            .FromSqlInterpolated($"SELECT * FROM daily_briefings WHERE owner_id = {input.OwnerId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (preference is null || preference.WorkflowId != input.WorkflowId || !preference.Enabled) return false;
        if (!preference.MarkDelivered(input.LocalDate)) return true;

        var reminders = await db.Reminders.AsNoTracking()
            .Where(x => x.OwnerId == input.OwnerId && x.Status == "pending" &&
                x.DueAt >= input.LocalDayStart && x.DueAt < input.NextLocalDayStart)
            .OrderBy(x => x.DueAt).Take(8).ToListAsync(cancellationToken);
        var activeTasks = await db.Tasks.AsNoTracking()
            .Where(x => x.OwnerId == input.OwnerId &&
                (x.Status == "queued" || x.Status == "running" || x.Status == "needs_approval"))
            .OrderBy(x => x.CreatedAt).Take(5).ToListAsync(cancellationToken);

        var lines = new List<string>();
        if (reminders.Count == 0) lines.Add("No reminders are due today.");
        else
        {
            lines.Add("Today's reminders:");
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
            lines.AddRange(reminders.Select(x =>
                $"• {TimeZoneInfo.ConvertTime(x.DueAt, timeZone):HH:mm} — {x.Title}"));
        }
        if (activeTasks.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Active tasks:");
            lines.AddRange(activeTasks.Select(x => $"• {x.Title} ({x.Status.Replace('_', ' ')})"));
        }
        var body = string.Join('\n', lines);
        if (body.Length > 2_000) body = body[..1_997] + "…";

        var notification = new Notification(Guid.CreateVersion7(), input.OwnerId,
            "briefing.daily", "Your morning briefing", body, null);
        db.Notifications.Add(notification);
        await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
        db.AuditEvents.Add(new AuditEvent(input.OwnerId, "briefings", "briefing.delivered", "low", true,
            metadataJson: JsonSerializer.Serialize(new { date = input.LocalDate })));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

internal static class DailyBriefingMapping
{
    public static DailyBriefingPreferenceRecord ToRecord(this DailyBriefingPreference preference) =>
        new(preference.OwnerId, preference.Enabled, preference.LocalTime, preference.TimeZoneId,
            preference.WorkflowId, preference.ScheduleDispatchedAt, preference.LastDeliveredDate);
}
