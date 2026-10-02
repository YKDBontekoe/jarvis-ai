using System.Text.RegularExpressions;

namespace Jarvis.Application.Channels;

public static class ChannelKinds
{
    /// <summary>WhatsApp Business Cloud API (Meta app, webhook, access token).</summary>
    public const string WhatsApp = "whatsapp";
    /// <summary>WhatsApp linked as a companion device by scanning a QR code (Baileys bridge).</summary>
    public const string WhatsAppLinked = "whatsapp_linked";
    public const string Signal = "signal";

    public static bool IsValid(string? kind) => kind is WhatsApp or WhatsAppLinked or Signal;

    public static bool IsWhatsApp(string? kind) => kind is WhatsApp or WhatsAppLinked;

    /// <summary>Channels linked by scanning a QR code; they have no credentials and no webhook.</summary>
    public static bool IsLinkedDevice(string? kind) => kind is WhatsAppLinked or Signal;

    public static string Label(string? kind) => IsWhatsApp(kind) ? "WhatsApp" : "Signal";

    /// <summary>Secret names each channel stores in the encrypted credential store.</summary>
    public static IReadOnlyList<string> SecretNames(string kind) => kind switch
    {
        WhatsApp => ["access_token", "app_secret", "verify_token"],
        _ => []
    };
}

public sealed record ChannelConnectionRecord(
    Guid Id,
    Guid OwnerId,
    string Kind,
    string DisplayName,
    string Account,
    bool Enabled,
    IReadOnlyList<string> AllowedSenders,
    bool ForwardNotifications,
    string? NotifyRecipient,
    string WebhookKey,
    DateTimeOffset? LastInboundAt,
    DateTimeOffset? LastOutboundAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset NotificationsForwardedUntil,
    IReadOnlyList<string>? NotificationCategories = null);

public sealed record SaveChannelRequest(
    string? Kind,
    string? DisplayName,
    string? Account,
    bool Enabled,
    IReadOnlyList<string>? AllowedSenders,
    bool ForwardNotifications,
    string? NotifyRecipient,
    IReadOnlyDictionary<string, string>? Secrets,
    IReadOnlyList<string>? NotificationCategories = null);

/// <summary>
/// What a channel may forward. A connection stores an explicit list (null = <see cref="Default"/>); approvals are
/// opt-in because most of them can only be decided in the Jarvis app.
/// </summary>
public static class ChannelNotificationCategories
{
    public const string Reminders = "reminders";
    public const string Tasks = "tasks";
    public const string Briefings = "briefings";
    public const string Watches = "watches";
    public const string Automations = "automations";
    public const string Learning = "learning";
    public const string CheckIns = "check_ins";
    public const string Approvals = "approvals";

    public static readonly IReadOnlyList<string> All =
        [Reminders, Tasks, Briefings, Watches, Automations, Learning, CheckIns, Approvals];

    /// <summary>Everything except approvals, matching how forwarding behaved before categories existed.</summary>
    public static readonly IReadOnlyList<string> Default = All.Where(item => item != Approvals).ToArray();

    /// <summary>The category of a notification type, or null for a type this build does not know.</summary>
    public static string? For(string type) => type switch
    {
        "reminder.due" or "reminder.failed" => Reminders,
        "task.completed" or "task.failed" => Tasks,
        "briefing.daily" => Briefings,
        "briefing.weekly" => Briefings,
        "watch.triggered" or "watch.failed" => Watches,
        "automation.notification" => Automations,
        "skill.learned" or "skill.improved" or "learning.dreamed" or "learning.reflected" => Learning,
        "heartbeat.checkin" or "habit.checkin" => CheckIns,
        "approval.required" or "automation.approval" => Approvals,
        _ => null
    };

    public static IReadOnlyList<string> Effective(IReadOnlyList<string>? stored) => stored ?? Default;

    /// <summary>Unknown types are forwarded so new notification kinds are not silently lost.</summary>
    public static bool IsEnabled(IReadOnlyList<string>? stored, string type) =>
        For(type) is not { } category || Effective(stored).Contains(category);

    /// <summary>Lower-cases and de-duplicates; throws <see cref="ArgumentException"/> for an unknown category.</summary>
    public static IReadOnlyList<string>? Normalize(IEnumerable<string>? requested)
    {
        if (requested is null) return null;
        var normalized = requested.Select(item => item?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(item => item.Length > 0).Distinct().ToArray();
        if (normalized.FirstOrDefault(item => !All.Contains(item)) is { } unknown)
            throw new ArgumentException($"{unknown} is not a notification category.");
        return All.Where(normalized.Contains).ToArray();
    }
}

public sealed record ChannelMessageRecord(Guid Id, Guid ConnectionId, string Direction, string Peer, string Text,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt, string? Error);

public sealed record ChannelThreadRecord(string Peer, Guid? ConversationId, int MessageCount,
    ChannelMessageRecord? LastMessage);

public sealed record InboundChannelMessage(Guid Id, ChannelConnectionRecord Connection, string Sender, string Text);

public interface IChannelRepository
{
    Task<IReadOnlyList<ChannelConnectionRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelConnectionRecord>> ListEnabledAsync(string? kind, CancellationToken cancellationToken);
    Task<ChannelConnectionRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<ChannelConnectionRecord?> FindByWebhookKeyAsync(string webhookKey, CancellationToken cancellationToken);
    Task<ChannelConnectionRecord> CreateAsync(Guid ownerId, SaveChannelRequest request, CancellationToken cancellationToken,
        Guid? id = null);
    Task<ChannelConnectionRecord?> UpdateAsync(Guid ownerId, Guid id, SaveChannelRequest request,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);

    /// <summary>Stores an inbound message once per external id; returns false for a duplicate delivery.</summary>
    Task<bool> EnqueueInboundAsync(Guid connectionId, string sender, string text, string externalId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<InboundChannelMessage>> ClaimPendingInboundAsync(int limit, CancellationToken cancellationToken);
    Task CompleteInboundAsync(Guid messageId, string? error, CancellationToken cancellationToken);
    Task RecordOutboundAsync(Guid connectionId, string recipient, string text, string? error,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelMessageRecord>> ListMessagesAsync(Guid ownerId, Guid connectionId, int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelThreadRecord>> ListThreadsAsync(Guid ownerId, Guid connectionId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelMessageRecord>> ListThreadMessagesAsync(Guid ownerId, Guid connectionId, string peer,
        int limit, CancellationToken cancellationToken);
    Task<Guid?> GetThreadConversationAsync(Guid connectionId, string sender, CancellationToken cancellationToken);
    Task SetThreadConversationAsync(Guid connectionId, string sender, Guid conversationId,
        CancellationToken cancellationToken);
    Task AdvanceNotificationWatermarkAsync(Guid connectionId, DateTimeOffset forwardedUntil,
        CancellationToken cancellationToken);
}

public static partial class ChannelAddresses
{
    /// <summary>Normalizes phone numbers to E.164-like digits so allowlists match any formatting.</summary>
    public static string Normalize(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.StartsWith("00", StringComparison.Ordinal)) trimmed = "+" + trimmed[2..];
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? string.Empty : "+" + digits;
    }

    public static bool IsPhoneNumber(string value) => PhonePattern().IsMatch(Normalize(value));

    public static bool IsAllowed(ChannelConnectionRecord connection, string sender)
    {
        var normalized = Normalize(sender);
        return normalized.Length > 0 && connection.AllowedSenders.Any(allowed => Normalize(allowed) == normalized);
    }

    [GeneratedRegex(@"^\+[1-9][0-9]{6,14}$")]
    private static partial Regex PhonePattern();
}
