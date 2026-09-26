namespace Jarvis.Domain.Integrations;

public sealed class IntegrationCredential
{
    private IntegrationCredential() { }

    public IntegrationCredential(Guid ownerId, string provider, string protectedPayload, string secretNamesJson)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Provider = provider;
        ProtectedPayload = protectedPayload;
        SecretNamesJson = secretNamesJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string ProtectedPayload { get; private set; } = string.Empty;
    public string SecretNamesJson { get; private set; } = "[]";
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Replace(string protectedPayload, string secretNamesJson)
    {
        ProtectedPayload = protectedPayload;
        SecretNamesJson = secretNamesJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
