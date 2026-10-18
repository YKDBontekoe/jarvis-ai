using Jarvis.Application.Attention;
using Jarvis.Application.Conversations;

namespace Jarvis.Api.Endpoints;

public sealed record AttentionSnoozeRequest(int? Days);

/// <summary>
/// The unified "Needs you" queue: approvals, paused and failed work, late promises, decisions due and promises to
/// review, in the order that matters. Putting an item away hides it for a while and never changes the record.
/// </summary>
internal static class AttentionEndpoints
{
    public static RouteGroupBuilder MapAttentionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/attention").WithTags("Attention");

        group.MapGet("", async (IAttentionService attention, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await attention.GetAsync(user.OwnerId, ct))).WithName("GetAttentionQueue");

        group.MapPost("/{id}/snooze", async (string id, AttentionSnoozeRequest request, IAttentionService attention,
            ICurrentUser user, CancellationToken ct) => Respond(await attention.SnoozeAsync(user.OwnerId, id, request.Days ?? 1, ct)))
            .WithName("SnoozeAttentionItem");

        group.MapDelete("/{id}/snooze", async (string id, IAttentionService attention, ICurrentUser user, CancellationToken ct) =>
            Respond(await attention.UnsnoozeAsync(user.OwnerId, id, ct))).WithName("UnsnoozeAttentionItem");

        return api;
    }

    private static IResult Respond(AttentionOperation<AttentionQueue> result) => result switch
    {
        { NotFound: true } => Results.NotFound(),
        { Succeeded: false } => EndpointHelpers.Invalid(result.Field ?? "request", result.Message ?? "The request is invalid."),
        _ => Results.Ok(result.Value)
    };
}
