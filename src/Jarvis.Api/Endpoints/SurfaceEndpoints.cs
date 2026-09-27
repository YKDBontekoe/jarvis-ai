using System.Text.Json;
using Jarvis.Api.Conversations;
using Jarvis.Application.Conversations;
using Jarvis.Application.Surfaces;

namespace Jarvis.Api.Endpoints;

public sealed record UiSurfaceDto(Guid Id, Guid ConversationId, string Kind, string Title, string Status,
    JsonElement Schema, DateTimeOffset CreatedAt);
public sealed record UiSurfaceActionRequest(string? ActionId, Dictionary<string, string>? Values);

internal static class SurfaceEndpoints
{
    public static RouteGroupBuilder MapSurfaceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/conversations/{conversationId:guid}/surfaces", async (Guid conversationId,
            IUiSurfaceRepository surfaces, IConversationStore conversations, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (await conversations.GetAsync(conversationId, currentUser.OwnerId, ct) is null)
                return Results.NotFound();
            var list = await surfaces.ListForConversationAsync(currentUser.OwnerId, conversationId, ct);
            return Results.Ok(list.Select(ToDto));
        }).WithName("ListConversationSurfaces");

        api.MapPost("/ui-surfaces/{id:guid}/actions", async (Guid id, UiSurfaceActionRequest request,
            IUiSurfaceRepository surfaces, ConversationTurnService turns, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var surface = await surfaces.GetAsync(currentUser.OwnerId, id, ct);
            if (surface is null) return Results.NotFound();
            if (surface.Status != "open")
                return Results.Conflict(new { message = "This card was already used." });
            var actionId = request.ActionId?.Trim();
            if (string.IsNullOrEmpty(actionId) || actionId.Length > 40)
                return EndpointHelpers.Invalid("actionId", "Choose one of the card's actions.");
            var values = request.Values ?? [];
            if (values.Count > 12)
                return EndpointHelpers.Invalid("values", "Send at most 12 field values.");
            if (values.Any(pair => pair.Key.Length > 40 || pair.Value.Length > 500))
                return EndpointHelpers.Invalid("values", "Field values must be short.");
            var valuesJson = JsonSerializer.Serialize(values);
            await surfaces.CompleteAsync(currentUser.OwnerId, id, actionId, valuesJson, ct);
            var content = values.Count == 0
                ? $"I chose '{actionId}' on the {surface.Title} card."
                : $"I chose '{actionId}' on the {surface.Title} card: {string.Join(", ", values.Select(pair => $"{pair.Key}={pair.Value}"))}.";
            var result = await turns.SendAsync(currentUser.OwnerId, surface.ConversationId, content, ct);
            if (result is ConversationTurnResult.Failed)
                await surfaces.ReopenAsync(currentUser.OwnerId, id, CancellationToken.None);
            return result.ToHttpResult();
        }).WithName("SubmitUiSurfaceAction");

        return api;
    }

    internal static UiSurfaceDto ToDto(UiSurfaceRecord surface)
    {
        using var document = JsonDocument.Parse(surface.SchemaJson);
        return new UiSurfaceDto(surface.Id, surface.ConversationId, surface.Kind, surface.Title, surface.Status,
            document.RootElement.Clone(), surface.CreatedAt);
    }
}
