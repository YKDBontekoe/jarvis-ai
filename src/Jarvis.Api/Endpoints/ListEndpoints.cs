using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Lists;

namespace Jarvis.Api.Endpoints;

/// <summary>The owner's personal lists (groceries, to-dos). Audit entries never include list or item text.</summary>
internal static class ListEndpoints
{
    public static RouteGroupBuilder MapListEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var lists = api.MapGroup("/lists");

        lists.MapGet("", async (IListService service, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await service.ListAsync(currentUser.OwnerId, ct)).Select(list => list.ToDto())))
            .WithName("ListPersonalLists");

        lists.MapGet("/{id:guid}", async (Guid id, IListService service, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var list = await service.GetAsync(id, currentUser.OwnerId, ct);
            return list is null ? Results.NotFound() : Results.Ok(list.ToDto());
        }).WithName("GetPersonalList");

        lists.MapPost("", async (PersonalListRequest request, IListService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(currentUser.OwnerId, request.Name, request.Kind, ct);
            if (Failed(result) is { } failure) return failure;
            var list = result.Value!;
            if (request.Items is { Length: > 0 } items)
            {
                var added = await service.AddItemsAsync(currentUser.OwnerId, list.Id, items, ct);
                if (added.Succeeded) list = added.Value!.List;
            }
            await AuditAsync(audit, logger, currentUser, "list.created", list.Id, ct);
            return Results.Created($"/api/v1/lists/{list.Id}", list.ToDto());
        }).WithName("CreatePersonalList");

        lists.MapPut("/{id:guid}", async (Guid id, PersonalListRequest request, IListService service,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(id, currentUser.OwnerId, request.Name, request.Kind, ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, currentUser, "list.updated", id, ct);
            var list = await service.GetAsync(id, currentUser.OwnerId, ct);
            return list is null ? Results.NotFound() : Results.Ok(list.ToDto());
        }).WithName("UpdatePersonalList");

        lists.MapDelete("/{id:guid}", async (Guid id, IListService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await service.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "list.deleted", id, ct, risk: "moderate");
            return Results.NoContent();
        }).WithName("DeletePersonalList");

        lists.MapPost("/{id:guid}/items", async (Guid id, ListItemsRequest request, IListService service,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.AddItemsAsync(currentUser.OwnerId, id, request.Items ?? [], ct);
            if (Failed(result) is { } failure) return failure;
            var value = result.Value!;
            if (value.Added.Count > 0 || value.Reopened.Count > 0)
                await AuditAsync(audit, logger, currentUser, "list.items_added", id, ct,
                    value.Added.Count + value.Reopened.Count);
            return Results.Ok(value.List.ToDto());
        }).WithName("AddPersonalListItems");

        lists.MapPatch("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, ListItemUpdateRequest request,
            IListService service, IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.UpdateItemAsync(currentUser.OwnerId, id, itemId, request.Text, request.Done, ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, currentUser, "list.item_updated", id, ct);
            return Results.Ok(result.Value!.ToDto());
        }).WithName("UpdatePersonalListItem");

        lists.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, IListService service,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await service.DeleteItemAsync(currentUser.OwnerId, id, itemId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "list.items_removed", id, ct, 1);
            return Results.NoContent();
        }).WithName("DeletePersonalListItem");

        lists.MapPost("/{id:guid}/clear-checked", async (Guid id, IListService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var removed = await service.ClearDoneAsync(currentUser.OwnerId, id, ct);
            if (removed is null) return Results.NotFound();
            if (removed > 0) await AuditAsync(audit, logger, currentUser, "list.items_removed", id, ct, removed);
            var list = await service.GetAsync(id, currentUser.OwnerId, ct);
            return list is null ? Results.NotFound() : Results.Ok(list.ToDto());
        }).WithName("ClearCheckedPersonalListItems");

        return api;
    }

    private static IResult? Failed<T>(ListOperation<T> result) => result.Failure switch
    {
        ListFailure.None => null,
        ListFailure.NotFound => Results.NotFound(),
        ListFailure.Conflict => ApiProblemResults.Conflict(result.Message ?? "That list already exists."),
        _ => ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.")
    };

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid listId, CancellationToken ct, int? count = null, string risk = "low") =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "lists", action, risk, true, null,
            JsonSerializer.Serialize(new { resourceId = listId, count, source = "app" }), ct);
}
