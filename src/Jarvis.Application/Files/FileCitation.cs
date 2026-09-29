namespace Jarvis.Application.Files;

public sealed record FileCitation(
    Guid FileId,
    string DisplayName,
    Guid ChunkId,
    int ChunkIndex,
    string Excerpt,
    int? PageNumber,
    string SourceStatus = "available");

public interface IFileCitationCollector
{
    void Record(IEnumerable<FileCitation> citations);
    IReadOnlyList<FileCitation> Drain();
}

public interface IFileCitationResolver
{
    Task<FileCitation?> ResolveAsync(Guid chunkId, Guid ownerId, CancellationToken cancellationToken);
}
