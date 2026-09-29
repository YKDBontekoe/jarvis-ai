using Jarvis.Application.Files;
using Jarvis.Domain.Files;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class DocumentCollectionRepository(JarvisDbContext db) : IDocumentCollectionRepository
{
    public async Task<IReadOnlyList<DocumentCollectionRecord>> ListAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var collections = await db.DocumentCollections.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var files = await db.DocumentCollectionFiles.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
        var byCollection = files.GroupBy(item => item.CollectionId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.FileId).ToArray());
        return collections.Select(item => ToRecord(item, byCollection.GetValueOrDefault(item.Id, []))).ToArray();
    }

    public async Task<DocumentCollectionRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var collection = await db.DocumentCollections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (collection is null) return null;
        var fileIds = await db.DocumentCollectionFiles.AsNoTracking()
            .Where(x => x.CollectionId == id && x.OwnerId == ownerId)
            .Select(x => x.FileId).ToArrayAsync(cancellationToken);
        return ToRecord(collection, fileIds);
    }

    public async Task<DocumentCollectionRecord> CreateAsync(Guid ownerId, DocumentCollectionDraft draft,
        CancellationToken cancellationToken)
    {
        var name = NormalizeName(draft.Name);
        var description = NormalizeDescription(draft.Description);
        if (await db.DocumentCollections.CountAsync(x => x.OwnerId == ownerId, cancellationToken) >=
            DocumentCollection.MaxCollectionsPerOwner)
            throw new ArgumentException(
                $"Keep at most {DocumentCollection.MaxCollectionsPerOwner} collections; delete unused ones first.");
        if (await db.DocumentCollections.AnyAsync(x => x.OwnerId == ownerId && x.Name == name, cancellationToken))
            throw new ArgumentException($"A collection named {name} already exists.");

        var collection = new DocumentCollection(ownerId, name, description);
        db.DocumentCollections.Add(collection);
        await ReplaceFilesAsync(ownerId, collection.Id, draft.FileIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await GetAsync(collection.Id, ownerId, cancellationToken))!;
    }

    public async Task<DocumentCollectionRecord?> UpdateAsync(Guid id, Guid ownerId, DocumentCollectionDraft draft,
        CancellationToken cancellationToken)
    {
        var collection = await db.DocumentCollections.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (collection is null) return null;
        var name = NormalizeName(draft.Name);
        var description = NormalizeDescription(draft.Description);
        if (await db.DocumentCollections.AnyAsync(
                x => x.OwnerId == ownerId && x.Name == name && x.Id != id, cancellationToken))
            throw new ArgumentException($"A collection named {name} already exists.");
        collection.Rename(name, description);
        await ReplaceFilesAsync(ownerId, id, draft.FileIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, ownerId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var collection = await db.DocumentCollections.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (collection is null) return false;
        db.DocumentCollections.Remove(collection);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AllBelongToOwnerAsync(Guid ownerId, IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return true;
        var distinct = ids.Distinct().ToArray();
        var count = await db.DocumentCollections.CountAsync(x => x.OwnerId == ownerId && distinct.Contains(x.Id),
            cancellationToken);
        return count == distinct.Length;
    }

    public async Task<IReadOnlySet<Guid>> ListFileIdsAsync(Guid ownerId, IReadOnlyCollection<Guid> collectionIds,
        CancellationToken cancellationToken)
    {
        if (collectionIds.Count == 0) return new HashSet<Guid>();
        var ids = collectionIds.Distinct().ToArray();
        return (await db.DocumentCollectionFiles.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && ids.Contains(x.CollectionId))
                .Select(x => x.FileId).Distinct().ToListAsync(cancellationToken))
            .ToHashSet();
    }

    private async Task ReplaceFilesAsync(Guid ownerId, Guid collectionId, IReadOnlyList<Guid>? fileIds,
        CancellationToken cancellationToken)
    {
        var requested = (fileIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (requested.Length > 0)
        {
            var owned = await db.Files.CountAsync(x => x.OwnerId == ownerId && requested.Contains(x.Id),
                cancellationToken);
            if (owned != requested.Length)
                throw new ArgumentException("Every file in a collection must belong to the same owner.");
        }

        var existing = await db.DocumentCollectionFiles.Where(x => x.CollectionId == collectionId).ToListAsync(
            cancellationToken);
        db.DocumentCollectionFiles.RemoveRange(existing);
        foreach (var fileId in requested)
            db.DocumentCollectionFiles.Add(new DocumentCollectionFileEntity
            {
                CollectionId = collectionId, FileId = fileId, OwnerId = ownerId
            });
    }

    private static string NormalizeName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > DocumentCollection.MaxNameLength)
            throw new ArgumentException($"Name must contain 1 to {DocumentCollection.MaxNameLength} characters.");
        return value;
    }

    private static string? NormalizeDescription(string? description)
    {
        var value = description?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > DocumentCollection.MaxDescriptionLength)
            throw new ArgumentException(
                $"Keep the description under {DocumentCollection.MaxDescriptionLength} characters.");
        return value;
    }

    private static DocumentCollectionRecord ToRecord(DocumentCollection collection, IReadOnlyList<Guid> fileIds) =>
        new(collection.Id, collection.OwnerId, collection.Name, collection.Description, fileIds,
            collection.CreatedAt, collection.UpdatedAt);
}
