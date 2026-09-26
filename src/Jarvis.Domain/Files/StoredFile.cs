namespace Jarvis.Domain.Files;

public sealed record StoredFile(
    Guid Id,
    Guid OwnerId,
    string ObjectKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAt,
    string ProcessingStatus);
