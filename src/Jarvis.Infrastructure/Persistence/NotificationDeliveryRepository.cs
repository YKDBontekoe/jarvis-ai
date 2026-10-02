using Jarvis.Application.Workflows;
using Jarvis.Domain.Approvals;
using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class PushDeliveryRepository(JarvisDbContext db) : IPushDeliveryQueue
{
    public async Task<IReadOnlyList<PushDeliveryKey>> ListDueAsync(int maxCount, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.PushDeliveries.AsNoTracking()
            .Where(x => x.DeliveredAt == null && x.Attempts < IPushDeliveryQueue.MaxAttempts &&
                        x.NextAttemptAt <= now && (x.LeaseUntil == null || x.LeaseUntil < now))
            .OrderBy(x => x.NextAttemptAt)
            .Select(x => new PushDeliveryKey(x.NotificationId, x.DeviceId))
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<PushDeliveryClaim> TryClaimAsync(PushDeliveryKey key, TimeSpan lease,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now.Add(lease);
        var claimed = await Delivery(key)
            .Where(x => x.DeliveredAt == null && x.Attempts < IPushDeliveryQueue.MaxAttempts &&
                        x.NextAttemptAt <= now && (x.LeaseUntil == null || x.LeaseUntil < now))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                .SetProperty(x => x.LeaseUntil, leaseUntil), cancellationToken);
        if (claimed == 0) return new PushDeliveryClaim(false, null);

        var delivery = await Delivery(key).AsNoTracking().SingleAsync(cancellationToken);
        var notification = await db.Notifications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == key.NotificationId, cancellationToken);
        var device = await db.PushDevices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == key.DeviceId, cancellationToken);
        var stale = notification is null || device is null || device.OwnerId != notification.OwnerId ||
                    await IsResolvedApprovalAsync(notification, cancellationToken);
        if (stale)
        {
            await Delivery(key).ExecuteDeleteAsync(cancellationToken);
            return new PushDeliveryClaim(true, null);
        }

        var data = await BuildPushDataAsync(notification!, cancellationToken);
        return new PushDeliveryClaim(true,
            new PushDeliveryWork(key, delivery.Attempts, device!.OwnerId, device.Token, notification!, data));
    }

    public Task MarkDeliveredAsync(PushDeliveryKey key, CancellationToken cancellationToken) =>
        Delivery(key).ExecuteUpdateAsync(update => update
            .SetProperty(x => x.DeliveredAt, DateTimeOffset.UtcNow)
            .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null)
            .SetProperty(x => x.LastError, (string?)null), cancellationToken);

    public async Task RecordFailureAsync(PushDeliveryKey key, int attempts, string error,
        CancellationToken cancellationToken)
    {
        var failed = await Delivery(key).SingleAsync(cancellationToken);
        failed.Fail(DateTimeOffset.UtcNow, error, permanent: attempts >= IPushDeliveryQueue.MaxAttempts);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await db.PushDeliveries.Where(x => x.DeviceId == deviceId).ExecuteDeleteAsync(cancellationToken);
        await db.PushDevices.Where(x => x.Id == deviceId).ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<PushDelivery> Delivery(PushDeliveryKey key) =>
        db.PushDeliveries.Where(x => x.NotificationId == key.NotificationId && x.DeviceId == key.DeviceId);

    private async Task<bool> IsResolvedApprovalAsync(Notification notification, CancellationToken cancellationToken) =>
        notification.Type == "approval.required" && notification.SourceId is { } approvalId &&
        !await db.ToolApprovals.AsNoTracking()
            .AnyAsync(x => x.Id == approvalId && x.Status == "pending", cancellationToken);

    private async Task<Dictionary<string, string>> BuildPushDataAsync(Notification notification,
        CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, string>
        {
            ["notificationId"] = notification.Id.ToString("D"),
            ["type"] = notification.Type,
            ["sourceId"] = notification.SourceId?.ToString("D") ?? string.Empty
        };
        if (notification.SourceId is not Guid sourceId) return data;

        Guid? conversationId = notification.Type switch
        {
            "reminder.due" or "reminder.failed" => await db.Reminders.AsNoTracking()
                .Where(x => x.Id == sourceId && x.OwnerId == notification.OwnerId)
                .Select(x => x.ConversationId)
                .FirstOrDefaultAsync(cancellationToken),
            "automation.notification" => await db.AutomationRules.AsNoTracking()
                .Where(x => x.Id == sourceId && x.OwnerId == notification.OwnerId)
                .Select(x => x.ConversationId)
                .FirstOrDefaultAsync(cancellationToken),
            "automation.approval" => await (
                from run in db.AutomationRuns.AsNoTracking()
                join rule in db.AutomationRules.AsNoTracking() on run.RuleId equals rule.Id
                where run.Id == sourceId && run.OwnerId == notification.OwnerId
                select rule.ConversationId).FirstOrDefaultAsync(cancellationToken),
            _ => null
        };
        if (conversationId is Guid id)
        {
            data["routeKind"] = "conversation";
            data["conversationId"] = id.ToString("D");
        }

        return data;
    }
}

public sealed class NotificationFeed(JarvisDbContext db) : INotificationFeed
{
    public async Task<NotificationCursor?> GetLatestCursorAsync(CancellationToken cancellationToken)
    {
        var latest = await db.Notifications.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.CreatedAt, x.Id })
            .FirstOrDefaultAsync(cancellationToken);
        return latest is null ? null : new NotificationCursor(latest.CreatedAt, latest.Id);
    }

    public async Task<IReadOnlyList<Notification>> ListAfterAsync(NotificationCursor? cursor, int maxCount,
        CancellationToken cancellationToken)
    {
        var query = db.Notifications.AsNoTracking();
        if (cursor is not null)
        {
            var createdAt = cursor.CreatedAt;
            var id = cursor.Id;
            query = query.Where(x => x.CreatedAt > createdAt || (x.CreatedAt == createdAt && x.Id.CompareTo(id) > 0));
        }

        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(maxCount).ToListAsync(cancellationToken);
    }

    public Task<ToolApproval?> FindPendingApprovalAsync(Guid ownerId, Guid approvalId,
        CancellationToken cancellationToken) =>
        db.ToolApprovals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == approvalId && x.OwnerId == ownerId && x.Status == "pending",
                cancellationToken);
}
