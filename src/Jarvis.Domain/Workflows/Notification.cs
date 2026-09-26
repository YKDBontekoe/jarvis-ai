namespace Jarvis.Domain.Workflows;

public sealed class Notification
{
    private Notification() { }

    public Notification(Guid id, Guid ownerId, string type, string title, string body, Guid? sourceId)
    {
        Id = id;
        OwnerId = ownerId;
        Type = type;
        Title = title;
        Body = body;
        SourceId = sourceId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public Guid? SourceId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead() => ReadAt ??= DateTimeOffset.UtcNow;
}
