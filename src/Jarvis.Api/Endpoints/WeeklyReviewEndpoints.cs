using Jarvis.Api.Errors;
using Jarvis.Application.Conversations;
using Jarvis.Application.Reviews;

namespace Jarvis.Api.Endpoints;

public sealed record AcceptFollowThroughRequest(string? Kind, string? TargetId, DateOnly? On, string? Note,
    string? LinkType, Guid? LinkId);

public sealed record DismissFollowThroughRequest(string? Kind, string? TargetId);

internal static class WeeklyReviewEndpoints
{
    public static RouteGroupBuilder MapWeeklyReviewEndpoints(this RouteGroupBuilder api)
    {
        var reviews = api.MapGroup("/reviews/weekly");

        reviews.MapGet("", async (IWeeklyReviewService service, ICurrentUser currentUser, int? weeks,
                CancellationToken ct) =>
                Results.Ok(await service.GetOverviewAsync(currentUser.OwnerId, weeks ?? 8, ct)))
            .WithName("GetWeeklyReviews");

        reviews.MapGet("/{id:guid}", async (Guid id, IWeeklyReviewService service, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var review = await service.GetAsync(currentUser.OwnerId, id, ct);
            return review is null ? Results.NotFound() : Results.Ok(review);
        }).WithName("GetWeeklyReview");

        reviews.MapPut("/settings", async (WeeklyReviewSettings request, IWeeklyReviewService service,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.SaveSettingsAsync(currentUser.OwnerId, request, ct)); }
            catch (ArgumentException exception) { return EndpointHelpers.Invalid("timeZoneId", exception.Message); }
        }).WithName("SaveWeeklyReviewSettings");

        // What the week left behind, counted from the owner's own records. Read-only: nothing is created.
        reviews.MapGet("/follow-through", async (DateOnly? weekStart, IWeekFollowThroughService service,
                ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok(await service.GetAsync(currentUser.OwnerId, weekStart, ct)))
            .WithName("GetWeekFollowThrough");

        reviews.MapPost("/follow-through/accept", async (AcceptFollowThroughRequest request,
            IWeekFollowThroughService service, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var accepted = await service.AcceptAsync(currentUser.OwnerId, new FollowThroughAcceptance(
                request.Kind ?? string.Empty, request.TargetId ?? string.Empty, request.On, request.Note,
                request.LinkType, request.LinkId), ct);
            return accepted.Failure switch
            {
                FollowThroughFailure.None => Results.Ok(accepted.Value),
                FollowThroughFailure.NotFound => Results.NotFound(),
                _ => EndpointHelpers.Invalid(accepted.Field ?? "request", accepted.Message ?? "The request is invalid.")
            };
        }).WithName("AcceptFollowThrough");

        reviews.MapPost("/follow-through/dismiss", async (DismissFollowThroughRequest request,
                IWeekFollowThroughService service, ICurrentUser currentUser, CancellationToken ct) =>
                await service.DismissAsync(currentUser.OwnerId, request.Kind ?? string.Empty,
                    request.TargetId ?? string.Empty, ct)
                    ? Results.NoContent()
                    : EndpointHelpers.Invalid("kind", "Unknown suggestion."))
            .WithName("DismissFollowThrough");

        reviews.MapPost("/generate", async (IWeeklyReviewService service, ICurrentUser currentUser,
                CancellationToken ct) =>
                Results.Ok(await service.GenerateNowAsync(currentUser.OwnerId, ct)))
            .WithName("GenerateWeeklyReview");

        return api;
    }
}
