using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Knowing;

namespace Jarvis.Api.Endpoints;

public sealed record TrainingAnswerBody(string? Verdict);

public sealed record GapAnswerBody(Guid? EntityId, string? Predicate, string? Answer);

public sealed record ChatModeBody(string? Kind, Guid? PersonId, string? Scenario, string? Goal, DateOnly? AsOf,
    string? Question, IReadOnlyList<string>? Advisers);

/// <summary>
/// How well Jarvis knows the owner: train its memory with true / wrong / outdated cards, clear the map of the unknown
/// one question at a time, see the trust ladder, and start special chats. Everything is owner-scoped.
/// </summary>
internal static class KnowingEndpoints
{
    public static RouteGroupBuilder MapKnowingEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var group = api.MapGroup("/knowing").WithTags("Knowing");

        group.MapGet("/training", async (ITrainingService training, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await training.DeckAsync(user.OwnerId, ct))).WithName("GetTrainingDeck");

        group.MapPost("/training/{memoryId:guid}", async (Guid memoryId, TrainingAnswerBody body,
            ITrainingService training, IAuditEventStore audit, ICurrentUser user, CancellationToken ct) =>
        {
            try
            {
                var answer = await training.AnswerAsync(user.OwnerId, memoryId, body.Verdict ?? string.Empty, ct);
                if (answer is null) return Results.NotFound();
                // The audit event carries the id and the verdict only, never what the memory said.
                await EndpointHelpers.TryAppendAuditAsync(audit, logger, user.OwnerId, "memory", "memory.reviewed",
                    "low", true, null, JsonSerializer.Serialize(new { memoryId, verdict = answer.Verdict }), ct);
                return Results.Ok(answer);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("verdict", exception.Message);
            }
        }).WithName("AnswerTrainingCard");

        group.MapGet("/map", async (IKnowledgeMapService map, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await map.MapAsync(user.OwnerId, ct))).WithName("GetKnowledgeMap");

        group.MapPost("/map/answer", async (GapAnswerBody body, IKnowledgeMapService map, ICurrentUser user,
            CancellationToken ct) =>
        {
            if (body.EntityId is not { } entityId || string.IsNullOrWhiteSpace(body.Predicate))
                return EndpointHelpers.Invalid("entityId", "Say which question this answers.");
            try
            {
                var result = await map.AnswerAsync(user.OwnerId, entityId, body.Predicate.Trim(),
                    body.Answer ?? string.Empty, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("answer", exception.Message);
            }
        }).WithName("AnswerKnowledgeGap");

        group.MapPost("/map/skip", async (GapAnswerBody body, IKnowledgeMapService map, ICurrentUser user,
            CancellationToken ct) =>
        {
            if (body.EntityId is not { } entityId || string.IsNullOrWhiteSpace(body.Predicate))
                return EndpointHelpers.Invalid("entityId", "Say which question to skip.");
            return Results.Ok(await map.SkipAsync(user.OwnerId, entityId, body.Predicate.Trim(), ct));
        }).WithName("SkipKnowledgeGap");

        group.MapGet("/trust", async (ITrustLadderService trust, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await trust.LadderAsync(user.OwnerId, ct))).WithName("GetTrustLadder");

        group.MapPost("/trust/{category}/hand-over", async (string category, ITrustLadderService trust,
            ICurrentUser user, CancellationToken ct) =>
            await trust.HandOverAsync(user.OwnerId, category, ct) switch
            {
                TrustHandOverResult.HandedOver => Results.NoContent(),
                TrustHandOverResult.NotFound => Results.NotFound(),
                TrustHandOverResult.NotEarned => EndpointHelpers.Invalid("category",
                    "Jarvis has not earned this yet: approve it ten times in a row first."),
                _ => EndpointHelpers.Invalid("category", "This kind of action always asks first.")
            }).WithName("HandOverTrust");

        group.MapDelete("/trust/{category}", async (string category, ITrustLadderService trust, ICurrentUser user,
            CancellationToken ct) =>
            await trust.TakeBackAsync(user.OwnerId, category, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("TakeBackTrust");

        var modes = api.MapGroup("/chat-modes").WithTags("Knowing");

        modes.MapGet("", async (IChatModeService chatModes, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await chatModes.ListAsync(user.OwnerId, ct))).WithName("ListChatModes");

        modes.MapGet("/{conversationId:guid}", async (Guid conversationId, IChatModeService chatModes,
            ICurrentUser user, CancellationToken ct) =>
            await chatModes.GetAsync(conversationId, user.OwnerId, ct) is { } mode
                ? Results.Ok(new { mode.ConversationId, mode.Kind, mode.CreatedAt })
                : Results.NotFound()).WithName("GetChatMode");

        modes.MapPost("", async (ChatModeBody body, IChatModeService chatModes, ICurrentUser user,
            CancellationToken ct) =>
        {
            var started = await chatModes.StartAsync(user.OwnerId, new ChatModeRequest(body.Kind ?? string.Empty,
                body.PersonId, body.Scenario, body.Goal, body.AsOf, body.Question, body.Advisers), ct);
            return started.Value is { } value
                ? Results.Created($"/api/v1/conversations/{value.ConversationId}", value)
                : EndpointHelpers.Invalid(started.Field ?? "kind", started.Error ?? "Could not start the chat.");
        }).WithName("StartChatMode");

        return api;
    }
}
