using Jarvis.Application.Conversations;
using Jarvis.Application.Game;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// The game: Jarvis's bets against the owner, the week's side quests, the race against last month's self and the
/// scoreboard. Reading the board also settles finished bets and writes the week's quests. Owner-scoped throughout.
/// </summary>
internal static class GameEndpoints
{
    public static RouteGroupBuilder MapGameEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/game").WithTags("Game");

        group.MapGet("", async (IGameService game, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await game.BoardAsync(user.OwnerId, ct))).WithName("GetGameBoard");

        group.MapPost("/refresh", async (IGameService game, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await game.RefreshAsync(user.OwnerId, ct))).WithName("RefreshGame");

        group.MapPost("/quests/{id:guid}/complete", async (Guid id, IGameService game, ICurrentUser user,
                CancellationToken ct) =>
            await game.CompleteQuestAsync(id, user.OwnerId, ct) is { } quest ? Results.Ok(quest) : Results.NotFound())
            .WithName("CompleteGameQuest");

        group.MapPost("/quests/{id:guid}/skip", async (Guid id, IGameService game, ICurrentUser user,
                CancellationToken ct) =>
            await game.SkipQuestAsync(id, user.OwnerId, ct) is { } quest ? Results.Ok(quest) : Results.NotFound())
            .WithName("SkipGameQuest");

        group.MapPost("/bets/{id:guid}/call-off", async (Guid id, IGameService game, ICurrentUser user,
                CancellationToken ct) =>
            await game.CallOffBetAsync(id, user.OwnerId, ct) is { } bet ? Results.Ok(bet) : Results.NotFound())
            .WithName("CallOffGameBet");

        return api;
    }
}
