using System.Text.RegularExpressions;

namespace Jarvis.Application.Channels;

public static class ChannelKinds
{
    public const string WhatsApp = "whatsapp";
    public const string Signal = "signal";

    public static bool IsValid(string? kind) => kind is WhatsApp or Signal;

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
    DateTimeOffset NotificationsForwardedUntil);

public sealed record SaveChannelRequest(
    string? Kind,
    string? DisplayName,
    string? Account,
    bool Enabled,
    IReadOnlyList<string>? AllowedSenders,
    bool ForwardNotifications,
    string? NotifyRecipient,
    IReadOnlyDictionary<string, string>? Secrets);

public sealed record ChannelMessageRecord(Guid Id, Guid ConnectionId, string Direction, string Peer, string Text,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt, string? Error);

public sealed record InboundChannelMessage(Guid Id, ChannelConnectionRecord Connection, string Sender, string Text);

public interface IChannelRepository
{
    Task<IReadOnlyList<ChannelConnectionRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelConnectionRecord>> ListEnabledAsync(string? kind, CancellationToken cancellationToken);
    Task<ChannelConnectionRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<ChannelConnectionRecord?> FindByWebhookKeyAsync(string webhookKey, CancellationToken cancellationToken);
    Task<ChannelConnectionRecord> CreateAsync(Guid ownerId, SaveChannelRequest request, CancellationToken cancellationToken);
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
