using Jarvis.Application.Conversations;
using Jarvis.Application.Lists;

namespace Jarvis.Api.Endpoints;

public sealed record ListItemsRequest(string? List, IReadOnlyList<string>? Items);

public sealed record ListDoneRequest(string? List, IReadOnlyList<string>? Items, bool? Done);

public sealed record ListNameRequest(string? List);

/// <summary>Plain lists (shopping, packing, ...) the owner keeps; items are ticked off and cleared.</summary>
internal static class ListEndpoints
{
    public static RouteGroupBuilder MapListEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/lists").WithTags("Lists");

        group.MapGet("", async (IListService lists, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await lists.ListAsync(user.OwnerId, ct))).WithName("ListLists");

        group.MapPost("/items", async (ListItemsRequest request, IListService lists, ICurrentUser user, CancellationToken ct) =>
            Result(await lists.AddItemsAsync(user.OwnerId, request.List ?? "", request.Items ?? [], ct)))
            .WithName("AddListItems");

        group.MapPost("/done", async (ListDoneRequest request, IListService lists, ICurrentUser user, CancellationToken ct) =>
            Result(await lists.SetDoneAsync(user.OwnerId, request.List ?? "", request.Items ?? [], request.Done ?? true, ct)))
            .WithName("SetListItemsDone");

        group.MapPost("/remove", async (ListItemsRequest request, IListService lists, ICurrentUser user, CancellationToken ct) =>
            Result(await lists.RemoveItemsAsync(user.OwnerId, request.List ?? "", request.Items ?? [], ct)))
            .WithName("RemoveListItems");

        group.MapPost("/clear-done", async (ListNameRequest request, IListService lists, ICurrentUser user, CancellationToken ct) =>
            Result(await lists.ClearDoneAsync(user.OwnerId, request.List ?? "", ct)))
            .WithName("ClearDoneListItems");

        group.MapDelete("/{id:guid}", async (Guid id, IListService lists, ICurrentUser user, CancellationToken ct) =>
            await lists.DeleteListAsync(user.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteList");

        return api;
    }

    private static IResult Result(ListOperation<ListRecord> operation) => operation.Failure switch
    {
        ListFailure.None => Results.Ok(operation.Value),
        ListFailure.NotFound => Results.NotFound(),
        ListFailure.Ambiguous => Results.Conflict(new { message = operation.Message }),
        _ => EndpointHelpers.Invalid("list", operation.Message ?? "Invalid list request."),
    };
}
