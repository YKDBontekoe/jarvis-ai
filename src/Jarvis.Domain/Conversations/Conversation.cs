namespace Jarvis.Domain.Conversations;

public sealed class Conversation
{
    private Conversation() { }

    public Conversation(Guid ownerId, string title)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Title = title;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public Guid? ProfileId { get; private set; }
    public int? ProfileVersion { get; private set; }
    public string? ProfileSnapshotJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the owner pinned this conversation to the top of the list; null when not pinned.</summary>
    public DateTimeOffset? PinnedAt { get; private set; }

    /// <summary>The owner's project this conversation belongs to; null when it stands alone.</summary>
    public Guid? ProjectId { get; private set; }
    public List<Message> Messages { get; private set; } = [];

    /// <summary>Moves the conversation into a project, or out of one with null. Does not move it in the recent list.</summary>
    public void MoveToProject(Guid? projectId) => ProjectId = projectId;

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    public void BindProfile(Guid profileId, int version, string snapshotJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        ProfileId = profileId;
        ProfileVersion = version;
        ProfileSnapshotJson = snapshotJson;
        Touch();
    }

    public const int MaximumTitleLength = 200;

    /// <summary>Gives the conversation an owner-chosen title. Does not move it in the recent list.</summary>
    public void Rename(string title)
    {
        var normalized = string.Join(' ', (title ?? string.Empty).Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) throw new ArgumentException("Title is required.", nameof(title));
        if (normalized.Length > MaximumTitleLength)
            throw new ArgumentException($"Title must be {MaximumTitleLength} characters or fewer.", nameof(title));
        Title = normalized;
    }

    public void SetPinned(bool pinned)
    {
        if (pinned) PinnedAt ??= DateTimeOffset.UtcNow;
        else PinnedAt = null;
    }

    public void SetTitleFromFirstMessage(string content)
    {
        if (Title is not ("New conversation" or "Chat with Jarvis")) return;

        var normalized = string.Join(' ', content.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) return;
        if (normalized.Length > 72)
        {
            var boundary = normalized.LastIndexOf(' ', 69);
            normalized = normalized[..(boundary > 24 ? boundary : 69)].TrimEnd() + "…";
        }

        Title = normalized;
    }
}
