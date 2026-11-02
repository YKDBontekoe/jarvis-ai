namespace Jarvis.Domain.Conversations;

public sealed class Message
{
    private Message() { }

    public Message(Guid conversationId, string role, string content, Guid? id = null, string? citationsJson = null,
        string? attachmentsJson = null, string? clientMessageId = null, string? clientPayloadHash = null)
    {
        Id = id ?? Guid.CreateVersion7();
        ConversationId = conversationId;
        Role = role;
        Content = content;
        CitationsJson = citationsJson;
        AttachmentsJson = attachmentsJson;
        ClientMessageId = clientMessageId;
        ClientPayloadHash = clientPayloadHash;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public string Role { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string? CitationsJson { get; private set; }

    /// <summary>Photos sent with a user message: file id, name, and content type. Never the image bytes.</summary>
    public string? AttachmentsJson { get; private set; }

    /// <summary>
    /// The id the sending client gave this user message. Unique within its conversation, so a retry of the
    /// same send finds the original message instead of starting another turn. Null for legacy requests.
    /// </summary>
    public string? ClientMessageId { get; private set; }

    /// <summary>Fingerprint of what was sent under <see cref="ClientMessageId"/>; a retry must match it.</summary>
    public string? ClientPayloadHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetCitationsJson(string? citationsJson) => CitationsJson = citationsJson;
}
