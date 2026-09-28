namespace Jarvis.Application.Integrations;

public sealed record McpOAuthSessionRecord(Guid Id, string Provider, string Status, string? AuthorizationUrl,
    string? Error, DateTimeOffset ExpiresAt);

public sealed record StartMcpOAuthRequest(string? Server, string? Endpoint);

public interface IMcpOAuthSessionStore
{
    Task<McpOAuthSessionRecord> SavePendingAsync(McpOAuthSessionDraft draft, CancellationToken cancellationToken);
    Task<McpOAuthSessionDraft?> GetByStateAsync(string state, CancellationToken cancellationToken);
    Task<McpOAuthSessionRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task MarkCompletedAsync(Guid id, bool success, string? error, CancellationToken cancellationToken);
}

public sealed record McpOAuthSessionDraft(Guid Id, Guid OwnerId, string State, string Provider, string? ServerKey,
    string CodeVerifier, string RedirectUri, string? AuthorizationEndpoint, string? TokenEndpoint,
    string? RegistrationEndpoint, string? ClientId, string? Resource, DateTimeOffset ExpiresAt,
    string Status = "pending", string? Error = null, string? AuthorizationUrl = null);

public interface IMcpOAuthService
{
    Task<McpOAuthSessionRecord> StartAsync(Guid ownerId, StartMcpOAuthRequest request, string publicBaseUrl,
        CancellationToken cancellationToken);
    Task<McpOAuthSessionRecord?> GetAsync(Guid ownerId, Guid sessionId, CancellationToken cancellationToken);
    Task<string> CompleteAsync(string state, string? code, string? error, CancellationToken cancellationToken);
}

public sealed record McpOAuthMetadata(
    string? AuthorizationEndpoint,
    string? TokenEndpoint,
    string? RegistrationEndpoint,
    string? ResourceMetadataUrl);
