using Jarvis.Application.Conversations;
using Jarvis.Application.Outcomes;

namespace Jarvis.Api.Endpoints;

public sealed record OutcomeFeedbackRequest(string? ArtifactKind, string? ArtifactId, string? Signal, string? Aspect,
    string? Note, Guid? ProfileId);

/// <summary>
/// Owner feedback on what Jarvis made, and the small reviewable changes that repeated feedback supports. A change only
/// sets a style preference or quiets a kind of suggestion; it never touches tools, permissions or profile scope.
/// </summary>
internal static class OutcomeEndpoints
{
    public static RouteGroupBuilder MapOutcomeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/outcomes").WithTags("Outcomes");

        group.MapPost("/feedback", async (OutcomeFeedbackRequest request, IOutcomeLearningService outcomes,
            ICurrentUser user, CancellationToken ct) => Respond(await outcomes.RecordAsync(user.OwnerId,
            request.ArtifactKind, request.ArtifactId, request.Signal, request.Aspect, request.Note, request.ProfileId, ct)))
            .WithName("RecordOutcomeFeedback");

        group.MapGet("/changes", async (IOutcomeLearningService outcomes, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await outcomes.ChangesAsync(user.OwnerId, ct))).WithName("ListBehaviorChanges");

        group.MapGet("/learned", async (IOutcomeLearningService outcomes, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await outcomes.LearnedAsync(user.OwnerId, ct))).WithName("GetLearnedCard");

        group.MapPost("/changes/{id}/apply", async (string id, IOutcomeLearningService outcomes, ICurrentUser user,
            CancellationToken ct) => Respond(await outcomes.ApplyAsync(user.OwnerId, id, ct))).WithName("ApplyBehaviorChange");

        group.MapPost("/changes/{id}/reject", async (string id, IOutcomeLearningService outcomes, ICurrentUser user,
            CancellationToken ct) => Respond(await outcomes.RejectAsync(user.OwnerId, id, ct))).WithName("RejectBehaviorChange");

        group.MapPost("/changes/{id}/undo", async (string id, IOutcomeLearningService outcomes, ICurrentUser user,
            CancellationToken ct) => Respond(await outcomes.UndoAsync(user.OwnerId, id, ct))).WithName("UndoBehaviorChange");

        return api;
    }

    private static IResult Respond<T>(OutcomeOperation<T> result) => result switch
    {
        { NotFound: true } => Results.NotFound(),
        { Succeeded: false } => EndpointHelpers.Invalid(result.Field ?? "request", result.Message ?? "The request is invalid."),
        _ => Results.Ok(result.Value)
    };
}
