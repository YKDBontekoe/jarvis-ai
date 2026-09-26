namespace Jarvis.Domain.Workflows;

public sealed class PushDevice
{
    private PushDevice() { }

    public PushDevice(Guid ownerId, string token, string platform)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Token = token;
        Platform = platform;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public string Platform { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Refresh(string platform)
    {
        Platform = platform;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AssignTo(Guid ownerId, string platform)
    {
        OwnerId = ownerId;
        Refresh(platform);
    }
}
