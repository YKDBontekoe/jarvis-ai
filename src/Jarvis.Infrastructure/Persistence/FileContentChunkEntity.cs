using Pgvector;
using NpgsqlTypes;

namespace Jarvis.Infrastructure.Persistence;

public sealed class FileContentChunkEntity
{
    public Guid Id { get; set; }
    public Guid FileId { get; set; }
    public Guid OwnerId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public int? PageNumber { get; set; }
    public Vector? Embedding { get; set; }
    public NpgsqlTsVector SearchText { get; set; } = null!;
}
