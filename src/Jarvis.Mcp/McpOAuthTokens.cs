using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Jarvis.Application.Integrations;

namespace Jarvis.Mcp;

/// <summary>An OAuth token response: the access token, and when present, how to renew it.</summary>
public sealed record McpOAuthTokenSet(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);

/// <summary>
/// Stores OAuth tokens next to the server's other keys and renews them before they expire, so a signed-in app
/// keeps working without the owner signing in again. The refresh token and endpoint are secrets like the token.
/// </summary>
public static class McpOAuthTokens
{
    public const string RefreshTokenSecret = "oauth_refresh_token";
    public const string TokenEndpointSecret = "oauth_token_endpoint";
    public const string ClientIdSecret = "oauth_client_id";
    public const string ResourceSecret = "oauth_resource";
    public const string ExpiresAtSecret = "oauth_expires_at";

    /// <summary>Renew this long before the token expires, so a turn never starts with a token about to lapse.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    public static McpOAuthTokenSet Parse(string body, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(token.GetString()))
            throw new InvalidOperationException("The authorization server did not return an access token.");
        var refresh = root.TryGetProperty("refresh_token", out var r) && r.ValueKind == JsonValueKind.String &&
                      !string.IsNullOrWhiteSpace(r.GetString())
            ? r.GetString()
            : null;
        DateTimeOffset? expires = null;
        if (root.TryGetProperty("expires_in", out var e))
        {
            var seconds = e.ValueKind switch
            {
                JsonValueKind.Number when e.TryGetInt64(out var n) => n,
                JsonValueKind.String when long.TryParse(e.GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var n) => n,
                _ => 0
            };
            if (seconds is > 0 and < 10L * 365 * 24 * 3600) expires = now.AddSeconds(seconds);
        }
        return new McpOAuthTokenSet(token.GetString()!, refresh, expires);
    }

    /// <summary>Saves the token and, when it can be renewed, what renewal needs.</summary>
    public static async Task SaveAsync(IIntegrationCredentialStore credentials, Guid ownerId, string provider,
        McpOAuthTokenSet tokens, string? tokenEndpoint, string? clientId, string? resource,
        CancellationToken cancellationToken)
    {
        await credentials.SaveSecretAsync(ownerId, provider, IntegrationCredentialProviders.UserMcpTokenSecret,
            tokens.AccessToken, cancellationToken);
        if (tokens.RefreshToken is null || tokens.ExpiresAt is null ||
            McpAuthorization.NormalizePublicHttpsUrl(tokenEndpoint) is null)
        {
            // A token without renewal details must not keep a stale expiry from an earlier sign-in.
            await credentials.DeleteSecretAsync(ownerId, provider, ExpiresAtSecret, cancellationToken);
            await credentials.DeleteSecretAsync(ownerId, provider, RefreshTokenSecret, cancellationToken);
            return;
        }
        await credentials.SaveSecretAsync(ownerId, provider, RefreshTokenSecret, tokens.RefreshToken, cancellationToken);
        await credentials.SaveSecretAsync(ownerId, provider, TokenEndpointSecret, tokenEndpoint!, cancellationToken);
        if (!string.IsNullOrWhiteSpace(clientId))
            await credentials.SaveSecretAsync(ownerId, provider, ClientIdSecret, clientId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(resource))
            await credentials.SaveSecretAsync(ownerId, provider, ResourceSecret, resource, cancellationToken);
        await credentials.SaveSecretAsync(ownerId, provider, ExpiresAtSecret,
            tokens.ExpiresAt.Value.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
    }

    /// <summary>True when the stored token expires soon and a refresh token is stored.</summary>
    public static bool NeedsRefresh(IReadOnlyDictionary<string, string> secrets, DateTimeOffset now) =>
        secrets.TryGetValue(ExpiresAtSecret, out var value) &&
        secrets.ContainsKey(RefreshTokenSecret) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires) &&
        expires - now <= RefreshMargin;

    /// <summary>Asks the authorization server for a new token with the stored refresh token.</summary>
    public static async Task<McpOAuthTokenSet> RefreshAsync(HttpClient http, IReadOnlyDictionary<string, string> secrets,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!secrets.TryGetValue(RefreshTokenSecret, out var refresh) ||
            !secrets.TryGetValue(TokenEndpointSecret, out var endpoint) ||
            McpAuthorization.NormalizePublicHttpsUrl(endpoint) is not { } tokenEndpoint)
            throw new InvalidOperationException("This sign-in cannot be renewed.");
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refresh,
            ["client_id"] = secrets.TryGetValue(ClientIdSecret, out var client) ? client : "jarvis"
        };
        if (secrets.TryGetValue(ResourceSecret, out var resource)) form["resource"] = resource;
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The authorization server refused to renew the sign-in.");
        var tokens = Parse(body, now);
        // Servers that do not rotate refresh tokens leave the old one valid.
        return tokens.RefreshToken is null ? tokens with { RefreshToken = refresh } : tokens;
    }
}
