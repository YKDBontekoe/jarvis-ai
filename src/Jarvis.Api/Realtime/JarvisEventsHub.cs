using Jarvis.Application.Conversations;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Realtime;

public sealed class JarvisEventsHub(
    IConversationStore conversations,
    ICurrentUser currentUser) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, OwnerGroupName(currentUser.OwnerId),
            Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public async Task JoinConversation(Guid conversationId)
    {
        var cancellationToken = Context.ConnectionAborted;
        if (await conversations.GetAsync(conversationId, currentUser.OwnerId, cancellationToken) is null)
            throw new HubException("Conversation not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(conversationId), cancellationToken);
    }

    public static string GroupName(Guid conversationId) => $"conversation:{conversationId:N}";
    public static string OwnerGroupName(Guid ownerId) => $"owner:{ownerId:N}";
}
