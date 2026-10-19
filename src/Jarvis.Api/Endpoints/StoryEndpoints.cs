using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Stories;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// Life stories over the factual timeline: a chosen window and scope, an outline whose counts match real moments,
/// wording the owner can edit without touching the records, a private saved copy, and a shareable export that
/// leaves out what the owner chose. Nothing is sent or shared by any of it.
/// </summary>
internal static class StoryEndpoints
{
    public static RouteGroupBuilder MapStoryEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/stories").WithTags("Stories");

        group.MapGet("", async (IStoryService stories, ICurrentUser user, CancellationToken ct) =>
            Results.Ok((await stories.ListAsync(user.OwnerId, ct)).Select(draft => new
            {
                draft.Id, draft.Title, draft.Style, draft.From, draft.To, draft.Version, draft.SavedVersion, draft.UpdatedAt
            }))).WithName("ListStories");

        group.MapPost("", async (StoryRequest request, IStoryService stories, ICurrentUser user, CancellationToken ct) =>
        {
            var created = await stories.CreateAsync(user.OwnerId, request, ct);
            return created.Succeeded
                ? Results.Created($"/api/v1/stories/{created.Value!.Id}", created.Value)
                : Failure(created);
        }).WithName("CreateStory");

        group.MapGet("/{id:guid}", async (Guid id, IStoryService stories, ICurrentUser user, CancellationToken ct) =>
            await stories.GetAsync(user.OwnerId, id, ct) is { } draft ? Results.Ok(draft) : Results.NotFound())
            .WithName("GetStory");

        group.MapDelete("/{id:guid}", async (Guid id, IStoryService stories, ICurrentUser user, CancellationToken ct) =>
            await stories.DeleteAsync(user.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteStory");

        group.MapPut("/{id:guid}/items/{itemId}", async (Guid id, string itemId, StoryEditRequest request,
            IStoryService stories, ICurrentUser user, CancellationToken ct) =>
            Done(await stories.EditItemAsync(user.OwnerId, id, itemId, request.Text, ct))).WithName("EditStoryItem");

        group.MapPut("/{id:guid}/items/{itemId}/removed", async (Guid id, string itemId, StoryRemoveRequest request,
            IStoryService stories, ICurrentUser user, CancellationToken ct) =>
            Done(await stories.SetRemovedAsync(user.OwnerId, id, itemId, request.Removed ?? true, ct)))
            .WithName("RemoveStoryItem");

        group.MapPost("/{id:guid}/items/{itemId}/move", async (Guid id, string itemId, StoryMoveRequest request,
            IStoryService stories, ICurrentUser user, CancellationToken ct) =>
            Done(await stories.MoveItemAsync(user.OwnerId, id, itemId, request.Direction, ct))).WithName("MoveStoryItem");

        group.MapPost("/{id:guid}/save", async (Guid id, IStoryService stories, ICurrentUser user,
            CancellationToken ct) => Done(await stories.SaveAsync(user.OwnerId, id, ct))).WithName("SaveStory");

        group.MapGet("/{id:guid}/export", async (Guid id, string? redact, IStoryService stories, ICurrentUser user,
            CancellationToken ct) =>
        {
            var export = await stories.ExportAsync(user.OwnerId, id, redact, ct);
            return export.Succeeded
                ? Results.File(Encoding.UTF8.GetBytes(export.Value!.Content), export.Value.ContentType,
                    export.Value.FileName)
                : Failure(export);
        }).WithName("ExportStory");

        return api;
    }

    private static IResult Done(StoryOperation<StoryDraft> operation) =>
        operation.Succeeded ? Results.Ok(operation.Value) : Failure(operation);

    private static IResult Failure<T>(StoryOperation<T> operation) => operation.Failure switch
    {
        StoryFailure.NotFound => Results.NotFound(),
        StoryFailure.Conflict => Results.Conflict(new { message = operation.Message }),
        _ => EndpointHelpers.Invalid(operation.Field ?? "story", operation.Message ?? "The request is invalid.")
    };
}
