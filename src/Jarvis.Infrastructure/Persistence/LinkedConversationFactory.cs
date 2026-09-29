using Jarvis.Application.Conversations;
using Jarvis.Domain.Conversations;

namespace Jarvis.Infrastructure.Persistence;

internal static class LinkedConversationFactory
{
    public static (Conversation Conversation, Message Intro) Create(Guid ownerId, string title, string intro)
    {
        var conversation = new Conversation(ownerId, title);
        return (conversation, new Message(conversation.Id, "assistant", intro));
    }
}
