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
    /// <summary>Nearest heading above the chunk; part of the keyword index and the embedded text.</summary>
    public string? Heading { get; set; }
    public Vector? Embedding { get; set; }
    /// <summary>Model that produced <see cref="Embedding"/>; semantic search only compares vectors of the same model.</summary>
    public string? EmbeddingModel { get; set; }
    public NpgsqlTsVector SearchText { get; set; } = null!;
}
