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
    public List<Message> Messages { get; private set; } = [];

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    public void BindProfile(Guid profileId, int version, string snapshotJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        ProfileId = profileId;
        ProfileVersion = version;
        ProfileSnapshotJson = snapshotJson;
        Touch();
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
