namespace Jarvis.Domain.Files;

public sealed record FileCollection(Guid Id, Guid OwnerId, string Name, DateTimeOffset CreatedAt);
