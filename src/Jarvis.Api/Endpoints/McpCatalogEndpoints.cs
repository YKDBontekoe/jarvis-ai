using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Mcp;

namespace Jarvis.Api.Endpoints;

/// <summary>Find and add MCP servers without knowing their address or package first.</summary>
internal static class McpCatalogEndpoints
{
    public sealed record ConnectMcpServerRequest(string Endpoint, string? Name);

    public static RouteGroupBuilder MapMcpCatalogEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/mcp-catalog", async (string? search, string? cursor, int? limit, IMcpCatalog catalog,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await catalog.SearchAsync(search, cursor, limit ?? 20, ct));
            }
            catch (McpCatalogUnavailableException exception)
            {
                return Unavailable(exception);
            }
        }).WithName("SearchMcpCatalog");

        api.MapPost("/mcp-catalog/install", async (McpCatalogInstallRequest request, IMcpCatalog catalog,
            IUserMcpServerRegistry servers, ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var server = await McpCatalogInstaller.InstallAsync(currentUser.OwnerId, request, catalog, servers, ct);
                return Results.Created($"/api/v1/mcp-servers/{server.Id}",
                    new McpCatalogInstallResult(server, await NextStepAsync(server, ct)));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("server", exception.Message);
            }
            catch (McpCatalogUnavailableException exception)
            {
                return Unavailable(exception);
            }
        }).WithName("InstallMcpCatalogServer");

        // Paste an address: Jarvis names the server after its host and allows every tool it offers. Each call
        // still asks for approval.
        api.MapPost("/mcp-servers/connect", async (ConnectMcpServerRequest request, IUserMcpServerRegistry servers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var endpoint = await McpServerEndpointValidator.ValidateAsync(request.Endpoint, ct);
                var name = string.IsNullOrWhiteSpace(request.Name) ? NameFromEndpoint(endpoint) : request.Name.Trim();
                var server = await servers.AddAsync(currentUser.OwnerId,
                    new AddUserMcpServerRequest(name, endpoint, [McpToolSelection.All]), ct);
                return Results.Created($"/api/v1/mcp-servers/{server.Id}",
                    new McpCatalogInstallResult(server, await NextStepAsync(server, ct)));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("endpoint", exception.Message);
            }
        }).WithName("ConnectMcpServer");

        return api;
    }

    private static async Task<string> NextStepAsync(UserMcpServer server, CancellationToken ct)
    {
        var usesOAuth = false;
        if (server.Transport == "streamableHttp" && server.Secrets is not { Count: > 0 })
        {
            var metadata = await McpAuthorizationDiscovery.DiscoverMetadataAsync(server.Endpoint, null, ct);
            usesOAuth = metadata.AuthorizationEndpoint is not null;
        }
        return McpCatalogInstaller.NextStep(server, usesOAuth);
    }

    /// <summary>"mcp.notion.com" becomes "Notion", "api.githubcopilot.com" becomes "Githubcopilot".</summary>
    internal static string NameFromEndpoint(string endpoint)
    {
        var labels = new Uri(endpoint).IdnHost.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var meaningful = labels.Length >= 2 ? labels[^2] : labels.FirstOrDefault() ?? "App";
        if (labels.Length >= 3 && meaningful.Length <= 3 && labels[^3] is not ("mcp" or "api" or "www"))
            meaningful = labels[^3];
        return McpRegistryMapper.DisplayName(null, meaningful);
    }

    private static IResult Unavailable(McpCatalogUnavailableException exception) =>
        Results.Json(new { error = "catalog_unavailable", message = exception.Message },
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
