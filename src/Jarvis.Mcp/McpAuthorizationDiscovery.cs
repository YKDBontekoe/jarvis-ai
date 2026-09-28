using System.Net.Http.Headers;
using Jarvis.Application.Integrations;

namespace Jarvis.Mcp;

public static class McpAuthorizationDiscovery
{
    public static async Task<string?> DiscoverAuthorizationUrlAsync(string? endpoint, Exception? failure,
        CancellationToken cancellationToken)
    {
        var metadata = await DiscoverMetadataAsync(endpoint, failure, cancellationToken);
        return metadata.AuthorizationEndpoint;
    }

    public static async Task<McpOAuthMetadata> DiscoverMetadataAsync(string? endpoint, Exception? failure,
        CancellationToken cancellationToken)
    {
        var metadataUrl = MetadataUrlFromException(failure) ?? MetadataUrlFromEndpoint(endpoint);
        if (metadataUrl is null) return new McpOAuthMetadata(null, null, null, null);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Get, metadataUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return new McpOAuthMetadata(null, null, null, metadataUrl);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var resource = McpAuthorization.ReadMetadata(body);
            var issuer = FirstAuthorizationServer(body);
            if (issuer is null)
                return resource with { ResourceMetadataUrl = metadataUrl };
            var wellKnown = issuer.TrimEnd('/') + "/.well-known/oauth-authorization-server";
            using var asRequest = new HttpRequestMessage(HttpMethod.Get, wellKnown);
            asRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var asResponse = await http.SendAsync(asRequest, cancellationToken);
            if (!asResponse.IsSuccessStatusCode)
                return resource with { ResourceMetadataUrl = metadataUrl };
            var asBody = await asResponse.Content.ReadAsStringAsync(cancellationToken);
            var asMetadata = McpAuthorization.ReadMetadata(asBody);
            return new McpOAuthMetadata(
                asMetadata.AuthorizationEndpoint ?? resource.AuthorizationEndpoint,
                asMetadata.TokenEndpoint ?? resource.TokenEndpoint,
                asMetadata.RegistrationEndpoint ?? resource.RegistrationEndpoint,
                metadataUrl);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new McpOAuthMetadata(null, null, null, metadataUrl);
        }
    }

    public static string AuthorizationFailureMessage(string? server, string? provider, string? authorizationUrl,
        string? startUrl = null)
    {
        var extra = authorizationUrl is null && startUrl is null
            ? "The MCP server returned an authentication error. Ask the user to authorize it now."
            : null;
        return McpAuthorization.FormatAsk(server, provider, authorizationUrl, extra: extra, startUrl: startUrl);
    }

    private static string? MetadataUrlFromException(Exception? failure)
    {
        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException http)
            {
                var header = http.Data["www-authenticate"] as string;
                var parsed = McpAuthorization.ParseResourceMetadataUrl(header);
                if (parsed is not null) return parsed;
            }
            var fromMessage = McpAuthorization.ParseResourceMetadataUrl(current.Message);
            if (fromMessage is not null) return fromMessage;
        }
        return null;
    }

    private static string? MetadataUrlFromEndpoint(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        var builder = new UriBuilder(uri)
        {
            Path = "/.well-known/oauth-protected-resource",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return McpAuthorization.NormalizePublicHttpsUrl(builder.Uri.AbsoluteUri);
    }

    private static string? FirstAuthorizationServer(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("authorization_servers", out var servers) ||
                servers.ValueKind != System.Text.Json.JsonValueKind.Array)
                return null;
            foreach (var item in servers.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                    return McpAuthorization.NormalizePublicHttpsUrl(item.GetString());
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        return null;
    }
}
