using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

internal static class PushDeliveryQueue
{
    public static async Task<IReadOnlyList<PushDelivery>> QueueAsync(JarvisDbContext db, Notification notification,
        CancellationToken cancellationToken)
    {
        var deviceIds = await db.PushDevices.AsNoTracking()
            .Where(x => x.OwnerId == notification.OwnerId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var deliveries = deviceIds.Select(deviceId => new PushDelivery(notification.Id, deviceId)).ToList();
        db.PushDeliveries.AddRange(deliveries);
        return deliveries;
    }
}
