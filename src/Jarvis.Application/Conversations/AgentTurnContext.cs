namespace Jarvis.Application.Conversations;

/// <summary>Ambient conversation for the in-flight agent turn, used by tools that persist UI or browser steps.</summary>
public sealed class AgentTurnContext
{
    private static readonly AsyncLocal<Guid?> Conversation = new();

    public Guid? ConversationId => Conversation.Value;

    public IDisposable Begin(Guid conversationId)
    {
        var previous = Conversation.Value;
        Conversation.Value = conversationId;
        return new Reset(previous);
    }

    private sealed class Reset(Guid? previous) : IDisposable
    {
        public void Dispose() => Conversation.Value = previous;
    }
}
