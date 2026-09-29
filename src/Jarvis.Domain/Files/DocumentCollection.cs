namespace Jarvis.Domain.Files;

/// <summary>Owner-named group of uploaded files used to scope assistant profiles.</summary>
public sealed class DocumentCollection
{
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 500;
    public const int MaxCollectionsPerOwner = 50;

    private DocumentCollection() { }

    public DocumentCollection(Guid ownerId, string name, string? description)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Name = name;
        Description = description;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Rename(string name, string? description)
    {
        Name = name;
        Description = description;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
