using Jarvis.Domain.Approvals;
using Jarvis.Domain.Workflows;

namespace Jarvis.Application.Workflows;

public sealed record PushDeliveryKey(Guid NotificationId, Guid DeviceId);

/// <summary>A claimed push delivery that is ready to send. <see cref="Data"/> carries ids only, never tokens.</summary>
public sealed record PushDeliveryWork(
    PushDeliveryKey Key,
    int Attempts,
    Guid OwnerId,
    string DeviceToken,
    Notification Notification,
    IReadOnlyDictionary<string, string> Data);

/// <summary>
/// The outcome of trying to lease one push delivery: <see cref="Claimed"/> is false when another worker holds it,
/// and <see cref="Work"/> is null when the delivery was claimed but dropped because its notification, device, or
/// pending approval no longer exists.
/// </summary>
public sealed record PushDeliveryClaim(bool Claimed, PushDeliveryWork? Work);

/// <summary>Leased, retried queue of push deliveries written by <see cref="INotificationRepository.CreateAsync"/>.</summary>
public interface IPushDeliveryQueue
{
    public const int MaxAttempts = 10;

    Task<IReadOnlyList<PushDeliveryKey>> ListDueAsync(int maxCount, CancellationToken cancellationToken);
    Task<PushDeliveryClaim> TryClaimAsync(PushDeliveryKey key, TimeSpan lease, CancellationToken cancellationToken);
    Task MarkDeliveredAsync(PushDeliveryKey key, CancellationToken cancellationToken);
    Task RecordFailureAsync(PushDeliveryKey key, int attempts, string error, CancellationToken cancellationToken);

    /// <summary>Removes a device whose token the push provider rejected, along with its queued deliveries.</summary>
    Task RemoveDeviceAsync(Guid deviceId, CancellationToken cancellationToken);
}

public sealed record NotificationCursor(DateTimeOffset CreatedAt, Guid Id);

/// <summary>Reads notifications in creation order for real-time fan-out across all owners.</summary>
public interface INotificationFeed
{
    Task<NotificationCursor?> GetLatestCursorAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Notification>> ListAfterAsync(NotificationCursor? cursor, int maxCount,
        CancellationToken cancellationToken);
    Task<ToolApproval?> FindPendingApprovalAsync(Guid ownerId, Guid approvalId, CancellationToken cancellationToken);
}
