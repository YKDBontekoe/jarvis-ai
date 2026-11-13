using Jarvis.Application.Conversations;
using Jarvis.Application.Discover;

namespace Jarvis.Api.Endpoints;

public sealed record DuelChallengeBody(Guid? RemoteAgentId, string? Metric, int? Days);

public sealed record DuelJoinBody(Guid? RemoteAgentId, string? Code);

/// <summary>Collisions, the life museum and duels with friends' Jarvises. Owner-scoped.</summary>
internal static class DiscoverEndpoints
{
    public static RouteGroupBuilder MapDiscoverEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/discover").WithTags("Discover");

        group.MapGet("/collision", async (ICollisionService collisions, ICurrentUser user, CancellationToken ct) =>
            await collisions.TodayAsync(user.OwnerId, ct) is { } collision
                ? Results.Ok(collision)
                : Results.NoContent()).WithName("GetCollision");

        group.MapPost("/collision/next", async (ICollisionService collisions, ICurrentUser user,
            CancellationToken ct) =>
            await collisions.NextAsync(user.OwnerId, ct) is { } collision
                ? Results.Ok(collision)
                : Results.NoContent()).WithName("NextCollision");

        group.MapGet("/museum", async (int? year, IMuseumService museum, ICurrentUser user, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await museum.BuildAsync(user.OwnerId, year, ct));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("year", exception.Message);
            }
        }).WithName("GetMuseum");

        var duels = api.MapGroup("/duels").WithTags("Discover");

        duels.MapGet("", async (IDuelService service, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(user.OwnerId, ct))).WithName("ListDuels");

        duels.MapPost("", async (DuelChallengeBody body, IDuelService service, ICurrentUser user,
            CancellationToken ct) =>
        {
            if (body.RemoteAgentId is not { } agentId)
                return EndpointHelpers.Invalid("remoteAgentId", "Choose a friend’s Jarvis from Agents.");
            var result = await service.ChallengeAsync(user.OwnerId, agentId, body.Metric ?? string.Empty,
                body.Days ?? 7, ct);
            return result.Value is { } duel
                ? Results.Created($"/api/v1/duels/{duel.Id}", duel)
                : EndpointHelpers.Invalid(result.Field ?? "duel", result.Error ?? "Could not start the duel.");
        }).WithName("ChallengeDuel");

        duels.MapPost("/join", async (DuelJoinBody body, IDuelService service, ICurrentUser user,
            CancellationToken ct) =>
        {
            if (body.RemoteAgentId is not { } agentId)
                return EndpointHelpers.Invalid("remoteAgentId", "Choose the friend’s Jarvis that challenged you.");
            var result = await service.JoinAsync(user.OwnerId, agentId, body.Code ?? string.Empty, ct);
            return result.Value is { } duel
                ? Results.Created($"/api/v1/duels/{duel.Id}", duel)
                : EndpointHelpers.Invalid(result.Field ?? "code", result.Error ?? "Could not join the duel.");
        }).WithName("JoinDuel");

        duels.MapDelete("/{id:guid}", async (Guid id, IDuelService service, ICurrentUser user,
            CancellationToken ct) =>
            await service.CancelAsync(id, user.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("CancelDuel");

        return api;
    }
}
