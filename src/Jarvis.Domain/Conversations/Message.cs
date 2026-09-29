namespace Jarvis.Domain.Conversations;

public sealed class Message
{
    private Message() { }

    public Message(Guid conversationId, string role, string content, Guid? id = null, string? citationsJson = null)
    {
        Id = id ?? Guid.CreateVersion7();
        ConversationId = conversationId;
        Role = role;
        Content = content;
        CitationsJson = citationsJson;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public string Role { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string? CitationsJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetCitationsJson(string? citationsJson) => CitationsJson = citationsJson;
}
