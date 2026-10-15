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
            IUserMcpServerRegistry servers, ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var page = await catalog.SearchAsync(search, cursor, limit ?? 20, ct);
                var installed = await InstalledAsync(servers, currentUser.OwnerId, ct);
                return Results.Ok(new
                {
                    servers = page.Servers.Select(entry => View(entry, installed)),
                    nextCursor = page.NextCursor
                });
            }
            catch (McpCatalogUnavailableException exception)
            {
                return Unavailable(exception);
            }
        }).WithName("SearchMcpCatalog");

        // What to show before the owner types anything.
        api.MapGet("/mcp-catalog/featured", async (IMcpCatalog catalog, IUserMcpServerRegistry servers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var installed = await InstalledAsync(servers, currentUser.OwnerId, ct);
            var categories = await McpFeaturedCatalog.ResolveAsync(catalog, ct);
            return Results.Ok(categories.Select(item => new
            {
                id = item.Category.Id,
                title = item.Category.Title,
                servers = item.Entries.Select(entry => View(entry, installed))
            }));
        }).WithName("FeaturedMcpCatalog");

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

    private static async Task<IReadOnlyDictionary<string, string>> InstalledAsync(IUserMcpServerRegistry servers,
        Guid ownerId, CancellationToken ct) =>
        (await servers.ListAsync(ownerId, ct))
            .Where(server => !string.IsNullOrWhiteSpace(server.CatalogName))
            .GroupBy(server => server.CatalogName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>A catalog entry plus whether the owner already has it, so the app opens it instead of adding twice.</summary>
    internal static object View(McpCatalogEntry entry, IReadOnlyDictionary<string, string> installed) => new
    {
        entry.Name,
        entry.Title,
        entry.Description,
        entry.Version,
        entry.WebsiteUrl,
        entry.RepositoryUrl,
        entry.Options,
        installed = installed.ContainsKey(entry.Name),
        installedServerId = installed.TryGetValue(entry.Name, out var id) ? id : null
    };

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
