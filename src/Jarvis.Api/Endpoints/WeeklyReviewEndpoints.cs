using Jarvis.Api.Errors;
using Jarvis.Application.Conversations;
using Jarvis.Application.Reviews;

namespace Jarvis.Api.Endpoints;

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

        reviews.MapPost("/generate", async (IWeeklyReviewService service, ICurrentUser currentUser,
                CancellationToken ct) =>
                Results.Ok(await service.GenerateNowAsync(currentUser.OwnerId, ct)))
            .WithName("GenerateWeeklyReview");

        return api;
    }
}
