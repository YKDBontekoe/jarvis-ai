namespace Jarvis.Domain.Integrations;

public sealed class McpOAuthSession
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? ServerKey { get; set; }
    public string CodeVerifier { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? AuthorizationEndpoint { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? RegistrationEndpoint { get; set; }
    public string? ClientId { get; set; }
    public string? Resource { get; set; }
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public bool IsPending(DateTimeOffset utcNow) =>
        Status == "pending" && ExpiresAt > utcNow;
}
