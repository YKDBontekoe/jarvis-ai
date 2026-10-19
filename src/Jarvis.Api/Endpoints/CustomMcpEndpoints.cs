using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Mcp;

namespace Jarvis.Api.Endpoints;

/// <summary>Apps Jarvis wrote for the owner: read the code, test it, approve it, roll back or remove it.</summary>
internal static class CustomMcpEndpoints
{
    private sealed class CustomMcpEndpointsMarker;

    public sealed record CustomMcpTestRequest(string? ToolName, string? ArgumentsJson);
    public sealed record CustomMcpActivateRequest(string? Sha256, string? OverrideReason = null);
    public sealed record BuilderRequest(string? Purpose, string? Inputs, string? Data, string? Result, bool? ChangesOutside);
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

        // The owner approves the exact code they reviewed. The hash is required, and the code must have passed a
        // test unless the owner gives a reason; the reason is kept in the audit trail.
        api.MapPost("/custom-mcp-servers/{id:guid}/activate", async (Guid id, CustomMcpActivateRequest? request,
            ICustomMcpServerStore store, IUserMcpServerRegistry registry, IAuditEventStore audit,
            ICurrentUser currentUser, ILogger<CustomMcpEndpointsMarker> logger, CancellationToken ct) =>
        {
            try
            {
                var (server, _, reason) = await CustomMcpServerManager.ActivateAsync(currentUser.OwnerId, id,
                    request?.Sha256, store, registry, ct, request?.OverrideReason);
                await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "custom_apps",
                    reason is null ? "custom_app.activated" : "custom_app.activated_untested", "medium", true, null,
                    System.Text.Json.JsonSerializer.Serialize(new { resourceId = id, sha256 = server.ActiveSha256, reason }),
                    ct);
                return Results.Ok(server);
            }
            catch (CustomMcpScriptException exception)
            {
                return EndpointHelpers.Invalid("source", exception.Message);
            }
            catch (CustomMcpReviewException exception) when (exception.Code == "hash_required")
            {
                return EndpointHelpers.Invalid("sha256", exception.Message);
            }
            catch (CustomMcpReviewException exception)
            {
                return Results.Conflict(new { error = exception.Code, message = exception.Message });
            }
            catch (ArgumentException exception)
            {
                return Results.Conflict(new { error = "changed", message = exception.Message });
            }
        }).WithName("ActivateCustomMcpServer");

        // What is about to be approved, set against what runs now: changed lines, dependencies, keys and how the
        // draft was tested.
        api.MapGet("/custom-mcp-servers/{id:guid}/review", async (Guid id, ICustomMcpServerStore store,
                ICurrentUser currentUser, CancellationToken ct) =>
            await store.GetAsync(currentUser.OwnerId, id, ct) is { } detail
                ? Results.Ok(CustomMcpReviewBuilder.Build(detail))
                : Results.NotFound())
            .WithName("ReviewCustomMcpServer");

        api.MapGet("/custom-mcp-servers/starters", () => Results.Ok(CustomAppStarter.Examples))
            .WithName("ListCustomAppStarters");

        api.MapPost("/custom-mcp-servers/starters/compose", (BuilderRequest request) =>
        {
            var (prompt, field, message) = CustomAppStarter.Compose(new CustomAppStarter.Answers(request.Purpose,
                request.Inputs, request.Data, request.Result, request.ChangesOutside == true));
            return prompt is null
                ? EndpointHelpers.Invalid(field ?? "purpose", message ?? "The request is invalid.")
                : Results.Ok(new { prompt });
        }).WithName("ComposeCustomAppStarter");

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
