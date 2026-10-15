using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Mcp;
using Microsoft.Extensions.Configuration;

namespace Jarvis.Api.Endpoints;

internal static class IntegrationEndpoints
{
    public static RouteGroupBuilder MapIntegrationEndpoints(this RouteGroupBuilder api)
    {
        MapMcpServers(api);
        MapCredentials(api);
        return api;
    }

    private static void MapMcpServers(RouteGroupBuilder api)
    {
        api.MapGet("/mcp-servers", async (IUserMcpServerRegistry servers, ICurrentUser currentUser,
                CancellationToken ct) => Results.Ok(await servers.ListAsync(currentUser.OwnerId, ct)))
            .WithName("ListUserMcpServers");

        api.MapPost("/mcp-servers", async (AddUserMcpServerRequest request, IUserMcpServerRegistry servers,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var server = await servers.AddAsync(currentUser.OwnerId, request, ct);
                return Results.Created($"/api/v1/mcp-servers/{server.Id}", server);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("server", exception.Message);
            }
        }).WithName("AddUserMcpServer");

        api.MapPut("/mcp-servers/{id}", async (string id, AddUserMcpServerRequest request,
            IUserMcpServerRegistry servers, ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var server = await servers.UpdateAsync(currentUser.OwnerId, id, request, ct);
                return server is null ? Results.NotFound() : Results.Ok(server);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("server", exception.Message);
            }
        }).WithName("UpdateUserMcpServer");

        api.MapDelete("/mcp-servers/{id}", async (string id, IUserMcpServerRegistry servers,
                ICurrentUser currentUser, CancellationToken ct) =>
            await servers.RemoveAsync(currentUser.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("RemoveUserMcpServer");

        // Connect once and report whether the app works, so a saved key or sign-in is confirmed right away.
        // Works for owner servers (by id) and host servers (by name).
        api.MapPost("/mcp-servers/{key}/test", async (string key, McpToolHost host, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await host.TestServerAsync(key, ct));
            }
            catch (ArgumentException exception)
            {
                return Results.Ok(new McpConnectionTestResult(false, "unavailable", 0, [], "unavailable",
                    exception.Message));
            }
            catch (InvalidMcpServerConfigurationException exception)
            {
                return Results.Conflict(new { error = "invalid_configuration", message = exception.Message });
            }
        }).WithName("TestMcpServer");

        api.MapPut("/mcp-servers/{id}/state", async (string id, McpServerStateRequest request,
            IUserMcpServerRegistry servers, ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var server = await ApplyUserStateAsync(id, request, servers, currentUser.OwnerId, ct);
                return server is null ? Results.NotFound() : Results.Ok(server);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("state", exception.Message);
            }
            catch (InvalidMcpServerConfigurationException exception)
            {
                return Results.Conflict(new { error = "invalid_configuration", message = exception.Message });
            }
        }).WithName("SetUserMcpServerState");

        api.MapPut("/mcp-controls/{name}", async (string name, McpServerStateRequest request,
            IConfiguration configuration, IOwnerMcpPolicyStore policy, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var configured = McpServerConfiguration.Find(configuration, name);
            if (configured is null) return Results.NotFound();
            try
            {
                var current = (await policy.GetAsync(currentUser.OwnerId, ct))
                    .TryGetValue(configured.Name, out var existing) ? existing : null;
                var next = McpToolSelection.NextHostOverride(current, configured.AllowedTools, request.Enabled,
                    request.ToolMode, request.AllowedTools);
                await policy.SaveAsync(currentUser.OwnerId, configured.Name, next, ct);
                return Results.Ok(new { name = configured.Name, enabled = next.Enabled, allowedTools = next.AllowedTools });
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("state", exception.Message);
            }
        }).WithName("SetHostMcpServerState");
    }

    private static async Task<UserMcpServer?> ApplyUserStateAsync(string id, McpServerStateRequest request,
        IUserMcpServerRegistry servers, Guid ownerId, CancellationToken cancellationToken)
    {
        if (request.Enabled is null && string.IsNullOrWhiteSpace(request.ToolMode))
            throw new ArgumentException("Set enabled, a tool mode, or both.");
        UserMcpServer? server = null;
        if (request.Enabled is { } enabled)
        {
            server = await servers.SetEnabledAsync(ownerId, id, enabled, cancellationToken);
            if (server is null) return null;
        }
        if (!string.IsNullOrWhiteSpace(request.ToolMode))
        {
            server = await servers.SetToolsAsync(ownerId, id, request.ToolMode, request.AllowedTools ?? [],
                cancellationToken);
        }
        return server;
    }

    private static void MapCredentials(RouteGroupBuilder api)
    {
        api.MapGet("/integrations/credentials", async (IIntegrationCredentialStore credentials,
                ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await credentials.ListAsync(currentUser.OwnerId, ct))
                    .Where(status => !IntegrationCredentialProviders.IsReserved(status.Provider))))
            .WithName("ListIntegrationCredentialStatuses");

        api.MapGet("/integrations/connections", async (McpToolHost mcpToolHost,
            Jarvis.Application.Learning.ILearningStore learning, ICurrentUser currentUser, CancellationToken ct) =>
        {
            await mcpToolHost.InitializeAsync(ct);
            var checkedAt = DateTimeOffset.UtcNow;
            var turns = (await learning.ListTracesAsync(currentUser.OwnerId, checkedAt.AddDays(-7), 1000, ct))
                .Select(trace => (trace.CreatedAt, trace.Tools.Select(tool => tool.Tool))).ToArray();
            return Results.Ok(mcpToolHost.Statuses.Select(status =>
            {
                var usage = McpAppUsageSummary.For(turns, status.Tools ?? []);
                return new
                {
                    status.Name,
                    status.State,
                    status.ToolCount,
                    status.Issue,
                    status.Id,
                    status.Enabled,
                    status.Tools,
                    status.HostManaged,
                    status.Transport,
                    status.CredentialProvider,
                    status.ConfiguredTools,
                    status.HasCredentials,
                    status.ReadOnlyTools,
                    status.SelfBuilt,
                    usage.CallsLast7Days,
                    usage.LastUsedAt,
                    LastCheckedAt = checkedAt
                };
            }));
        }).WithName("ListIntegrationConnectionStatuses");

        api.MapGet("/integrations/{provider}/credentials", async (string provider,
            IIntegrationCredentialStore credentials, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (IntegrationCredentialProviders.IsReserved(provider)) return Results.NotFound();
            var status = await credentials.GetStatusAsync(currentUser.OwnerId, provider, ct);
            return status is null ? Results.NotFound() : Results.Ok(status);
        }).WithName("GetIntegrationCredentialStatus");

        api.MapPut("/integrations/{provider}/credentials", async (string provider,
            SaveIntegrationCredentialsRequest request, IIntegrationCredentialStore credentials,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (RejectManagedCredentialRoute(provider) is { } rejected) return rejected;
            try
            {
                await credentials.SaveAsync(currentUser.OwnerId, provider, request.Secrets, ct);
                return Results.Ok(await credentials.GetStatusAsync(currentUser.OwnerId, provider, ct));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("credentials", exception.Message);
            }
        }).WithName("SaveIntegrationCredentials");

        api.MapPut("/integrations/{provider}/credentials/{secretName}", async (string provider, string secretName,
            SaveIntegrationSecretRequest request, IIntegrationCredentialStore credentials,
            IUserMcpServerRegistry servers, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (RejectManagedCredentialRoute(provider, secretName) is { } rejected) return rejected;
            try
            {
                var userServer = await FindUserMcpServerAsync(servers, currentUser.OwnerId, provider, ct);
                if (IntegrationCredentialProviders.IsUserMcpManaged(provider))
                {
                    if (userServer is null) return Results.NotFound();
                    if (!AcceptsSecret(userServer, secretName))
                        return EndpointHelpers.Invalid("credential", "This server does not use a secret with that name.");
                }
                await credentials.SaveSecretAsync(currentUser.OwnerId, provider, secretName, request.Value, ct);
                return Results.Ok(PublicCredentialStatus(
                    (await credentials.GetStatusAsync(currentUser.OwnerId, provider, ct))!, userServer));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("credential", exception.Message);
            }
        }).WithName("SaveIntegrationSecret");

        api.MapDelete("/integrations/{provider}/credentials/{secretName}", async (string provider, string secretName,
            IIntegrationCredentialStore credentials, IUserMcpServerRegistry servers, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (RejectManagedCredentialRoute(provider, secretName) is { } rejected) return rejected;
            try
            {
                if (IntegrationCredentialProviders.IsUserMcpManaged(provider) &&
                    await FindUserMcpServerAsync(servers, currentUser.OwnerId, provider, ct) is null)
                    return Results.NotFound();
                return await credentials.DeleteSecretAsync(currentUser.OwnerId, provider, secretName, ct)
                    ? Results.NoContent() : Results.NotFound();
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("credential", exception.Message);
            }
        }).WithName("DeleteIntegrationSecret");

        api.MapDelete("/integrations/{provider}/credentials", async (string provider,
            IIntegrationCredentialStore credentials, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (RejectManagedCredentialRoute(provider) is { } rejected) return rejected;
            return await credentials.DeleteAsync(currentUser.OwnerId, provider, ct)
                ? Results.NoContent()
                : Results.NotFound();
        }).WithName("DeleteIntegrationCredentials");
    }

    private static IResult? RejectManagedCredentialRoute(string provider, string? secretName = null)
    {
        if (IntegrationCredentialProviders.IsSystemManaged(provider))
            return EndpointHelpers.Invalid("provider",
                "This provider's secrets are managed from its own settings screen, not integration credentials.");
        if (!IntegrationCredentialProviders.IsUserMcpManaged(provider)) return null;
        // The token and any secret the server declares are owner-editable; its stored configuration is not.
        if (secretName is not null && secretName != IntegrationCredentialProviders.UserMcpConfigSecret) return null;
        return EndpointHelpers.Invalid("provider",
            "MCP servers are managed from /api/v1/mcp-servers, not integration credentials.");
    }

    private static async Task<UserMcpServer?> FindUserMcpServerAsync(IUserMcpServerRegistry servers, Guid ownerId,
        string provider, CancellationToken cancellationToken)
    {
        if (!IntegrationCredentialProviders.IsUserMcpServerId(provider)) return null;
        return (await servers.ListAsync(ownerId, cancellationToken))
            .FirstOrDefault(server => server.Id == provider && server.IsValid);
    }

    private static bool AcceptsSecret(UserMcpServer server, string secretName) =>
        IntegrationCredentialProviders.IsUserMcpTokenSecret(secretName) ||
        server.Secrets?.Any(secret => secret.Name == secretName) == true;

    private static IntegrationCredentialStatus PublicCredentialStatus(IntegrationCredentialStatus status,
        UserMcpServer? server) =>
        IntegrationCredentialProviders.IsUserMcpManaged(status.Provider)
            ? status with
            {
                SecretNames = status.SecretNames
                    .Where(name => server is not null && AcceptsSecret(server, name))
                    .ToArray()
            }
            : status;
}
