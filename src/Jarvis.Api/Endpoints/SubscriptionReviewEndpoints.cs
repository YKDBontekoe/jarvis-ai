using Jarvis.Application.Conversations;
using Jarvis.Application.Finance;

namespace Jarvis.Api.Endpoints;

public sealed record ReviewChoiceRequest(string? Choice);

public sealed record ReviewPackRequest(string? Goal, Guid? ProjectId);

public sealed record ReviewFollowUpRequest(DateOnly? ExpectedBy);

public sealed record ReviewOutcomeRequest(decimal? ConfirmedAmount, DateOnly? ConfirmedOn, string? Note);

public sealed record ReviewCancellationRequest(string? Evidence, DateOnly? EffectiveOn);

/// <summary>
/// The renewal review of one subscription: what is known, the paths the owner can take and what each leads to, a
/// negotiation or cancellation pack saved to the Library, the provider's reply tracked as a promise, and the
/// owner's confirmed result. Nothing is contacted, and nothing counts as cancelled or saved without the owner's
/// confirmation.
/// </summary>
internal static class SubscriptionReviewEndpoints
{
    public static RouteGroupBuilder MapSubscriptionReviewEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/finance/subscriptions/{id:guid}/review").WithTags("Subscription review");

        group.MapGet("", async (Guid id, ISubscriptionReviewService review, ICurrentUser user, CancellationToken ct) =>
            Respond(await review.GetAsync(user.OwnerId, id, ct))).WithName("GetSubscriptionReview");

        group.MapPost("/choice", async (Guid id, ReviewChoiceRequest request, ISubscriptionReviewService review,
            ICurrentUser user, CancellationToken ct) =>
            Respond(await review.ChooseAsync(user.OwnerId, id, request.Choice, ct)))
            .WithName("ChooseSubscriptionPath");

        group.MapPost("/comparison", async (Guid id, ISubscriptionReviewService review, ICurrentUser user,
            CancellationToken ct) => Respond(await review.StartComparisonAsync(user.OwnerId, id, ct),
                value => new { comparisonId = value })).WithName("StartSubscriptionComparison");

        group.MapPost("/pack", async (Guid id, ReviewPackRequest request, ISubscriptionReviewService review,
            ICurrentUser user, CancellationToken ct) =>
            Respond(await review.BuildPackAsync(user.OwnerId, id, request.Goal, request.ProjectId, ct)))
            .WithName("BuildSubscriptionPack");

        group.MapPost("/follow-up", async (Guid id, ReviewFollowUpRequest request, ISubscriptionReviewService review,
            ICurrentUser user, CancellationToken ct) =>
            Respond(await review.TrackResponseAsync(user.OwnerId, id, request.ExpectedBy, ct)))
            .WithName("TrackSubscriptionResponse");

        group.MapPost("/outcome", async (Guid id, ReviewOutcomeRequest request, ISubscriptionReviewService review,
            ICurrentUser user, CancellationToken ct) =>
            Respond(await review.RecordOutcomeAsync(user.OwnerId, id, request.ConfirmedAmount, request.ConfirmedOn,
                request.Note, ct))).WithName("RecordSubscriptionOutcome");

        group.MapPost("/cancellation", async (Guid id, ReviewCancellationRequest request,
            ISubscriptionReviewService review, ICurrentUser user, CancellationToken ct) =>
            Respond(await review.ConfirmCancellationAsync(user.OwnerId, id, request.Evidence, request.EffectiveOn, ct)))
            .WithName("ConfirmSubscriptionCancellation");

        return api;
    }

    private static IResult Respond<T>(FinanceOperation<T> result) => Respond(result, value => value!);

    private static IResult Respond<T>(FinanceOperation<T> result, Func<T, object> shape) => result.Failure switch
    {
        FinanceFailure.None => Results.Ok(shape(result.Value!)),
        FinanceFailure.NotFound => Results.NotFound(),
        _ => EndpointHelpers.Invalid(result.Field ?? "review", result.Message ?? "The request is invalid.")
    };
}
