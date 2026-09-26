namespace Jarvis.Domain.Conversations;

public sealed class AgentSessionState
{
    private AgentSessionState() { }

    public AgentSessionState(Guid conversationId, string state)
    {
        ConversationId = conversationId;
        State = state;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid ConversationId { get; private set; }
    public string State { get; private set; } = "{}";
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Replace(string state)
    {
        State = state;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
