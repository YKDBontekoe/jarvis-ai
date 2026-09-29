using System.Text.Json;
using Jarvis.Api.Conversations;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Application.Realtime;
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
            IUiSurfaceRepository surfaces, RemoteQueryExecutor remote, ICurrentUser currentUser,
            IRealtimePublisher realtime, IIntegrationCredentialStore credentials, CancellationToken ct) =>
        {
            var surface = await surfaces.GetAsync(currentUser.OwnerId, id, ct);
            if (surface is null) return Results.NotFound();
            if (surface.Status != "open")
                return Results.Conflict(new { message = "This card was already used." });
            var actionId = request.ActionId?.Trim();
            if (string.IsNullOrEmpty(actionId) || actionId.Length > 40)
                return EndpointHelpers.Invalid("actionId", "Choose one of the card's actions.");
            var values = request.Values ?? [];
            if (values.Count > UiSurfaceAnswers.MaxFieldValues)
                return EndpointHelpers.Invalid("values", "Send at most 12 field values.");
            if (!UiSurfaceSchema.TryParse(surface.SchemaJson, out var schema))
            {
                using var empty = JsonDocument.Parse("{}");
                schema = empty.RootElement.Clone();
            }
            if (values.Any(pair => pair.Key.Length > 40 ||
                                   !UiSurfaceAnswers.IsAllowedValue(schema, pair.Key, pair.Value)))
                return EndpointHelpers.Invalid("values", "Field values must be short.");
            try
            {
                foreach (var secret in UiSurfaceAnswers.SecretsToStore(schema, values))
                    await credentials.SaveSecretAsync(currentUser.OwnerId, secret.Provider, secret.SecretName,
                        secret.Value, CancellationToken.None);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("credential", exception.Message);
            }
            var stored = UiSurfaceAnswers.Redact(schema, values);
            var valuesJson = JsonSerializer.Serialize(stored);
            await surfaces.CompleteAsync(currentUser.OwnerId, id, actionId, valuesJson, CancellationToken.None);
            var content = UiSurfaceAnswers.Describe(surface.Title, actionId, values, schema);
            ConversationTurnResult result;
            try
            {
                result = await remote.SendAsync(currentUser.OwnerId, surface.ConversationId, content);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            if (result is ConversationTurnResult.Failed)
            {
                foreach (var changed in await surfaces.ReopenAsync(currentUser.OwnerId, id, CancellationToken.None))
                    await realtime.PublishToConversationAsync(changed.ConversationId, "ui.surface", Payload(changed),
                        CancellationToken.None);
            }
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

    private static object Payload(UiSurfaceRecord surface) => new
    {
        id = surface.Id,
        conversationId = surface.ConversationId,
        kind = surface.Kind,
        title = surface.Title,
        status = surface.Status,
        schema = JsonSerializer.Deserialize<JsonElement>(surface.SchemaJson)
    };
}
