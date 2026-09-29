using Jarvis.Application.Files;
using Jarvis.Domain.Files;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class FileCollectionRepository(JarvisDbContext db) : IFileCollectionRepository
{
    public async Task<FileCollection> CreateAsync(Guid ownerId, string name, CancellationToken cancellationToken)
    {
        var trimmed = ValidateName(name);
        var normalized = FileCollectionNames.Normalize(trimmed);
        if (await db.FileCollections.AsNoTracking()
                .AnyAsync(x => x.OwnerId == ownerId && x.NormalizedName == normalized, cancellationToken))
            throw new InvalidOperationException("A collection with that name already exists.");

        var entity = new FileCollectionEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = trimmed,
            NormalizedName = normalized,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.FileCollections.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(entity);
    }

    public async Task<FileCollection?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.FileCollections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken)) is { } entity
            ? ToRecord(entity)
            : null;

    public async Task<IReadOnlyList<FileCollection>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.FileCollections.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.NormalizedName).ToListAsync(cancellationToken))
        .Select(ToRecord).ToArray();

    public async Task<bool> RenameAsync(Guid id, Guid ownerId, string name, CancellationToken cancellationToken)
    {
        var trimmed = ValidateName(name);
        var normalized = FileCollectionNames.Normalize(trimmed);
        if (await db.FileCollections.AsNoTracking()
                .AnyAsync(x => x.OwnerId == ownerId && x.NormalizedName == normalized && x.Id != id, cancellationToken))
            throw new InvalidOperationException("A collection with that name already exists.");

        return await db.FileCollections.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Name, trimmed)
                .SetProperty(x => x.NormalizedName, normalized), cancellationToken) != 0;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.FileCollections.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) != 0;

    public async Task<bool> AddFileAsync(Guid collectionId, Guid fileId, Guid ownerId, CancellationToken cancellationToken)
    {
        var collection = await db.FileCollections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == collectionId && x.OwnerId == ownerId, cancellationToken);
        if (collection is null) return false;
        var file = await db.Files.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == fileId && x.OwnerId == ownerId, cancellationToken);
        if (file is null || file.ProcessingStatus == "deleting") return false;
        if (await db.FileCollectionMembers.AsNoTracking()
                .AnyAsync(x => x.CollectionId == collectionId && x.FileId == fileId, cancellationToken))
            return true;

        db.FileCollectionMembers.Add(new FileCollectionMemberEntity
        {
            CollectionId = collectionId,
            FileId = fileId,
            OwnerId = ownerId,
            AddedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveFileAsync(Guid collectionId, Guid fileId, Guid ownerId, CancellationToken cancellationToken) =>
        await db.FileCollectionMembers.Where(x => x.CollectionId == collectionId && x.FileId == fileId && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) != 0;

    public async Task<IReadOnlyList<StoredFile>> ListFilesAsync(Guid collectionId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (!await db.FileCollections.AsNoTracking()
                .AnyAsync(x => x.Id == collectionId && x.OwnerId == ownerId, cancellationToken))
            return [];

        var fileIds = await db.FileCollectionMembers.AsNoTracking()
            .Where(x => x.CollectionId == collectionId && x.OwnerId == ownerId)
            .Select(x => x.FileId).ToListAsync(cancellationToken);
        return (await db.Files.AsNoTracking().Where(x => x.OwnerId == ownerId && fileIds.Contains(x.Id))
                .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken))
            .Select(x => x.ToRecord()).ToArray();
    }

    private static string ValidateName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > FileCollectionNames.MaxNameLength)
            throw new ArgumentException($"Collection name must contain 1 to {FileCollectionNames.MaxNameLength} characters.");
        return trimmed;
    }

    private static FileCollection ToRecord(FileCollectionEntity entity) =>
        new(entity.Id, entity.OwnerId, entity.Name, entity.CreatedAt);
}

public sealed class ConversationFileContextRepository(JarvisDbContext db) : IConversationFileContextRepository
{
    public async Task<ConversationFileSources> GetSourcesAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (!await OwnsConversationAsync(conversationId, ownerId, cancellationToken))
            return new ConversationFileSources([], []);

        var files = await db.ConversationFileAttachments.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.OwnerId == ownerId)
            .OrderBy(x => x.AttachedAt).ToListAsync(cancellationToken);
        var collections = await db.ConversationCollectionAttachments.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.OwnerId == ownerId)
            .OrderBy(x => x.AttachedAt).ToListAsync(cancellationToken);
        return new ConversationFileSources(
            files.Select(x => new ConversationFileAttachment(x.ConversationId, x.FileId, x.OwnerId, x.AttachedAt))
                .ToArray(),
            collections.Select(x =>
                    new ConversationCollectionAttachment(x.ConversationId, x.CollectionId, x.OwnerId, x.AttachedAt))
                .ToArray());
    }

    public async Task<bool> AttachFileAsync(Guid conversationId, Guid fileId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (!await OwnsConversationAsync(conversationId, ownerId, cancellationToken)) return false;
        var file = await db.Files.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == fileId && x.OwnerId == ownerId, cancellationToken);
        if (file is null || file.ProcessingStatus == "deleting") return false;
        if (await db.ConversationFileAttachments.AsNoTracking()
                .AnyAsync(x => x.ConversationId == conversationId && x.FileId == fileId, cancellationToken))
            return true;

        db.ConversationFileAttachments.Add(new ConversationFileAttachmentEntity
        {
            ConversationId = conversationId,
            FileId = fileId,
            OwnerId = ownerId,
            AttachedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DetachFileAsync(Guid conversationId, Guid fileId, Guid ownerId,
        CancellationToken cancellationToken) =>
        await db.ConversationFileAttachments
            .Where(x => x.ConversationId == conversationId && x.FileId == fileId && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) != 0;

    public async Task<bool> AttachCollectionAsync(Guid conversationId, Guid collectionId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (!await OwnsConversationAsync(conversationId, ownerId, cancellationToken)) return false;
        if (!await db.FileCollections.AsNoTracking()
                .AnyAsync(x => x.Id == collectionId && x.OwnerId == ownerId, cancellationToken))
            return false;
        if (await db.ConversationCollectionAttachments.AsNoTracking()
                .AnyAsync(x => x.ConversationId == conversationId && x.CollectionId == collectionId, cancellationToken))
            return true;

        db.ConversationCollectionAttachments.Add(new ConversationCollectionAttachmentEntity
        {
            ConversationId = conversationId,
            CollectionId = collectionId,
            OwnerId = ownerId,
            AttachedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DetachCollectionAsync(Guid conversationId, Guid collectionId, Guid ownerId,
        CancellationToken cancellationToken) =>
        await db.ConversationCollectionAttachments
            .Where(x => x.ConversationId == conversationId && x.CollectionId == collectionId && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) != 0;

    public async Task<IReadOnlyList<Guid>> ResolveScopedFileIdsAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (!await OwnsConversationAsync(conversationId, ownerId, cancellationToken)) return [];

        var direct = await db.ConversationFileAttachments.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.OwnerId == ownerId)
            .Select(x => x.FileId).ToListAsync(cancellationToken);
        var collectionIds = await db.ConversationCollectionAttachments.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.OwnerId == ownerId)
            .Select(x => x.CollectionId).ToListAsync(cancellationToken);
        if (direct.Count == 0 && collectionIds.Count == 0) return [];

        var fromCollections = collectionIds.Count == 0
            ? []
            : await db.FileCollectionMembers.AsNoTracking()
                .Where(x => collectionIds.Contains(x.CollectionId) && x.OwnerId == ownerId)
                .Select(x => x.FileId).ToListAsync(cancellationToken);

        return direct.Concat(fromCollections).Distinct().ToArray();
    }

    private Task<bool> OwnsConversationAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken) =>
        db.Conversations.AsNoTracking()
            .AnyAsync(x => x.Id == conversationId && x.OwnerId == ownerId, cancellationToken);
}
