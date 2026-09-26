using Jarvis.Application.Conversations;

namespace Jarvis.Worker;

internal sealed class WorkerCurrentUser : ICurrentUser
{
    public Guid OwnerId { get; private set; }

    public void SetOwner(Guid ownerId)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("Task owner ID is required.", nameof(ownerId));
        OwnerId = ownerId;
    }
}
