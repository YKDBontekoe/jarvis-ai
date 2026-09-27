using Jarvis.Application.Conversations;
using Jarvis.Application.Devices;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Realtime;

public sealed class JarvisEventsHub(
    IConversationStore conversations,
    ICurrentUser currentUser,
    IDeviceInvoker devices) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, OwnerGroupName(currentUser.OwnerId),
            Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        devices.Unregister(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task JoinConversation(Guid conversationId)
    {
        var cancellationToken = Context.ConnectionAborted;
        if (await conversations.GetAsync(conversationId, currentUser.OwnerId, cancellationToken) is null)
            throw new HubException("Conversation not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(conversationId), cancellationToken);
    }

    public Task RegisterDevice(string? name, IReadOnlyList<string>? capabilities)
    {
        devices.Register(currentUser.OwnerId, Context.ConnectionId, name ?? "Device", capabilities ?? []);
        return Task.CompletedTask;
    }

    public Task CompleteDeviceInvoke(Guid invokeId, string? result, string? error)
    {
        devices.Complete(invokeId, result, error);
        return Task.CompletedTask;
    }

    public static string GroupName(Guid conversationId) => $"conversation:{conversationId:N}";
    public static string OwnerGroupName(Guid ownerId) => $"owner:{ownerId:N}";
}
