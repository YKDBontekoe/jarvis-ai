using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

internal static class ApprovalInboxCleanup
{
    public static async Task RemoveAsync(JarvisDbContext db, IReadOnlyCollection<Guid> approvalIds,
        CancellationToken cancellationToken)
    {
        if (approvalIds.Count == 0) return;
        var notificationIds = await db.Notifications
            .Where(x => x.Type == "approval.required" && x.SourceId != null && approvalIds.Contains(x.SourceId.Value))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (notificationIds.Count == 0) return;
        await db.PushDeliveries.Where(x => notificationIds.Contains(x.NotificationId))
            .ExecuteDeleteAsync(cancellationToken);
        await db.Notifications.Where(x => notificationIds.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
