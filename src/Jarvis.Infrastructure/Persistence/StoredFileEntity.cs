using Jarvis.Domain.Files;

namespace Jarvis.Infrastructure.Persistence;

public sealed class StoredFileEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduleDispatchedAt { get; set; }
    public string ProcessingStatus { get; set; } = "uploaded";

    /// <summary>The owner's project this file belongs to; null when it is not in a project.</summary>
    public Guid? ProjectId { get; set; }

    public StoredFile ToRecord() => new(Id, OwnerId, ObjectKey, FileName, ContentType,
        SizeBytes, Sha256, CreatedAt, ProcessingStatus);
}
