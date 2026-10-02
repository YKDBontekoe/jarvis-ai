using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Reading;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// The owner's reading list: links saved to read later. The worker fetches and summarizes new links. Audit entries
/// carry the item id only, never the link, title or summary.
/// </summary>
internal static class ReadingEndpoints
{
    public static RouteGroupBuilder MapReadingEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var reading = api.MapGroup("/reading");

        reading.MapGet("", async (IReadingListService service, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await service.ListAsync(currentUser.OwnerId, ct)).Select(item => item.ToDto())))
            .WithName("ListReadingItems");

        reading.MapGet("/{id:guid}", async (Guid id, IReadingListService service, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var item = await service.GetAsync(id, currentUser.OwnerId, ct);
            return item is null ? Results.NotFound() : Results.Ok(item.ToDto());
        }).WithName("GetReadingItem");

        reading.MapPost("", async (ReadingItemRequest request, IReadingListService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.SaveAsync(currentUser.OwnerId, request.Url, request.Note, ReadingSources.App,
                ct);
            if (Failed(result) is { } failure) return failure;
            var value = result.Value!;
            if (value.AlreadySaved) return Results.Ok(value.Item.ToDto());
            await AuditAsync(audit, logger, currentUser, "reading.saved", value.Item.Id, ct);
            return Results.Created($"/api/v1/reading/{value.Item.Id}", value.Item.ToDto());
        }).WithName("SaveReadingItem");

        reading.MapPatch("/{id:guid}", async (Guid id, ReadingItemUpdateRequest request, IReadingListService service,
            IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(id, currentUser.OwnerId, request.Read, request.Note, ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, currentUser, "reading.updated", id, ct);
            return Results.Ok(result.Value!.ToDto());
        }).WithName("UpdateReadingItem");

        reading.MapPost("/{id:guid}/refresh", async (Guid id, IReadingListService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var result = await service.RefreshAsync(id, currentUser.OwnerId, ct);
            if (Failed(result) is { } failure) return failure;
            await AuditAsync(audit, logger, currentUser, "reading.refreshed", id, ct);
            return Results.Ok(result.Value!.ToDto());
        }).WithName("RefreshReadingItem");

        reading.MapDelete("/{id:guid}", async (Guid id, IReadingListService service, IAuditEventStore audit,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (!await service.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
            await AuditAsync(audit, logger, currentUser, "reading.removed", id, ct);
            return Results.NoContent();
        }).WithName("DeleteReadingItem");

        return api;
    }

    private static IResult? Failed<T>(ReadingOperation<T> result) => result.Failure switch
    {
        ReadingFailure.None => null,
        ReadingFailure.NotFound => Results.NotFound(),
        _ => ApiProblemResults.Validation(result.Field ?? "request", result.Message ?? "The request is invalid.")
    };

    private static Task AuditAsync(IAuditEventStore audit, ILogger logger, ICurrentUser currentUser, string action,
        Guid itemId, CancellationToken ct) =>
        EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "reading", action, "low", true, null,
            JsonSerializer.Serialize(new { resourceId = itemId, source = "app" }), ct);
}
