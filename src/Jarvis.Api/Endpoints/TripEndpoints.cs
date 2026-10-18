using Jarvis.Application.Conversations;
using Jarvis.Application.Trips;

namespace Jarvis.Api.Endpoints;

public sealed record TripChoiceRequest(bool? Chosen);

public sealed record TripChecklistRequest(bool? Done);

public sealed record TripDatesRequest(DateOnly? Start, DateOnly? End);

/// <summary>
/// Trips: dates, options with dated and sourced quotes, an itinerary with honest checks, a checklist from the
/// trip's facts and proposed holds. Nothing is booked, bought or written to a calendar by any of it.
/// </summary>
internal static class TripEndpoints
{
    public static RouteGroupBuilder MapTripEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/trips").WithTags("Trips");

        group.MapGet("", async (ITripService trips, ICurrentUser user, CancellationToken ct) =>
            Results.Ok(await trips.ListAsync(user.OwnerId, ct))).WithName("ListTrips");

        group.MapPost("", async (TripInput input, ITripService trips, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await trips.CreateAsync(user.OwnerId, input, ct);
            return result.Succeeded ? Results.Created($"/api/v1/trips/{result.Value!.Trip.Id}", result.Value) : Failure(result);
        }).WithName("CreateTrip");

        group.MapGet("/{id:guid}", async (Guid id, ITripService trips, ICurrentUser user, CancellationToken ct) =>
            await trips.GetAsync(user.OwnerId, id, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .WithName("GetTrip");

        group.MapPut("/{id:guid}", async (Guid id, TripInput input, ITripService trips, ICurrentUser user,
            CancellationToken ct) => Done(await trips.UpdateAsync(user.OwnerId, id, input, ct))).WithName("UpdateTrip");

        group.MapDelete("/{id:guid}", async (Guid id, ITripService trips, ICurrentUser user, CancellationToken ct) =>
            await trips.DeleteAsync(user.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound()).WithName("DeleteTrip");

        group.MapPost("/{id:guid}/options", async (Guid id, OptionInput input, ITripService trips, ICurrentUser user,
            CancellationToken ct) => Done(await trips.AddOptionAsync(user.OwnerId, id, input, ct))).WithName("AddTripOption");

        group.MapDelete("/{id:guid}/options/{optionId}", async (Guid id, string optionId, ITripService trips,
            ICurrentUser user, CancellationToken ct) =>
            Done(await trips.RemoveOptionAsync(user.OwnerId, id, optionId, ct))).WithName("RemoveTripOption");

        group.MapPut("/{id:guid}/options/{optionId}/chosen", async (Guid id, string optionId, TripChoiceRequest request,
            ITripService trips, ICurrentUser user, CancellationToken ct) =>
            Done(await trips.ChooseOptionAsync(user.OwnerId, id, optionId, request.Chosen ?? true, ct)))
            .WithName("ChooseTripOption");

        group.MapPost("/{id:guid}/items", async (Guid id, ItemInput input, ITripService trips, ICurrentUser user,
            CancellationToken ct) => Done(await trips.AddItemAsync(user.OwnerId, id, input, ct))).WithName("AddTripItem");

        group.MapDelete("/{id:guid}/items/{itemId}", async (Guid id, string itemId, ITripService trips,
            ICurrentUser user, CancellationToken ct) =>
            Done(await trips.RemoveItemAsync(user.OwnerId, id, itemId, ct))).WithName("RemoveTripItem");

        group.MapPut("/{id:guid}/checklist/{entryId}", async (Guid id, string entryId, TripChecklistRequest request,
            ITripService trips, ICurrentUser user, CancellationToken ct) =>
            Done(await trips.ToggleChecklistAsync(user.OwnerId, id, entryId, request.Done ?? true, ct)))
            .WithName("ToggleTripChecklist");

        // Replanning shows the difference first; nothing changes until it is applied.
        group.MapPost("/{id:guid}/dates/preview", async (Guid id, TripDatesRequest request, ITripService trips,
            ICurrentUser user, CancellationToken ct) =>
        {
            var result = await trips.PreviewDatesAsync(user.OwnerId, id, request.Start, request.End, ct);
            return result.Succeeded ? Results.Ok(result.Value) : Failure(result);
        }).WithName("PreviewTripDates");

        group.MapPut("/{id:guid}/dates", async (Guid id, TripDatesRequest request, ITripService trips,
            ICurrentUser user, CancellationToken ct) =>
            Done(await trips.ApplyDatesAsync(user.OwnerId, id, request.Start, request.End, ct))).WithName("ApplyTripDates");

        return api;
    }

    private static IResult Done(TripOperation<TripView> result) => result.Succeeded ? Results.Ok(result.Value) : Failure(result);

    private static IResult Failure<T>(TripOperation<T> result) => result.Failure switch
    {
        TripFailure.NotFound => Results.NotFound(),
        _ => EndpointHelpers.Invalid(result.Field ?? "trip", result.Message ?? "The request is invalid.")
    };
}
