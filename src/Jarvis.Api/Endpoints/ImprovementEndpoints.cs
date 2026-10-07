using Jarvis.Application.Conversations;
using Jarvis.Application.Improvements;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// Changes Jarvis would like to make to itself (a memory worth keeping, a skill it learned, a skill that keeps getting
/// thumbs-down) and the few it already made on its own, which can be undone. Nothing here changes anything the owner
/// has not accepted, except the low-risk memories shown as applied.
/// </summary>
internal static class ImprovementEndpoints
{
    public static RouteGroupBuilder MapImprovementEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/improvements").WithTags("Improvements");

        group.MapGet("", async (IImprovementService improvements, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await improvements.ListAsync(user.OwnerId, ct))).WithName("ListImprovements");

        group.MapPost("/refresh", async (IImprovementMiner miner, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await miner.RefreshAsync(user.OwnerId, ct))).WithName("RefreshImprovements");

        group.MapPost("/{id:guid}/accept", async (Guid id, IImprovementService improvements, ICurrentUser user,
            CancellationToken ct) =>
            await improvements.AcceptAsync(id, user.OwnerId, ct) is { } view
                ? Results.Ok(view)
                : Results.NotFound()).WithName("AcceptImprovement");

        group.MapPost("/{id:guid}/dismiss", async (Guid id, IImprovementService improvements, ICurrentUser user,
            CancellationToken ct) =>
            await improvements.DismissAsync(id, user.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DismissImprovement");

        group.MapPost("/{id:guid}/undo", async (Guid id, IImprovementService improvements, ICurrentUser user,
            CancellationToken ct) =>
            await improvements.UndoAsync(id, user.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("UndoImprovement");

        return api;
    }
}
