using Jarvis.Application.Conversations;
using Jarvis.Application.Persona;

namespace Jarvis.Api.Endpoints;

public sealed record SavePersonaRequest(string? CustomInstructions, string? PreferredName, string? ReplyLanguage);
public sealed record AddPersonaTraitRequest(string? Category, string? Statement);
public sealed record UpdatePersonaTraitRequest(string? Statement, bool? Pinned);
public sealed record MessageFeedbackRequest(string? Rating, string? Note);

internal static class PersonaEndpoints
{
    public static RouteGroupBuilder MapPersonaEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/persona", async (PersonaService persona, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok(await persona.GetAsync(currentUser.OwnerId, ct)))
            .WithName("GetPersona");

        api.MapPut("/persona", async (SavePersonaRequest request, PersonaService persona, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await persona.SaveProfileAsync(currentUser.OwnerId, request.CustomInstructions,
                    request.PreferredName, request.ReplyLanguage, ct));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("persona", exception.Message);
            }
        }).WithName("SavePersona");

        api.MapPost("/persona/traits", async (AddPersonaTraitRequest request, PersonaService persona,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await persona.AddUserTraitAsync(currentUser.OwnerId, request.Category ?? "other",
                    request.Statement ?? string.Empty, ct));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("statement", exception.Message);
            }
        }).WithName("AddPersonaTrait");

        api.MapPatch("/persona/traits/{id:guid}", async (Guid id, UpdatePersonaTraitRequest request,
            PersonaService persona, ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var trait = await persona.UpdateTraitAsync(currentUser.OwnerId, id, request.Statement, request.Pinned, ct);
                return trait is null ? Results.NotFound() : Results.Ok(trait);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("statement", exception.Message);
            }
        }).WithName("UpdatePersonaTrait");

        api.MapDelete("/persona/traits/{id:guid}", async (Guid id, PersonaService persona, ICurrentUser currentUser,
                CancellationToken ct) =>
            await persona.RemoveTraitAsync(currentUser.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("RemovePersonaTrait");

        api.MapPost("/conversations/{conversationId:guid}/messages/{messageId:guid}/feedback", async (
            Guid conversationId, Guid messageId, MessageFeedbackRequest request, IConversationStore conversations,
            IMessageFeedbackRepository feedback, Jarvis.Application.Learning.ILearningRecorder learning,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (request.Rating is not ("up" or "down"))
                return EndpointHelpers.Invalid("rating", "Rate a reply up or down.");
            var note = request.Note?.Trim();
            if (note?.Length > 1_000) return EndpointHelpers.Invalid("note", "Keep feedback under 1,000 characters.");
            if (await conversations.GetAsync(conversationId, currentUser.OwnerId, ct) is null) return Results.NotFound();
            var message = (await conversations.GetMessagesAsync(conversationId, ct))
                .FirstOrDefault(item => item.Id == messageId);
            if (message is not { Role: "assistant" }) return Results.NotFound();
            var saved = await feedback.SaveAsync(currentUser.OwnerId, conversationId, messageId, request.Rating,
                string.IsNullOrEmpty(note) ? null : note, ct);
            // The note stays in message_feedback; the signal only says which way the reply was rated. A re-rating
            // adds a newer signal, and readers use the latest one per message.
            learning.RecordSignal(currentUser.OwnerId, conversationId,
                request.Rating == "up" ? Jarvis.Application.Learning.LearningSignalKinds.ThumbsUp
                    : Jarvis.Application.Learning.LearningSignalKinds.ThumbsDown, messageId);
            return Results.Ok(saved);
        }).WithName("RateMessage");

        return api;
    }
}
