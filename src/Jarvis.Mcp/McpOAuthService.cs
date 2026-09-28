using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jarvis.Application.Integrations;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Jarvis.Mcp;

public sealed class McpOAuthService(
    IMcpOAuthSessionStore sessions,
    IUserMcpServerRegistry servers,
    IIntegrationCredentialStore credentials,
    ILogger<McpOAuthService> logger) : IMcpOAuthService
{
    public async Task<McpOAuthSessionRecord> StartAsync(Guid ownerId, StartMcpOAuthRequest request,
        string publicBaseUrl, CancellationToken cancellationToken)
    {
        var (provider, serverKey, endpoint) = await ResolveTargetAsync(ownerId, request, cancellationToken);
        var metadata = await McpAuthorizationDiscovery.DiscoverMetadataAsync(endpoint, null, cancellationToken);
        var redirectUri = $"{publicBaseUrl.TrimEnd('/')}/api/v1/integrations/oauth/callback";
        if (McpAuthorization.NormalizePublicHttpsUrl(redirectUri) is null &&
            !redirectUri.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) &&
            !redirectUri.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Jarvis needs a public HTTPS origin (Jarvis:PublicBaseUrl) to finish OAuth.");

        var clientId = await RegisterClientAsync(metadata.RegistrationEndpoint, redirectUri, cancellationToken);
        var verifier = CreateVerifier();
        var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var sessionId = Guid.CreateVersion7();
        string? authorizeUrl = null;
        if (!string.IsNullOrWhiteSpace(metadata.AuthorizationEndpoint))
        {
            var query = new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = clientId ?? "jarvis",
                ["redirect_uri"] = redirectUri,
                ["state"] = state,
                ["code_challenge"] = Challenge(verifier),
                ["code_challenge_method"] = "S256",
                ["resource"] = endpoint
            };
            authorizeUrl = AppendQuery(metadata.AuthorizationEndpoint, query);
        }

        if (string.IsNullOrWhiteSpace(authorizeUrl) || string.IsNullOrWhiteSpace(metadata.TokenEndpoint))
        {
            return new McpOAuthSessionRecord(Guid.Empty, provider, "needs_token", metadata.AuthorizationEndpoint,
                "oauth_handshake_unavailable", DateTimeOffset.UtcNow);
        }

        var draft = new McpOAuthSessionDraft(sessionId, ownerId, state, provider, serverKey, verifier, redirectUri,
            metadata.AuthorizationEndpoint, metadata.TokenEndpoint, metadata.RegistrationEndpoint, clientId ?? "jarvis",
            endpoint, DateTimeOffset.UtcNow.AddMinutes(15), AuthorizationUrl: authorizeUrl);
        return await sessions.SavePendingAsync(draft, cancellationToken);
    }

    public Task<McpOAuthSessionRecord?> GetAsync(Guid ownerId, Guid sessionId, CancellationToken cancellationToken) =>
        sessions.GetAsync(ownerId, sessionId, cancellationToken);

    public async Task<string> CompleteAsync(string state, string? code, string? error,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetByStateAsync(state, cancellationToken);
        if (session is null)
            return FailPage("This authorization request expired or was not recognized.");
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            await sessions.MarkCompletedAsync(session.Id, false, error ?? "missing_code", cancellationToken);
            return FailPage("Authorization was cancelled or did not return a code.");
        }

        try
        {
            var token = await ExchangeAsync(session, code, cancellationToken);
            await credentials.SaveSecretAsync(session.OwnerId, session.Provider,
                IntegrationCredentialProviders.UserMcpTokenSecret, token, cancellationToken);
            if (!string.IsNullOrWhiteSpace(session.ServerKey) &&
                !IntegrationCredentialProviders.IsUserMcpServerId(session.Provider))
            {
                // Host servers such as github use the host credential provider slug.
            }
            await sessions.MarkCompletedAsync(session.Id, true, null, cancellationToken);
            return SuccessPage();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "MCP OAuth token exchange failed.");
            await sessions.MarkCompletedAsync(session.Id, false, "token_exchange_failed", cancellationToken);
            return FailPage("Jarvis could not complete authorization. You can store a token in Settings → Integrations.");
        }
    }

    private async Task<(string Provider, string? ServerKey, string? Endpoint)> ResolveTargetAsync(Guid ownerId,
        StartMcpOAuthRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Server) && !string.IsNullOrWhiteSpace(request.Endpoint))
            throw new ArgumentException("Pass either a server or an endpoint, not both.");
        if (!string.IsNullOrWhiteSpace(request.Server))
        {
            var key = request.Server.Trim();
            foreach (var server in await servers.ListAsync(ownerId, cancellationToken))
            {
                if (server.Id.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    server.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return (server.Id, server.Name, string.IsNullOrWhiteSpace(server.Endpoint) ? null : server.Endpoint);
            }
            return (key.StartsWith("jarvis-", StringComparison.Ordinal) ? key : key.ToLowerInvariant(), key, null);
        }
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            throw new ArgumentException("Name the MCP server or pass its public HTTPS endpoint.");
        var endpoint = await McpServerEndpointValidator.ValidateAsync(request.Endpoint, cancellationToken);
        return ("jarvis-mcp-discovery", endpoint, endpoint);
    }

    private static async Task<string?> RegisterClientAsync(string? registrationEndpoint, string redirectUri,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registrationEndpoint)) return null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.PostAsJsonAsync(registrationEndpoint, new
            {
                client_name = "Jarvis",
                redirect_uris = new[] { redirectUri },
                grant_types = new[] { "authorization_code", "refresh_token" },
                response_types = new[] { "code" },
                token_endpoint_auth_method = "none",
                application_type = "web"
            }, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("client_id", out var id) &&
                   id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<string> ExchangeAsync(McpOAuthSessionDraft session, string code,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = session.RedirectUri,
            ["client_id"] = session.ClientId ?? "jarvis",
            ["code_verifier"] = session.CodeVerifier
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, session.TokenEndpoint) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The authorization server rejected the token request.");
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("access_token", out var token) ||
            token.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(token.GetString()))
            throw new InvalidOperationException("The authorization server did not return an access token.");
        return token.GetString()!;
    }

    private static string CreateVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    private static string Challenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string AppendQuery(string endpoint, Dictionary<string, string?> values)
    {
        var pairs = values.Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => new KeyValuePair<string, string?>(item.Key, item.Value));
        return QueryHelpers.AddQueryString(endpoint, pairs);
    }

    private static string SuccessPage() =>
        """
        <!doctype html><html lang="en"><meta charset="utf-8"><title>Jarvis</title>
        <body style="font-family:system-ui;padding:2rem;max-width:36rem">
        <h1>Connected</h1><p>You can return to Jarvis. This window can be closed.</p></body></html>
        """;

    private static string FailPage(string message) =>
        $"""
        <!doctype html><html lang="en"><meta charset="utf-8"><title>Jarvis</title>
        <body style="font-family:system-ui;padding:2rem;max-width:36rem">
        <h1>Authorization did not finish</h1><p>{System.Net.WebUtility.HtmlEncode(message)}</p></body></html>
        """;
}
