using Jarvis.Application.Workflows;

namespace Jarvis.Application.Notifications;

public static class PushDeliveryOutcomes
{
    public const string ProviderAccepted = "provider_accepted";
    public const string Held = "held";
    public const string AppOnly = "app_only";
}

/// <summary>One phone's push, as stored. <see cref="Outcome"/> is null while it is waiting or being retried.</summary>
public sealed record DeviceDelivery(string? Outcome, int Attempts, DateTimeOffset? At, string? LastError, bool GaveUp);

public sealed record NotificationDeliveryFacts(Guid NotificationId, string Type, string Title, DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt, IReadOnlyList<DeviceDelivery> Devices);

public sealed record DeliveryStep(string State, string Text, DateTimeOffset? At);

public sealed record NotificationDeliveryView(Guid NotificationId, string Title, IReadOnlyList<DeliveryStep> Steps);

public interface INotificationDeliveryReader
{
    /// <summary>Null when the notification is not the owner's.</summary>
    Task<NotificationDeliveryFacts?> GetAsync(Guid ownerId, Guid notificationId, CancellationToken cancellationToken);
}

/// <summary>
/// Says what actually happened to a notification, step by step, without claiming more than Jarvis knows. A push handed
/// to the provider is not a phone showing it: only being opened in the app is observed.
/// </summary>
public static class NotificationDeliveryRules
{
    public static NotificationDeliveryView Describe(NotificationDeliveryFacts facts)
    {
        var steps = new List<DeliveryStep> { new("created", "Created in your notifications.", facts.CreatedAt) };

        if (facts.Devices.Count == 0)
            steps.Add(new DeliveryStep("not_pushed", "No phone was registered for push, so nothing was sent to one.", null));
        else
            foreach (var device in facts.Devices)
                steps.Add(Push(device, facts.Devices.Count > 1));

        steps.Add(facts.ReadAt is { } read
            ? new DeliveryStep("read", "Opened in the app.", read)
            : new DeliveryStep("unread", "Not opened in the app yet.", null));
        return new NotificationDeliveryView(facts.NotificationId, facts.Title, steps);
    }

    private static DeliveryStep Push(DeviceDelivery device, bool several)
    {
        var who = several ? "One phone: " : "";
        return device.Outcome switch
        {
            PushDeliveryOutcomes.ProviderAccepted => new DeliveryStep("provider_accepted",
                who + "Handed to the push service. Jarvis cannot see when a phone shows it.", device.At),
            PushDeliveryOutcomes.Held => new DeliveryStep("held",
                who + "Held back by your current mode. It is in the “while you were away” catch-up.", device.At),
            PushDeliveryOutcomes.AppOnly => new DeliveryStep("app_only",
                who + "Kept in the app, as you chose for this kind. No push was sent.", device.At),
            null when device.At is not null => new DeliveryStep("unrecorded",
                who + "Processed before Jarvis recorded how, so it is not known whether it was pushed or kept in the app.", device.At),
            _ when device.GaveUp => new DeliveryStep("failed",
                who + $"Could not be sent after {device.Attempts} tries" + (device.LastError is null ? "." : $" ({device.LastError})."), device.At),
            _ when device.Attempts > 0 => new DeliveryStep("retrying",
                who + $"Not sent yet; trying again (attempt {device.Attempts})" + (device.LastError is null ? "." : $", last problem: {device.LastError}."), device.At),
            _ => new DeliveryStep("waiting", who + "Waiting to be sent.", null)
        };
    }
}
