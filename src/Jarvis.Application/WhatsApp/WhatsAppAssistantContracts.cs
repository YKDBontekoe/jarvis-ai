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
    DateTimeOffset SentAt, DateTimeOffset? ReceivedAt = null, string? SenderId = null,
    WhatsAppMedia? Media = null, WhatsAppQuote? Quote = null);

/// <summary>A message the bridge forwarded for a chat on the owner's watch list.</summary>
public sealed record ObservedWhatsAppMessage(string ExternalId, string ChatId, bool FromMe, string? Sender,
    string Text, DateTimeOffset SentAt, string? SenderId = null, string? MediaJson = null, byte[]? Content = null,
    string? ContentType = null, bool Historical = false, string? CanonicalChatId = null);

/// <summary>
/// New messages in one chat that the reminder scan has not looked at yet, with the context before them.
/// Completing the scan with <see cref="ScannedThrough"/> marks exactly these messages as read.
/// </summary>
public sealed record WhatsAppScanBatch(WhatsAppChatSettings Chat, IReadOnlyList<WhatsAppChatMessage> Context,
    IReadOnlyList<WhatsAppChatMessage> NewMessages, DateTimeOffset ScannedThrough);

public sealed record WhatsAppSearchHit(WhatsAppChatSettings Chat, WhatsAppChatMessage Message);

/// <summary>Activity already saved in Jarvis; unread counts do not change WhatsApp read receipts.</summary>
/// <param name="CatchUp">Summary of the unread messages, only while it still covers something unread.</param>
public sealed record WhatsAppChatActivity(string ChatId, string? Preview, bool? FromMe, int UnreadCount,
    WhatsAppCatchUp? CatchUp = null);

/// <summary>Something in a chat the owner should answer: who asked, and what about.</summary>
public sealed record WhatsAppReplyItem(string Who, string About);

/// <summary>
/// A short summary of the unread messages in one read-along chat and what the owner should reply to. Written in
/// the background once a chat goes quiet; it disappears when the owner reads past it.
/// </summary>
public sealed record WhatsAppCatchUp(string Summary, IReadOnlyList<WhatsAppReplyItem> ToReply, int MessageCount,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Unread messages in one chat to summarize, with a little context before them. Completing with
/// <see cref="Through"/> marks exactly these messages as summarized.
/// </summary>
public sealed record WhatsAppCatchUpBatch(WhatsAppChatSettings Chat, IReadOnlyList<WhatsAppChatMessage> Context,
    IReadOnlyList<WhatsAppChatMessage> Unread, DateTimeOffset Through);

public interface IWhatsAppAssistantRepository
{
    /// <summary>Consolidates verified phone/LID aliases on one owned connection without removing messages.</summary>
    Task<bool> MergeChatAliasesAsync(Guid ownerId, Guid connectionId, string phoneId,
        IReadOnlyList<string> aliases, CancellationToken cancellationToken);
    Task<WhatsAppChatMessage?> GetMessageAsync(Guid ownerId, Guid connectionId, string chatId, Guid messageId,
        CancellationToken cancellationToken);
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

    /// <summary>Bytes for one stored photo, sticker, voice note, document or video preview. Owner scoped.</summary>
    Task<(byte[] Content, string ContentType)?> OpenMediaAsync(Guid ownerId, Guid connectionId, Guid messageId,
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

    /// <summary>
    /// Claims read-along chats that have been quiet for <paramref name="quiet"/>, received something since their
    /// last catch-up, and have at least <paramref name="minUnread"/> unread messages from other people. Chats
    /// below that bar are completed without a summary so they are not looked at again until a new message.
    /// </summary>
    Task<IReadOnlyList<WhatsAppCatchUpBatch>> ClaimCatchUpBatchesAsync(TimeSpan quiet, int minUnread, int limit,
        int contextSize, CancellationToken cancellationToken);

    /// <summary>Stores the catch-up (or clears it when null) and releases the claim.</summary>
    Task CompleteCatchUpAsync(Guid chatSettingsId, DateTimeOffset through, WhatsAppCatchUp? catchUp,
        CancellationToken cancellationToken);
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

    /// <summary>Summarizes unread messages and lists what the owner should reply to; null when it could not.</summary>
    Task<WhatsAppCatchUp?> SummarizeUnreadAsync(Guid ownerId, WhatsAppCatchUpBatch batch,
        CancellationToken cancellationToken);
}

public sealed record WhatsAppSendResult(bool Sent, string? ExternalId, string? Error);

/// <summary>Sends from the owner's own linked WhatsApp. Only the API host can send; workers get a no-op.</summary>
public interface IWhatsAppSender
{
    bool Available { get; }
    Task<WhatsAppSendResult> SendAsync(Guid ownerId, Guid connectionId, string chatId, string text,
        CancellationToken cancellationToken);

    /// <summary>Sends <paramref name="text"/> as a WhatsApp reply that quotes <paramref name="replyTo"/>.</summary>
    Task<WhatsAppSendResult> ReplyAsync(Guid ownerId, Guid connectionId, string chatId, string text,
        WhatsAppChatMessage replyTo, CancellationToken cancellationToken);

    /// <summary>Reacts to a saved message with one emoji; an empty emoji takes the reaction back.</summary>
    Task<WhatsAppSendResult> ReactAsync(Guid ownerId, Guid connectionId, string chatId, WhatsAppChatMessage target,
        string emoji, CancellationToken cancellationToken);
}

public sealed class NoOpWhatsAppSender : IWhatsAppSender
{
    private static readonly Task<WhatsAppSendResult> Unavailable =
        Task.FromResult(new WhatsAppSendResult(false, null, "Sending WhatsApp messages is not available here."));

    public bool Available => false;

    public Task<WhatsAppSendResult> SendAsync(Guid ownerId, Guid connectionId, string chatId, string text,
        CancellationToken cancellationToken) => Unavailable;

    public Task<WhatsAppSendResult> ReplyAsync(Guid ownerId, Guid connectionId, string chatId, string text,
        WhatsAppChatMessage replyTo, CancellationToken cancellationToken) => Unavailable;

    public Task<WhatsAppSendResult> ReactAsync(Guid ownerId, Guid connectionId, string chatId,
        WhatsAppChatMessage target, string emoji, CancellationToken cancellationToken) => Unavailable;
}

public static partial class WhatsAppChatIds
{
    public const int MaxDisplayNameLength = 80;
    public const int MaxMessageLength = 4_000;

    /// <summary>
    /// Normalizes a chat id: a phone number becomes "+digits"; group (@g.us) and unresolved (@lid) jids stay as
    /// they are, without a device or domain suffix. Percent-encoding is decoded first, so a chat id that arrived
    /// still encoded matches the id that was saved. Returns null for anything else.
    /// </summary>
    public static string? Normalize(string? value)
    {
        var trimmed = DecodeToken(Unescape(value ?? string.Empty).Trim());
        if (trimmed is null) return null;
        var canonical = CanonicalJid(trimmed);
        if (GroupPattern().IsMatch(canonical) || LidPattern().IsMatch(canonical)) return canonical;
        if (canonical.Contains('@')) return null;
        var phone = ChannelAddresses.Normalize(canonical);
        return ChannelAddresses.IsPhoneNumber(phone) ? phone : null;
    }

    /// <summary>Drops ":device" and "_agent" from a jid and lowercases the server. Values without @ are unchanged.</summary>
    private static string CanonicalJid(string value)
    {
        var at = value.LastIndexOf('@');
        if (at <= 0) return value;
        var user = value[..at];
        var server = value[(at + 1)..].ToLowerInvariant();
        var device = user.IndexOf(':');
        if (device > 0) user = user[..device];
        var agent = user.LastIndexOf('_');
        if (agent > 0 && user[(agent + 1)..].All(char.IsAsciiDigit)) user = user[..agent];
        return string.IsNullOrEmpty(user) || string.IsNullOrEmpty(server) ? value : $"{user}@{server}";
    }

    /// <summary>
    /// Group ids contain <c>@</c>. Clients send those as <c>b64.</c> plus base64url so a proxy that treats
    /// <c>@</c> as user-info cannot drop the id from the message request. Plain ids stay valid.
    /// </summary>
    private static string? DecodeToken(string value)
    {
        if (!value.StartsWith("b64.", StringComparison.Ordinal)) return value;
        if (value.Length is < 8 or > 200) return null;
        try
        {
            var encoded = value[4..].Replace('-', '+').Replace('_', '/');
            encoded = (encoded.Length % 4) switch { 2 => encoded + "==", 3 => encoded + "=", _ => encoded };
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Trim();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Unescape(string value)
    {
        var current = value.Trim();
        for (var attempt = 0; attempt < 2 && current.Contains('%'); attempt++)
        {
            try
            {
                var decoded = Uri.UnescapeDataString(current);
                if (decoded == current) return current;
                current = decoded.Trim();
            }
            catch (UriFormatException)
            {
                return current;
            }
        }
        return current;
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
