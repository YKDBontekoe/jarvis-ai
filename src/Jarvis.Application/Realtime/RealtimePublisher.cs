namespace Jarvis.Application.Realtime;

/// <summary>Fan-out of owner or conversation events. Implementations may no-op in workers that have no SignalR hub.</summary>
public interface IRealtimePublisher
{
    Task PublishToConversationAsync(Guid conversationId, string eventName, object payload,
        CancellationToken cancellationToken);
    Task PublishToOwnerAsync(Guid ownerId, string eventName, object payload, CancellationToken cancellationToken);
}

public sealed class NoOpRealtimePublisher : IRealtimePublisher
{
    public Task PublishToConversationAsync(Guid conversationId, string eventName, object payload,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishToOwnerAsync(Guid ownerId, string eventName, object payload,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
