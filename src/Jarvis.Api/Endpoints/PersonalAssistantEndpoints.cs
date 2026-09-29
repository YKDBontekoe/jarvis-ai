using System.Net;
using Jarvis.Application.Conversations;
using Jarvis.Application.Devices;
using Jarvis.Application.Home;
using Jarvis.Application.Integrations;
using Jarvis.Application.Memory;
using Jarvis.Application.Workflows;
using Jarvis.Mcp;

namespace Jarvis.Api.Endpoints;

internal static class PersonalAssistantEndpoints
{
    public static RouteGroupBuilder MapPersonalAssistantEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/home", async (IHomeBriefingService home, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await home.GetAsync(currentUser.OwnerId, ct))).WithName("GetHomeBriefing");

        api.MapPost("/devices/telemetry", async (SaveDeviceTelemetryRequest request, IDeviceTelemetryStore telemetry,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
                return EndpointHelpers.Invalid("location", "Latitude and longitude must be valid WGS84 values.");
            if (request.BatteryPercent is < 0 or > 100)
                return EndpointHelpers.Invalid("battery", "Battery percent must be between 0 and 100.");
            return Results.Ok(await telemetry.SaveAsync(currentUser.OwnerId, request, ct));
        }).WithName("SaveDeviceTelemetry");

        api.MapGet("/integrations/packs", async (IIntegrationCredentialStore credentials,
            IUserMcpServerRegistry servers, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await IntegrationPackCatalog.ListAsync(credentials, servers, currentUser.OwnerId, ct)))
            .WithName("ListIntegrationPacks");

        api.MapPost("/integrations/packs/{id}", async (string id, InstallIntegrationPackRequest request,
            IIntegrationCredentialStore credentials, IUserMcpServerRegistry servers, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await IntegrationPackInstaller.InstallAsync(currentUser.OwnerId, id, request,
                    credentials, servers, ct));
            }
            catch (ArgumentException exception)
            {
                return exception.Message.Contains("Unknown", StringComparison.Ordinal)
                    ? Results.NotFound()
                    : EndpointHelpers.Invalid("pack", exception.Message);
            }
        }).WithName("InstallIntegrationPack");

        api.MapPost("/integrations/oauth/sessions", async (StartMcpOAuthRequest request, IMcpOAuthService oauth,
            IConfiguration configuration, HttpRequest http, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var publicBase = configuration["Jarvis:PublicBaseUrl"]?.TrimEnd('/')
                             ?? configuration["Channels:PublicBaseUrl"]?.TrimEnd('/')
                             ?? $"{http.Scheme}://{http.Host}";
            try
            {
                return Results.Ok(await oauth.StartAsync(currentUser.OwnerId, request, publicBase, ct));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("oauth", exception.Message);
            }
        }).WithName("StartMcpOAuth");

        api.MapGet("/integrations/oauth/sessions/{id:guid}", async (Guid id, IMcpOAuthService oauth,
                ICurrentUser currentUser, CancellationToken ct) =>
            await oauth.GetAsync(currentUser.OwnerId, id, ct) is { } session ? Results.Ok(session) : Results.NotFound())
            .WithName("GetMcpOAuthSession");

        api.MapGet("/integrations/oauth/callback", async (string? state, string? code, string? error,
            IMcpOAuthService oauth, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(state))
                return Results.BadRequest("Missing OAuth state.");
            var html = await oauth.CompleteAsync(state, code, error, ct);
            return Results.Content(html, "text/html; charset=utf-8");
        }).WithName("McpOAuthCallback").AllowAnonymous();

        api.MapGet("/coding/runs", async (ICodingRunStore runs, ICurrentUser currentUser, CancellationToken ct) =>
            Results.Ok(await runs.ListAsync(currentUser.OwnerId, ct))).WithName("ListCodingRuns");

        api.MapGet("/coding/runs/{id:guid}", async (Guid id, ICodingRunStore runs, ICurrentUser currentUser,
                CancellationToken ct) =>
            await runs.GetAsync(id, currentUser.OwnerId, ct) is { } run ? Results.Ok(run) : Results.NotFound())
            .WithName("GetCodingRun");

        api.MapPut("/graph/entities/{id:guid}", async (Guid id, UpdateGraphEntityRequest request,
            IKnowledgeGraphRepository graph, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var updated = await graph.UpdateEntityAsync(currentUser.OwnerId, id, request.Name, request.Type,
                request.Summary, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).WithName("UpdateKnowledgeGraphEntity");

        api.MapPost("/graph/facts", async (ProposeGraphFactRequest request, IKnowledgeGraphRepository graph,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Predicate) ||
                string.IsNullOrWhiteSpace(request.Object))
                return EndpointHelpers.Invalid("fact", "A fact needs a subject, predicate, and object.");
            var count = await graph.MergeAsync(currentUser.OwnerId,
            [
                new GraphFact(request.Subject, request.SubjectType ?? "thing", request.Predicate, request.Object,
                    request.ObjectType, request.ObjectIsEntity, request.Exclusive, DateTimeOffset.UtcNow, 1f)
            ], null, ct);
            return Results.Ok(new { merged = count });
        }).WithName("ProposeKnowledgeGraphFact");

        api.MapDelete("/graph/relations/{id:guid}", async (Guid id, IKnowledgeGraphRepository graph,
                ICurrentUser currentUser, CancellationToken ct) =>
            await graph.CloseRelationAsync(currentUser.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("CloseKnowledgeGraphRelation");

        api.MapPost("/graph/entities/merge", async (MergeGraphEntitiesRequest request, IKnowledgeGraphRepository graph,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                return await graph.MergeEntitiesAsync(currentUser.OwnerId, request.KeepId, request.AbsorbId, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("merge", exception.Message);
            }
        }).WithName("MergeKnowledgeGraphEntities");

        return api;
    }
}
