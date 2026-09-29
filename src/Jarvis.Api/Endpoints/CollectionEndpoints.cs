using Jarvis.Application.Conversations;
using Jarvis.Application.Files;

namespace Jarvis.Api.Endpoints;

public sealed record SaveDocumentCollectionRequest(string? Name, string? Description, IReadOnlyList<Guid>? FileIds);

internal static class CollectionEndpoints
{
    public static RouteGroupBuilder MapCollectionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/collections");

        group.MapGet("", async (IDocumentCollectionRepository collections, ICurrentUser currentUser,
                CancellationToken ct) =>
                Results.Ok(await collections.ListAsync(currentUser.OwnerId, ct)))
            .WithName("ListDocumentCollections");

        group.MapGet("/{id:guid}", async (Guid id, IDocumentCollectionRepository collections, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var collection = await collections.GetAsync(id, currentUser.OwnerId, ct);
            return collection is null ? Results.NotFound() : Results.Ok(collection);
        }).WithName("GetDocumentCollection");

        group.MapPost("", async (SaveDocumentCollectionRequest request, IDocumentCollectionRepository collections,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var created = await collections.CreateAsync(currentUser.OwnerId,
                    new DocumentCollectionDraft(request.Name ?? "", request.Description, request.FileIds), ct);
                return Results.Created($"/api/v1/collections/{created.Id}", created);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("collection", exception.Message);
            }
        }).WithName("CreateDocumentCollection");

        group.MapPut("/{id:guid}", async (Guid id, SaveDocumentCollectionRequest request,
            IDocumentCollectionRepository collections, ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var updated = await collections.UpdateAsync(id, currentUser.OwnerId,
                    new DocumentCollectionDraft(request.Name ?? "", request.Description, request.FileIds), ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("collection", exception.Message);
            }
        }).WithName("UpdateDocumentCollection");

        group.MapDelete("/{id:guid}", async (Guid id, IDocumentCollectionRepository collections,
            ICurrentUser currentUser, CancellationToken ct) =>
            await collections.DeleteAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteDocumentCollection");

        return api;
    }
}
