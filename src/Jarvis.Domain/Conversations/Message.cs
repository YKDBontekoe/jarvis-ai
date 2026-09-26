namespace Jarvis.Domain.Conversations;

public sealed class Message
{
    private Message() { }

    public Message(Guid conversationId, string role, string content, Guid? id = null)
    {
        Id = id ?? Guid.CreateVersion7();
        ConversationId = conversationId;
        Role = role;
        Content = content;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public string Role { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
