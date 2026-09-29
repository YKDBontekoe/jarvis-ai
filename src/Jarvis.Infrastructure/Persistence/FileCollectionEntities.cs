namespace Jarvis.Infrastructure.Persistence;

public sealed class FileCollectionEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FileCollectionMemberEntity
{
    public Guid CollectionId { get; set; }
    public Guid FileId { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

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
