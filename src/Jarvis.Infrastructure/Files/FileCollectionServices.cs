using Jarvis.Application.Files;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Files;

public sealed class ConversationFileScopeService(IConversationFileContextRepository context) : IConversationFileScopeService
{
    public async Task<IReadOnlyCollection<Guid>?> ResolveSearchFileIdsAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var fileIds = await context.ResolveScopedFileIdsAsync(conversationId, ownerId, cancellationToken);
        return fileIds.Count == 0 ? null : fileIds;
    }
}

public sealed class FileCitationCollector : IFileCitationCollector
{
    private readonly List<FileCitation> _citations = [];

    public void Record(IEnumerable<FileCitation> citations)
    {
        foreach (var citation in citations)
        {
            if (_citations.Any(existing => existing.ChunkId == citation.ChunkId)) continue;
            _citations.Add(citation);
        }
    }

    public IReadOnlyList<FileCitation> Drain()
    {
        var drained = _citations.ToArray();
        _citations.Clear();
        return drained;
    }
}

public sealed class FileCitationResolver(JarvisDbContext db) : IFileCitationResolver
{
    public async Task<FileCitation?> ResolveAsync(Guid chunkId, Guid ownerId, CancellationToken cancellationToken)
    {
        var chunk = await db.FileContentChunks.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == chunkId && x.OwnerId == ownerId, cancellationToken);
        if (chunk is null) return null;
        var file = await db.Files.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == chunk.FileId && x.OwnerId == ownerId, cancellationToken);
        if (file is null) return null;

        var status = file.ProcessingStatus switch
        {
            "deleting" => "deleted",
            "processing" or "queued" => "reprocessing",
            "failed" => "unavailable",
            _ => "available"
        };
        return new FileCitation(file.Id, file.FileName, chunk.Id, chunk.ChunkIndex,
            FileReferenceSanitizer.SanitizeExcerpt(chunk.Content), chunk.PageNumber, status);
    }
}
