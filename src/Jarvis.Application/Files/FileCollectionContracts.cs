using Jarvis.Domain.Files;

namespace Jarvis.Application.Files;

public static class FileCollectionNames
{
    public const int MaxNameLength = 120;

    public static string Normalize(string name) =>
        string.Join(' ', name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
}

public sealed record FileCollectionMember(Guid CollectionId, Guid FileId, Guid OwnerId, DateTimeOffset AddedAt);

public sealed record ConversationFileAttachment(Guid ConversationId, Guid FileId, Guid OwnerId, DateTimeOffset AttachedAt);

public sealed record ConversationCollectionAttachment(Guid ConversationId, Guid CollectionId, Guid OwnerId,
    DateTimeOffset AttachedAt);

public sealed record ConversationFileSources(
    IReadOnlyList<ConversationFileAttachment> Files,
    IReadOnlyList<ConversationCollectionAttachment> Collections);

public interface IFileCollectionRepository
{
    Task<FileCollection> CreateAsync(Guid ownerId, string name, CancellationToken cancellationToken);
    Task<FileCollection?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FileCollection>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<bool> RenameAsync(Guid id, Guid ownerId, string name, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> AddFileAsync(Guid collectionId, Guid fileId, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> RemoveFileAsync(Guid collectionId, Guid fileId, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredFile>> ListFilesAsync(Guid collectionId, Guid ownerId, CancellationToken cancellationToken);
}

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
