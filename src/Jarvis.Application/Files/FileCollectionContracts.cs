using Jarvis.Application.Files;

namespace Jarvis.Application.Files;

public sealed record ConversationFileAttachment(Guid ConversationId, Guid FileId, Guid OwnerId, DateTimeOffset AttachedAt);

public sealed record ConversationCollectionAttachment(Guid ConversationId, Guid CollectionId, Guid OwnerId,
    DateTimeOffset AttachedAt);

public sealed record ConversationFileSources(
    IReadOnlyList<ConversationFileAttachment> Files,
    IReadOnlyList<ConversationCollectionAttachment> Collections);

public interface IConversationFileContextRepository
{
    Task<ConversationFileSources> GetSourcesAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> AttachFileAsync(Guid conversationId, Guid fileId, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> DetachFileAsync(Guid conversationId, Guid fileId, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> AttachCollectionAsync(Guid conversationId, Guid collectionId, Guid ownerId,
        CancellationToken cancellationToken);
    Task<bool> DetachCollectionAsync(Guid conversationId, Guid collectionId, Guid ownerId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ResolveScopedFileIdsAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken);
}
