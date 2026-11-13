using Jarvis.Application.Conversations;
using Jarvis.Application.Home;

namespace Jarvis.Api.Endpoints;

public sealed record HomeLayoutTileRequest(string? Id, string? Size);

public sealed record HomeLayoutRequest(IReadOnlyList<HomeLayoutTileRequest>? Tiles, IReadOnlyList<string>? HiddenCards);

/// <summary>
/// The owner's Home (tiles in order with their sizes, and the cards they put away), shared by every device and by
/// Jarvis, which can rearrange it from chat.
/// </summary>
internal static class HomeLayoutEndpoints
{
    public static RouteGroupBuilder MapHomeLayoutEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/home/layout").WithTags("Home");

        group.MapGet("", async (IHomeLayoutService layouts, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await layouts.GetAsync(user.OwnerId, ct))).WithName("GetHomeLayout");

        group.MapPut("", async (HomeLayoutRequest request, IHomeLayoutService layouts, ICurrentUser user,
                CancellationToken ct) =>
        {
            if (request.Tiles is null) return Results.BadRequest(new { error = "tiles is required." });
            var tiles = request.Tiles.Select(t => new HomeLayoutTile(t?.Id ?? "", t?.Size ?? "")).ToList();
            return Result(await layouts.SaveFromAppAsync(user.OwnerId, tiles, request.HiddenCards, ct));
        }).WithName("SaveHomeLayout");

        group.MapPost("/undo", async (IHomeLayoutService layouts, ICurrentUser user, CancellationToken ct) =>
            Result(await layouts.UndoAsync(user.OwnerId, ct))).WithName("UndoHomeLayout");

        group.MapPost("/reset", async (IHomeLayoutService layouts, ICurrentUser user, CancellationToken ct) =>
            Result(await layouts.ResetAsync(user.OwnerId, HomeLayoutAuthors.App, ct))).WithName("ResetHomeLayout");

        return api;
    }

    private static IResult Result(HomeLayoutResult result) =>
        result.Succeeded ? Results.Ok(result.View) : Results.Conflict(new { error = result.Error });
}
