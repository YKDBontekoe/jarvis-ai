using Jarvis.Domain.Memory;
using Pgvector;

namespace Jarvis.Infrastructure.Persistence;

public sealed class MemoryEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Kind { get; set; } = "fact";
    public string Content { get; set; } = string.Empty;
    public Vector? Embedding { get; set; }
    public string? EmbeddingModel { get; set; }
    public DateTimeOffset? GraphIndexedAt { get; set; }
    public float Importance { get; set; }
    public float Confidence { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public bool IsPinned { get; set; }
    public Guid? ProfileId { get; set; }
    public NpgsqlTypes.NpgsqlTsVector SearchVector { get; set; } = null!;

    public MemoryRecord ToRecord() => new(Id, OwnerId, Kind, Content, Importance, Confidence,
        SourceType, SourceId, CreatedAt, UpdatedAt, ValidUntil, IsPinned, ProfileId);
}
