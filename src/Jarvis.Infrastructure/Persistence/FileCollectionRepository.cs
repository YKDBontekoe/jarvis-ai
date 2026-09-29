using Jarvis.Application.Files;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

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
        if (!await db.DocumentCollections.AsNoTracking()
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
            : await db.DocumentCollectionFiles.AsNoTracking()
                .Where(x => collectionIds.Contains(x.CollectionId) && x.OwnerId == ownerId)
                .Select(x => x.FileId).ToListAsync(cancellationToken);

        return direct.Concat(fromCollections).Distinct().ToArray();
    }

    private Task<bool> OwnsConversationAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken) =>
        db.Conversations.AsNoTracking()
            .AnyAsync(x => x.Id == conversationId && x.OwnerId == ownerId, cancellationToken);
}
