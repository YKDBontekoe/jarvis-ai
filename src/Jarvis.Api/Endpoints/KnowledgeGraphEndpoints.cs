using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;

namespace Jarvis.Api.Endpoints;

internal static class KnowledgeGraphEndpoints
{
    public static RouteGroupBuilder MapKnowledgeGraphEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/graph/overview", async (int? limit, IKnowledgeGraphRepository graph, ICurrentUser currentUser,
                CancellationToken ct) =>
                Results.Ok(await graph.GetOverviewAsync(currentUser.OwnerId, Math.Clamp(limit ?? 60, 1, 150), ct)))
            .WithName("GetKnowledgeGraphOverview");

        api.MapGet("/graph/entities", async (string? search, IKnowledgeGraphRepository graph, ICurrentUser currentUser,
                CancellationToken ct) =>
            {
                if (search?.Length > 200) return EndpointHelpers.Invalid("search", "Search with 200 characters or fewer.");
                return Results.Ok(await graph.ListEntitiesAsync(currentUser.OwnerId, search, 100, ct));
            })
            .WithName("ListKnowledgeGraphEntities");

        api.MapGet("/graph/entities/{id:guid}", async (Guid id, IKnowledgeGraphRepository graph,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var details = await graph.GetEntityAsync(currentUser.OwnerId, id, ct);
            return details is null ? Results.NotFound() : Results.Ok(details);
        }).WithName("GetKnowledgeGraphEntity");

        api.MapDelete("/graph/entities/{id:guid}", async (Guid id, IKnowledgeGraphRepository graph,
                ICurrentUser currentUser, CancellationToken ct) =>
            await graph.DeleteEntityAsync(currentUser.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteKnowledgeGraphEntity");

        api.MapGet("/memory/index-status", async (IMemoryIndexRepository index, ICurrentUser currentUser,
                CancellationToken ct) => Results.Ok(await index.GetStatusAsync(currentUser.OwnerId, ct)))
            .WithName("GetMemoryIndexStatus");

        return api;
    }
}
