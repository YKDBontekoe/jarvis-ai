namespace Jarvis.Domain.Workflows;

public sealed class PushDelivery
{
    private PushDelivery() { }

    public PushDelivery(Guid notificationId, Guid deviceId)
    {
        NotificationId = notificationId;
        DeviceId = deviceId;
        NextAttemptAt = DateTimeOffset.UtcNow;
    }

    public Guid NotificationId { get; private set; }
    public Guid DeviceId { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>How the push ended: <c>provider_accepted</c>, <c>held</c> (a mode kept it back) or <c>app_only</c>
    /// (the owner chose to keep this kind in the app). Null while it is still waiting or being retried.</summary>
    public string? Outcome { get; private set; }

    public void Fail(DateTimeOffset now, string error, bool permanent)
    {
        LeaseUntil = null;
        LastError = error.Length <= 500 ? error : error[..500];
        NextAttemptAt = permanent ? DateTimeOffset.MaxValue : now.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, Math.Min(Attempts, 10))));
    }
}
