namespace Jarvis.Infrastructure.Persistence;

public sealed class DocumentCollectionFileEntity
{
    public Guid CollectionId { get; set; }
    public Guid FileId { get; set; }
    public Guid OwnerId { get; set; }
}
