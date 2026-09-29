namespace Jarvis.Infrastructure.Persistence;

public sealed class ConversationFileAttachmentEntity
{
    public Guid ConversationId { get; set; }
    public Guid FileId { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset AttachedAt { get; set; }
}

public sealed class ConversationCollectionAttachmentEntity
{
    public Guid ConversationId { get; set; }
    public Guid CollectionId { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset AttachedAt { get; set; }
}
