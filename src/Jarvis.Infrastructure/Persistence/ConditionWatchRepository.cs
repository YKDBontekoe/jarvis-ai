using System.Text.Json;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ConditionWatchRepository(JarvisDbContext db) : IConditionWatchRepository
{
    public async Task<ConditionWatchRecord> CreateAsync(Guid ownerId, CreateConditionWatchRequest request,
        CancellationToken cancellationToken)
    {
        var watch = new ConditionWatch(ownerId, request.Title.Trim(), request.Url, request.JsonPath,
            request.Comparison, request.Threshold, request.IntervalMinutes);
        db.ConditionWatches.Add(watch);
        db.AuditEvents.Add(new AuditEvent(ownerId, "condition_watches", "watch.created", "low", true,
            metadataJson: JsonSerializer.Serialize(new { resourceId = watch.Id })));
        await db.SaveChangesAsync(cancellationToken);
        return watch.ToRecord();
    }

    public async Task<ConditionWatchRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.ConditionWatches.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<ConditionWatchRecord?> GetForExecutionAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.ConditionWatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<ConditionWatchRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.ConditionWatches.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Status == "active" ? 0 : 1).ThenByDescending(x => x.CreatedAt)
            .Take(200).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToList();

    public async Task<IReadOnlyList<ConditionWatchRecord>> ListPendingForSchedulingAsync(CancellationToken cancellationToken) =>
        (await db.ConditionWatches.AsNoTracking().Where(x => x.Status == "active" && x.ScheduleDispatchedAt == null)
            .OrderBy(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToList();

    public async Task<int> RequeueStaleActiveAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var grace = ConditionWatch.ScheduleStaleGraceMinutes;
        return await db.ConditionWatches.Where(x => x.Status == "active" &&
                x.ScheduleDispatchedAt != null &&
                (x.LastCheckedAt ?? x.ScheduleDispatchedAt)!.Value
                    .AddMinutes(x.IntervalMinutes + grace) < utcNow)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ScheduleDispatchedAt, (DateTimeOffset?)null),
                cancellationToken);
    }

    public async Task MarkScheduleDispatchedAsync(Guid id, CancellationToken cancellationToken)
    {
        var watch = await db.ConditionWatches.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (watch is null || watch.Status != "active") return;
        watch.MarkScheduleDispatched();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var watch = await GetLockedAsync(id, cancellationToken);
        if (watch is null || watch.OwnerId != ownerId || watch.Status != "active") return false;
        watch.Cancel();
        db.AuditEvents.Add(new AuditEvent(ownerId, "condition_watches", "watch.cancelled", "low", true,
            metadataJson: JsonSerializer.Serialize(new { resourceId = watch.Id })));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<ConditionWatchCheckResult> RecordCheckAsync(Guid id, double value,
        DateTimeOffset checkedAt, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var watch = await GetLockedAsync(id, cancellationToken);
        if (watch is null || watch.Status != "active")
            return new ConditionWatchCheckResult(false, 15);

        var triggered = watch.RecordCheck(value, checkedAt);
        if (triggered)
        {
            var notification = new Notification(Guid.CreateVersion7(), watch.OwnerId, "watch.triggered",
                "Condition met", watch.Title, watch.Id);
            db.Notifications.Add(notification);
            await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
            db.AuditEvents.Add(new AuditEvent(watch.OwnerId, "condition_watches", "watch.triggered", "low", true,
                metadataJson: JsonSerializer.Serialize(new { resourceId = watch.Id, value })));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ConditionWatchCheckResult(watch.Status == "active", watch.IntervalMinutes);
    }

    public async Task MarkFailedAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var watch = await GetLockedAsync(id, cancellationToken);
        if (watch is null || watch.Status != "active") return;
        watch.Fail();
        var notification = new Notification(Guid.CreateVersion7(), watch.OwnerId, "watch.failed",
            "Condition watch stopped", watch.Title, watch.Id);
        db.Notifications.Add(notification);
        await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
        db.AuditEvents.Add(new AuditEvent(watch.OwnerId, "condition_watches", "watch.failed", "moderate", false,
            metadataJson: JsonSerializer.Serialize(new { resourceId = watch.Id })));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<ConditionWatch?> GetLockedAsync(Guid id, CancellationToken cancellationToken) =>
        db.ConditionWatches.FromSqlInterpolated($"SELECT * FROM condition_watches WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
}

internal static class ConditionWatchMapping
{
    public static ConditionWatchRecord ToRecord(this ConditionWatch watch) => new(watch.Id, watch.OwnerId,
        watch.Title, watch.Url, watch.JsonPath, watch.Comparison, watch.Threshold, watch.IntervalMinutes,
        watch.WorkflowId, watch.Status, watch.CreatedAt, watch.LastCheckedAt, watch.LastValue);
}
