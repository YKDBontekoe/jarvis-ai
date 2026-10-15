using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Mcp;

namespace Jarvis.Api.Endpoints;

/// <summary>Apps Jarvis wrote for the owner: read the code, test it, approve it, roll back or remove it.</summary>
internal static class CustomMcpEndpoints
{
    public sealed record CustomMcpTestRequest(string? ToolName, string? ArgumentsJson);
    public sealed record CustomMcpActivateRequest(string? Sha256);
    public sealed record CustomMcpRevertRequest(int Revision);

    public static RouteGroupBuilder MapCustomMcpEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/custom-mcp-servers", async (ICustomMcpServerStore store, ICurrentUser currentUser,
                CancellationToken ct) => Results.Ok(await store.ListAsync(currentUser.OwnerId, ct)))
            .WithName("ListCustomMcpServers");

        api.MapGet("/custom-mcp-servers/{id:guid}", async (Guid id, ICustomMcpServerStore store,
                ICurrentUser currentUser, CancellationToken ct) =>
            await store.GetAsync(currentUser.OwnerId, id, ct) is { } detail ? Results.Ok(detail) : Results.NotFound())
            .WithName("GetCustomMcpServer");

        api.MapPost("/custom-mcp-servers/{id:guid}/test", async (Guid id, CustomMcpTestRequest? request,
            McpToolHost host, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await host.TestCustomServerAsync(id, request?.ToolName, request?.ArgumentsJson, ct));
            }
            catch (ArgumentException)
            {
                return Results.NotFound();
            }
        }).WithName("TestCustomMcpServer");

        // The owner approves the exact code they reviewed: activation fails when the draft changed since.
        api.MapPost("/custom-mcp-servers/{id:guid}/activate", async (Guid id, CustomMcpActivateRequest? request,
            ICustomMcpServerStore store, IUserMcpServerRegistry registry, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            try
            {
                var (server, _) = await CustomMcpServerManager.ActivateAsync(currentUser.OwnerId, id,
                    request?.Sha256, store, registry, ct);
                return Results.Ok(server);
            }
            catch (CustomMcpScriptException exception)
            {
                return EndpointHelpers.Invalid("source", exception.Message);
            }
            catch (ArgumentException exception)
            {
                return Results.Conflict(new { error = "changed", message = exception.Message });
            }
        }).WithName("ActivateCustomMcpServer");

        api.MapPost("/custom-mcp-servers/{id:guid}/revert", async (Guid id, CustomMcpRevertRequest request,
            ICustomMcpServerStore store, ICurrentUser currentUser, CancellationToken ct) =>
            await store.RevertDraftAsync(currentUser.OwnerId, id, request.Revision, ct) is { } server
                ? Results.Ok(server)
                : Results.NotFound())
            .WithName("RevertCustomMcpServer");

        api.MapDelete("/custom-mcp-servers/{id:guid}", async (Guid id, ICustomMcpServerStore store,
                IUserMcpServerRegistry registry, ICurrentUser currentUser, CancellationToken ct) =>
            await CustomMcpServerManager.DeleteAsync(currentUser.OwnerId, id, store, registry, ct)
                ? Results.NoContent()
                : Results.NotFound())
            .WithName("DeleteCustomMcpServer");

        return api;
    }
}
