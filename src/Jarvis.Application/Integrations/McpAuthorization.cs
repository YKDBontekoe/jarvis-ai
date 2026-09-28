using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.Application.Integrations;

/// <summary>
/// User-facing MCP authorization request. When a server needs OAuth or a stored token,
/// Jarvis must ask the owner to authorize rather than continuing unauthorized.
/// </summary>
public sealed record McpAuthorizationAsk(
    string Status,
    bool AskUser,
    bool MustAsk,
    string Message,
    string IntegrationsPath,
    string CredentialName,
    string? Server = null,
    string? Provider = null,
    string? AuthorizationUrl = null,
    string? ResourceMetadataUrl = null);

public static class McpAuthorization
{
    public const string RequiredStatus = "authorization_required";
    public const string IntegrationsPath = "Settings → Integrations";
    public const string TokenSecretName = IntegrationCredentialProviders.UserMcpTokenSecret;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex WwwParam = new(
        @"([A-Za-z][A-Za-z0-9_-]*)\s*=\s*(?:""([^""]*)""|([^\s,]+))",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string FormatAsk(string? server, string? provider, string? authorizationUrl = null,
        string? resourceMetadataUrl = null, string? extra = null)
    {
        var providerId = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        var serverName = string.IsNullOrWhiteSpace(server) ? providerId : server.Trim();
        var url = NormalizePublicHttpsUrl(authorizationUrl);
        var metadata = NormalizePublicHttpsUrl(resourceMetadataUrl);
        var message = url is null
            ? "Ask the user to authorize this MCP server before using it. Open Settings → Integrations, " +
              $"store a token under provider '{providerId ?? serverName}' as credential name '{TokenSecretName}', " +
              "then tell you when they have finished. Do not continue as if the server is connected, and never collect the token in chat."
            : "Ask the user to authorize this MCP server before using it. Include this authorization URL as a Markdown link " +
              $"in your reply: {url}. You may also open it on a connected device with OpenUrlOnDevice. " +
              "Wait until they finish authorization. If they prefer a token, they can store it in Settings → Integrations " +
              $"under provider '{providerId ?? serverName}' as credential name '{TokenSecretName}'. Never collect the token in chat.";
        if (!string.IsNullOrWhiteSpace(extra))
            message += " " + extra.Trim();
        return JsonSerializer.Serialize(new McpAuthorizationAsk(
            RequiredStatus, true, true, message, IntegrationsPath, TokenSecretName,
            serverName, providerId, url, metadata), JsonOptions);
    }

    public static bool IsAuthorizationStatus(HttpStatusCode? status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public static bool IsAuthorizationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException http && IsAuthorizationStatus(http.StatusCode))
                return true;
            var text = current.Message;
            if (text.Contains("401", StringComparison.Ordinal) ||
                text.Contains("403", StringComparison.Ordinal) ||
                text.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("www-authenticate", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("authentication required", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("authorization required", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string? ParseResourceMetadataUrl(string? wwwAuthenticate)
    {
        if (string.IsNullOrWhiteSpace(wwwAuthenticate)) return null;
        foreach (var match in WwwParam.Matches(wwwAuthenticate).Cast<Match>())
        {
            if (!match.Groups[1].Value.Equals("resource_metadata", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            return NormalizePublicHttpsUrl(value);
        }
        return null;
    }

    public static string? ReadAuthorizationEndpoint(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (TryReadUrl(root, "authorization_endpoint") is { } endpoint) return endpoint;
            if (root.TryGetProperty("authorization_servers", out var servers) && servers.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in servers.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        return NormalizePublicHttpsUrl(item.GetString());
                    if (item.ValueKind == JsonValueKind.Object &&
                        TryReadUrl(item, "authorization_endpoint") is { } nested)
                        return nested;
                }
            }
            return TryReadUrl(root, "authorization_url") ?? TryReadUrl(root, "authorizationUrl");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? NormalizePublicHttpsUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return null;
        return uri.AbsoluteUri;
    }

    private static string? TryReadUrl(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? NormalizePublicHttpsUrl(value.GetString())
            : null;
}
