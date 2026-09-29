namespace Jarvis.Application.Files;

public sealed record DocumentCollectionRecord(
    Guid Id,
    Guid OwnerId,
    string Name,
    string? Description,
    IReadOnlyList<Guid> FileIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DocumentCollectionDraft(string Name, string? Description, IReadOnlyList<Guid>? FileIds = null);

public interface IDocumentCollectionRepository
{
    Task<IReadOnlyList<DocumentCollectionRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<DocumentCollectionRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<DocumentCollectionRecord> CreateAsync(Guid ownerId, DocumentCollectionDraft draft,
        CancellationToken cancellationToken);
    Task<DocumentCollectionRecord?> UpdateAsync(Guid id, Guid ownerId, DocumentCollectionDraft draft,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<bool> AllBelongToOwnerAsync(Guid ownerId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> ListFileIdsAsync(Guid ownerId, IReadOnlyCollection<Guid> collectionIds,
        CancellationToken cancellationToken);
}
