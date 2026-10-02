using Jarvis.Application.Channels;

namespace Jarvis.Application.WhatsApp;

/// <summary>
/// The owner's choices for one chat on their linked WhatsApp ("read along"). Nothing is read until
/// <see cref="ReadAlong"/> is turned on, and groups start off like every other chat.
/// </summary>
public sealed record WhatsAppChatSettings(
    Guid Id,
    Guid OwnerId,
    Guid ConnectionId,
    string ChatId,
    string DisplayName,
    bool IsGroup,
    bool ReadAlong,
    bool AutoReminders,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? AskConversationId = null);

/// <summary>A message in a chat the owner reads along with. <see cref="Sender"/> is null for the owner's own.</summary>
public sealed record WhatsAppChatMessage(
    Guid Id,
    Guid ConnectionId,
    string ChatId,
    string ExternalId,
    bool FromMe,
    string? Sender,
    string Text,
    DateTimeOffset SentAt, DateTimeOffset? ReceivedAt = null);

/// <summary>A message the bridge forwarded for a chat on the owner's watch list.</summary>
public sealed record ObservedWhatsAppMessage(string ExternalId, string ChatId, bool FromMe, string? Sender,
    string Text, DateTimeOffset SentAt);

/// <summary>
/// New messages in one chat that the reminder scan has not looked at yet, with the context before them.
/// Completing the scan with <see cref="ScannedThrough"/> marks exactly these messages as read.
/// </summary>
public sealed record WhatsAppScanBatch(WhatsAppChatSettings Chat, IReadOnlyList<WhatsAppChatMessage> Context,
    IReadOnlyList<WhatsAppChatMessage> NewMessages, DateTimeOffset ScannedThrough);

public sealed record WhatsAppSearchHit(WhatsAppChatSettings Chat, WhatsAppChatMessage Message);

/// <summary>Activity already saved in Jarvis; unread counts do not change WhatsApp read receipts.</summary>
public sealed record WhatsAppChatActivity(string ChatId, string? Preview, bool? FromMe, int UnreadCount);

public interface IWhatsAppAssistantRepository
{
    Task<IReadOnlyList<WhatsAppChatSettings>> ListChatsAsync(Guid ownerId, Guid? connectionId,
        CancellationToken cancellationToken);
    Task<WhatsAppChatSettings?> GetChatAsync(Guid ownerId, Guid connectionId, string chatId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<WhatsAppChatActivity>> ListActivityAsync(Guid ownerId, Guid connectionId,
        CancellationToken cancellationToken);
    /// <summary>Marks through a message the client actually loaded, without marking later arrivals as read.</summary>
    Task<bool> MarkReadAsync(Guid ownerId, Guid connectionId, string chatId, Guid messageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates the owner's settings for a chat; null when the connection is not the owner's linked
    /// WhatsApp.
    /// </summary>
    Task<WhatsAppChatSettings?> SaveChatAsync(Guid ownerId, Guid connectionId, string chatId, string displayName,
        bool readAlong, bool autoReminders, CancellationToken cancellationToken);

    /// <summary>Chat ids the bridge should forward for a connection (read along on).</summary>
    Task<IReadOnlyList<string>> ListWatchedChatIdsAsync(Guid connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores forwarded messages once per external id, only for chats that still have read along on. Returns how
    /// many were new.
    /// </summary>
    Task<int> StoreObservedAsync(Guid connectionId, IReadOnlyList<ObservedWhatsAppMessage> messages,
        CancellationToken cancellationToken);

    /// <summary>Newest first, up to <paramref name="limit"/>, optionally before a point in time.</summary>
    Task<IReadOnlyList<WhatsAppChatMessage>> ListMessagesAsync(Guid ownerId, Guid connectionId, string chatId,
        int limit, DateTimeOffset? before, CancellationToken cancellationToken, Guid? beforeId = null);
    Task<IReadOnlyList<WhatsAppSearchHit>> SearchAsync(Guid ownerId, string query, Guid? connectionId,
        string? chatId, int limit, CancellationToken cancellationToken);
    Task<int> ClearHistoryAsync(Guid ownerId, Guid connectionId, string chatId, CancellationToken cancellationToken);

    /// <summary>Remembers the Jarvis conversation used for "Ask Jarvis" about this chat.</summary>
    Task SetAskConversationAsync(Guid ownerId, Guid chatSettingsId, Guid conversationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims chats with auto reminders on whose newest unscanned message is older than <paramref name="quiet"/>,
    /// so a burst of messages is read as one conversation.
    /// </summary>
    Task<IReadOnlyList<WhatsAppScanBatch>> ClaimScanBatchesAsync(TimeSpan quiet, int limit, int contextSize,
        CancellationToken cancellationToken);
    Task CompleteScanAsync(Guid chatSettingsId, DateTimeOffset scannedThrough, CancellationToken cancellationToken);
}

/// <summary>A reminder the scan found in a chat.</summary>
public sealed record WhatsAppReminderSuggestion(string Title, DateTimeOffset DueAt);

/// <summary>Model-backed helpers for the read-along chats. Chat content is always treated as untrusted data.</summary>
public interface IWhatsAppAssistant
{
    /// <summary>Drafts a reply in the owner's voice; <paramref name="instruction"/> is the owner's own steer.</summary>
    Task<string?> SuggestReplyAsync(Guid ownerId, WhatsAppChatSettings chat,
        IReadOnlyList<WhatsAppChatMessage> messages, string? instruction, CancellationToken cancellationToken);

    /// <summary>Finds appointments and promises in the new messages that deserve a reminder.</summary>
    Task<IReadOnlyList<WhatsAppReminderSuggestion>> FindRemindersAsync(Guid ownerId, WhatsAppScanBatch batch,
        IReadOnlyList<string> existingReminders, DateTimeOffset now, string timeZoneId,
        CancellationToken cancellationToken);
}

public sealed record WhatsAppSendResult(bool Sent, string? ExternalId, string? Error);

/// <summary>Sends from the owner's own linked WhatsApp. Only the API host can send; workers get a no-op.</summary>
public interface IWhatsAppSender
{
    bool Available { get; }
    Task<WhatsAppSendResult> SendAsync(Guid ownerId, Guid connectionId, string chatId, string text,
        CancellationToken cancellationToken);
}

public sealed class NoOpWhatsAppSender : IWhatsAppSender
{
    public bool Available => false;

    public Task<WhatsAppSendResult> SendAsync(Guid ownerId, Guid connectionId, string chatId, string text,
        CancellationToken cancellationToken) =>
        Task.FromResult(new WhatsAppSendResult(false, null, "Sending WhatsApp messages is not available here."));
}

public static partial class WhatsAppChatIds
{
    public const int MaxDisplayNameLength = 80;
    public const int MaxMessageLength = 4_000;

    /// <summary>
    /// Normalizes a chat id: a phone number becomes "+digits"; group (@g.us) and unresolved (@lid) jids stay as
    /// they are. Returns null for anything else.
    /// </summary>
    public static string? Normalize(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (GroupPattern().IsMatch(trimmed) || LidPattern().IsMatch(trimmed)) return trimmed;
        if (trimmed.Contains('@')) return null;
        var phone = ChannelAddresses.Normalize(trimmed);
        return ChannelAddresses.IsPhoneNumber(phone) ? phone : null;
    }

    public static bool IsGroup(string chatId) => chatId.EndsWith("@g.us", StringComparison.Ordinal);

    /// <summary>A readable fallback name when WhatsApp gave none.</summary>
    public static string FallbackName(string chatId) =>
        IsGroup(chatId) ? "Group chat" : chatId.EndsWith("@lid", StringComparison.Ordinal) ? "WhatsApp contact" : chatId;

    public static string CleanName(string? name, string chatId)
    {
        var clean = string.Join(' ', (name ?? string.Empty).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) return FallbackName(chatId);
        return clean.Length <= MaxDisplayNameLength ? clean : clean[..MaxDisplayNameLength].TrimEnd();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[0-9-]{5,64}@g\.us$")]
    private static partial System.Text.RegularExpressions.Regex GroupPattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[0-9]{5,32}@lid$")]
    private static partial System.Text.RegularExpressions.Regex LidPattern();
}
