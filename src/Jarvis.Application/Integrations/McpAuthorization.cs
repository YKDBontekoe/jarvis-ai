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
    string? ResourceMetadataUrl = null,
    string? StartUrl = null);

public static class McpAuthorization
{
    public const string RequiredStatus = "authorization_required";
    public const string IntegrationsPath = "this chat";
    public const string TokenSecretName = IntegrationCredentialProviders.UserMcpTokenSecret;
    public const string ChatCredentialHint =
        "Call AskForMcpCredential with that provider so they can paste a token into a secret field in this chat. Never collect the token as chat text or in tool arguments.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex WwwParam = new(
        @"([A-Za-z][A-Za-z0-9_-]*)\s*=\s*(?:""([^""]*)""|([^\s,]+))",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string FormatAsk(string? server, string? provider, string? authorizationUrl = null,
        string? resourceMetadataUrl = null, string? extra = null, string? startUrl = null)
    {
        var providerId = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        var serverName = string.IsNullOrWhiteSpace(server) ? providerId : server.Trim();
        var url = NormalizePublicHttpsUrl(authorizationUrl);
        var metadata = NormalizePublicHttpsUrl(resourceMetadataUrl);
        var connect = NormalizePublicHttpsUrl(startUrl);
        var message = connect is not null
            ? "Ask the user to authorize this MCP server in this chat. RenderUi a card with an Authorize action whose url is this connect link " +
              $"({connect}). You may also open it with OpenUrlOnDevice. Wait until they finish authorization. " +
              $"If they need to paste a token instead, {ChatCredentialHint}"
            : url is null
            ? "Ask the user to authorize this MCP server in this chat. " +
              $"Provider '{providerId ?? serverName}'. {ChatCredentialHint}"
            : "Ask the user to authorize this MCP server in this chat. RenderUi a card with an Authorize action whose url is " +
              $"{url}. You may also open it with OpenUrlOnDevice. Wait until they finish authorization. " +
              $"If they prefer a token, provider '{providerId ?? serverName}'. {ChatCredentialHint}";
        if (!string.IsNullOrWhiteSpace(extra))
            message += " " + extra.Trim();
        return JsonSerializer.Serialize(new McpAuthorizationAsk(
            RequiredStatus, true, true, message, IntegrationsPath, TokenSecretName,
            serverName, providerId, url, metadata, connect), JsonOptions);
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

    public static string? ReadAuthorizationEndpoint(string json) =>
        ReadMetadata(json).AuthorizationEndpoint;

    public static McpOAuthMetadata ReadMetadata(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new McpOAuthMetadata(null, null, null, null);
            var authorization = TryReadUrl(root, "authorization_endpoint")
                ?? TryReadUrl(root, "authorization_url")
                ?? TryReadUrl(root, "authorizationUrl");
            if (authorization is null && root.TryGetProperty("authorization_servers", out var servers) &&
                servers.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in servers.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        authorization = NormalizePublicHttpsUrl(item.GetString());
                        if (authorization is not null) break;
                    }
                    if (item.ValueKind == JsonValueKind.Object &&
                        TryReadUrl(item, "authorization_endpoint") is { } nested)
                    {
                        authorization = nested;
                        break;
                    }
                }
            }
            return new McpOAuthMetadata(
                authorization,
                TryReadUrl(root, "token_endpoint"),
                TryReadUrl(root, "registration_endpoint"),
                TryReadUrl(root, "resource"));
        }
        catch (JsonException)
        {
            return new McpOAuthMetadata(null, null, null, null);
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
