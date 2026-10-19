using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// Goal-led app discovery: what the owner wants done, what Jarvis can really do with the connected apps, the one
/// real next step when it cannot, a harmless first use, and pinned actions. Setup itself stays in the existing
/// pack, OAuth and credential flows; no secret passes through here.
/// </summary>
internal static class AppGoalEndpoints
{
    public static RouteGroupBuilder MapAppGoalEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/apps/goals").WithTags("App goals");

        group.MapGet("", async (string? q, IAppGoalService goals, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await goals.SearchAsync(user.OwnerId, q, ct))).WithName("SearchAppGoals");

        group.MapPost("/{goalId}/try", async (string goalId, IAppGoalService goals, ICurrentUser user,
            CancellationToken ct) =>
            await goals.TryAsync(user.OwnerId, goalId, TimeProvider.System.GetUtcNow(), ct) is { } result
                ? Results.Ok(result)
                : Results.NotFound()).WithName("TryAppGoal");

        group.MapPut("/{goalId}/pin", async (string goalId, IAppGoalService goals, ICurrentUser user,
            CancellationToken ct) =>
            await goals.PinAsync(user.OwnerId, goalId, ct)
                ? Results.NoContent()
                : EndpointHelpers.Invalid("goal", "Connect the app first, or unpin one of your six pinned actions."))
            .WithName("PinAppGoal");

        group.MapDelete("/{goalId}/pin", async (string goalId, IAppGoalService goals, ICurrentUser user,
            CancellationToken ct) =>
            await goals.UnpinAsync(user.OwnerId, goalId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("UnpinAppGoal");

        return api;
    }
}
