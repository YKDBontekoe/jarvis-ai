using Jarvis.Application.Settings;

namespace Jarvis.Application.Trips;

public sealed record TripSummary(Guid Id, string Title, string Destination, DateOnly Start, DateOnly End, int Unknowns);

public interface ITripService
{
    Task<IReadOnlyList<TripSummary>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<TripView?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<TripOperation<TripView>> CreateAsync(Guid ownerId, TripInput input, CancellationToken cancellationToken);
    Task<TripOperation<TripView>> UpdateAsync(Guid ownerId, Guid id, TripInput input, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<TripOperation<TripView>> AddOptionAsync(Guid ownerId, Guid id, OptionInput input, CancellationToken cancellationToken);
    Task<TripOperation<TripView>> RemoveOptionAsync(Guid ownerId, Guid id, string optionId, CancellationToken cancellationToken);

    /// <summary>Choosing is the owner's decision inside the trip. It books nothing. One chosen option per transport/stay.</summary>
    Task<TripOperation<TripView>> ChooseOptionAsync(Guid ownerId, Guid id, string optionId, bool chosen,
        CancellationToken cancellationToken);

    Task<TripOperation<TripView>> AddItemAsync(Guid ownerId, Guid id, ItemInput input, CancellationToken cancellationToken);
    Task<TripOperation<TripView>> RemoveItemAsync(Guid ownerId, Guid id, string itemId, CancellationToken cancellationToken);
    Task<TripOperation<TripView>> ToggleChecklistAsync(Guid ownerId, Guid id, string entryId, bool done,
        CancellationToken cancellationToken);

    /// <summary>What changing the dates would do. Changes nothing.</summary>
    Task<TripOperation<ReplanPreview>> PreviewDatesAsync(Guid ownerId, Guid id, DateOnly? start, DateOnly? end,
        CancellationToken cancellationToken);

    Task<TripOperation<TripView>> ApplyDatesAsync(Guid ownerId, Guid id, DateOnly? start, DateOnly? end,
        CancellationToken cancellationToken);
}

public sealed class TripService(IOwnerSettingsStore settings, Preferences.IOwnerPreferenceService? zone = null,
    TimeProvider? timeProvider = null) : ITripService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<TripSummary>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var today = await TodayAsync(ownerId, cancellationToken);
        return (await LoadAsync(ownerId, cancellationToken)).OrderBy(t => t.Start)
            .Select(t => new TripSummary(t.Id, t.Title, t.Destination, t.Start, t.End,
                TripPlanner.Unknowns(t, today).Count)).ToArray();
    }

    public async Task<TripView?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        (await LoadAsync(ownerId, cancellationToken)).FirstOrDefault(t => t.Id == id) is { } trip
            ? TripPlanner.Build(trip, await TodayAsync(ownerId, cancellationToken))
            : null;

    public async Task<TripOperation<TripView>> CreateAsync(Guid ownerId, TripInput input,
        CancellationToken cancellationToken)
    {
        var trips = (await LoadAsync(ownerId, cancellationToken)).ToList();
        if (trips.Count >= TripRules.MaxTrips)
            return TripOperation<TripView>.Invalid("trip", $"You can keep up to {TripRules.MaxTrips} trips. Remove one first.");
        var now = _clock.GetUtcNow();
        var blank = new Trip(Guid.NewGuid(), "", "", null, default, default, 1, false, null, "EUR", null, null, [], [], [], now, now);
        if (Apply(blank, input) is not { } built) return Validate(input);
        trips.Add(built);
        await SaveAsync(ownerId, trips, cancellationToken);
        return TripOperation<TripView>.Ok(TripPlanner.Build(built, await TodayAsync(ownerId, cancellationToken)));
    }

    public Task<TripOperation<TripView>> UpdateAsync(Guid ownerId, Guid id, TripInput input,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, trip => Apply(trip, input) is { } next
            ? TripOperation<Trip>.Ok(next with { Items = Within(next).ToArray() })
            : Validate<Trip>(input), cancellationToken);

    public async Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var trips = (await LoadAsync(ownerId, cancellationToken)).ToList();
        if (trips.RemoveAll(t => t.Id == id) == 0) return false;
        await SaveAsync(ownerId, trips, cancellationToken);
        return true;
    }

    public Task<TripOperation<TripView>> AddOptionAsync(Guid ownerId, Guid id, OptionInput input,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, trip =>
        {
            if (trip.Options.Count >= TripRules.MaxOptions)
                return TripOperation<Trip>.Invalid("option", $"A trip keeps up to {TripRules.MaxOptions} options.");
            var kind = input.Kind?.Trim().ToLowerInvariant();
            if (kind is null || !TripKinds.OptionKinds.Contains(kind))
                return TripOperation<Trip>.Invalid("kind", "Choose transport, stay or activity.");
            var title = TripRules.Clean(input.Title, 120);
            if (title is null) return TripOperation<Trip>.Invalid("title", "Give the option a name.");
            decimal? price = input.Price;
            string? currency = null;
            if (price is not null)
            {
                if (price < 0 || price > 10_000_000)
                    return TripOperation<Trip>.Invalid("price", "A price must be zero or more.");
                currency = TripRules.NormalizeCurrency(input.Currency ?? trip.Currency);
                if (currency is null) return TripOperation<Trip>.Invalid("currency", "Use a three-letter currency such as EUR.");
            }
            var availability = input.Availability?.Trim().ToLowerInvariant() ?? Availability.Unknown;
            if (!Availability.All.Contains(availability))
                return TripOperation<Trip>.Invalid("availability", "Availability is unknown, available or sold_out.");
            if (input.SourceUrl is not null && TripRules.CleanUrl(input.SourceUrl) is null)
                return TripOperation<Trip>.Invalid("sourceUrl", "A source must be a web address starting with http or https.");
            if (input.QuotedOn is { } quoted && quoted > DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddDays(1))
                return TripOperation<Trip>.Invalid("quotedOn", "A quote cannot be from the future.");
            var option = new TripOption(Guid.NewGuid().ToString("N")[..8], kind, title, price, currency, input.QuotedOn,
                input.ForStart, input.ForEnd, TripRules.CleanUrl(input.SourceUrl), TripRules.Clean(input.Cancellation),
                input.CancelBy, availability, TripRules.Clean(input.Notes), false);
            return TripOperation<Trip>.Ok(trip with { Options = [.. trip.Options, option] });
        }, cancellationToken);

    public Task<TripOperation<TripView>> RemoveOptionAsync(Guid ownerId, Guid id, string optionId,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, trip => trip.Options.Any(o => o.Id == optionId)
            ? TripOperation<Trip>.Ok(trip with { Options = trip.Options.Where(o => o.Id != optionId).ToArray() })
            : TripOperation<Trip>.NotFound(), cancellationToken);

    public Task<TripOperation<TripView>> ChooseOptionAsync(Guid ownerId, Guid id, string optionId, bool chosen,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, trip =>
        {
            var target = trip.Options.FirstOrDefault(o => o.Id == optionId);
            if (target is null) return TripOperation<Trip>.NotFound();
            if (chosen && target.Availability == Availability.SoldOut)
                return TripOperation<Trip>.Invalid("option", "That option is marked sold out.");
            var options = trip.Options.Select(o =>
                o.Id == optionId ? o with { Chosen = chosen }
                : chosen && target.Kind != TripKinds.Activity && o.Kind == target.Kind ? o with { Chosen = false }
                : o).ToArray();
            return TripOperation<Trip>.Ok(trip with { Options = options });
        }, cancellationToken);

    public Task<TripOperation<TripView>> AddItemAsync(Guid ownerId, Guid id, ItemInput input,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, trip =>
        {
            if (trip.Items.Count >= TripRules.MaxItems)
                return TripOperation<Trip>.Invalid("item", $"A trip keeps up to {TripRules.MaxItems} itinerary items.");
            if (input.Date is not { } date || date < trip.Start || date > trip.End)
                return TripOperation<Trip>.Invalid("date", "Pick a day inside the trip.");
            if (input.Start is not { } start || input.End is not { } end || end <= start)
                return TripOperation<Trip>.Invalid("end", "It must end after it starts.");
            var title = TripRules.Clean(input.Title, 120);
            if (title is null) return TripOperation<Trip>.Invalid("title", "Name what you will do.");
            var kind = input.Kind?.Trim().ToLowerInvariant() ?? TripKinds.Activity;
            if (!TripKinds.ItemKinds.Contains(kind))
                return TripOperation<Trip>.Invalid("kind", "Choose travel, stay, activity or meal.");
            var travel = Math.Clamp(input.TravelMinutesBefore ?? 0, 0, 600);
            var item = new TripItem(Guid.NewGuid().ToString("N")[..8], date, start, end, title, kind,
                TripRules.Clean(input.Place, 120), travel, input.OpensAt, input.ClosesAt, input.Verified == true,
                TripRules.CleanUrl(input.SourceUrl));
            return TripOperation<Trip>.Ok(trip with { Items = [.. trip.Items, item] });
        }, cancellationToken);

    public Task<TripOperation<TripView>> RemoveItemAsync(Guid ownerId, Guid id, string itemId,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, trip => trip.Items.Any(i => i.Id == itemId)
            ? TripOperation<Trip>.Ok(trip with { Items = trip.Items.Where(i => i.Id != itemId).ToArray() })
            : TripOperation<Trip>.NotFound(), cancellationToken);

    public Task<TripOperation<TripView>> ToggleChecklistAsync(Guid ownerId, Guid id, string entryId, bool done,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, trip =>
        {
            if (!TripPlanner.Checklist(trip).Any(e => e.Id == entryId)) return TripOperation<Trip>.NotFound();
            var set = trip.ChecklistDone.ToHashSet();
            if (done) set.Add(entryId); else set.Remove(entryId);
            return TripOperation<Trip>.Ok(trip with { ChecklistDone = set.Order().ToArray() });
        }, cancellationToken);

    public async Task<TripOperation<ReplanPreview>> PreviewDatesAsync(Guid ownerId, Guid id, DateOnly? start,
        DateOnly? end, CancellationToken cancellationToken)
    {
        var trip = (await LoadAsync(ownerId, cancellationToken)).FirstOrDefault(t => t.Id == id);
        if (trip is null) return TripOperation<ReplanPreview>.NotFound();
        return CheckDates(start, end) is { } bad
            ? TripOperation<ReplanPreview>.Invalid(bad.Field, bad.Message)
            : TripOperation<ReplanPreview>.Ok(TripPlanner.Preview(trip, start!.Value, end!.Value));
    }

    public Task<TripOperation<TripView>> ApplyDatesAsync(Guid ownerId, Guid id, DateOnly? start, DateOnly? end,
        CancellationToken cancellationToken) =>
        MutateAsync(ownerId, id, trip => CheckDates(start, end) is { } bad
            ? TripOperation<Trip>.Invalid(bad.Field, bad.Message)
            : TripOperation<Trip>.Ok(TripPlanner.Replan(trip, start!.Value, end!.Value, _clock.GetUtcNow())),
            cancellationToken);

    private static (string Field, string Message)? CheckDates(DateOnly? start, DateOnly? end)
    {
        if (start is null) return ("start", "Pick the first day.");
        if (end is null || end < start) return ("end", "The last day cannot be before the first.");
        if (end.Value.DayNumber - start.Value.DayNumber + 1 > TripRules.MaxDays)
            return ("end", $"A trip here is at most {TripRules.MaxDays} days.");
        return null;
    }

    private static IEnumerable<TripItem> Within(Trip trip) =>
        trip.Items.Where(i => i.Date >= trip.Start && i.Date <= trip.End);

    private Trip? Apply(Trip trip, TripInput input)
    {
        var title = TripRules.Clean(input.Title ?? (trip.Title.Length == 0 ? null : trip.Title), 120);
        var destination = TripRules.Clean(input.Destination ?? (trip.Destination.Length == 0 ? null : trip.Destination), 120);
        var start = input.Start ?? (trip.Start == default ? null : trip.Start);
        var end = input.End ?? (trip.End == default ? null : trip.End);
        if (title is null || destination is null || start is null || end is null || CheckDates(start, end) is not null)
            return null;
        var currency = TripRules.NormalizeCurrency(input.Currency ?? trip.Currency);
        var travelers = input.Travelers ?? trip.Travelers;
        if (currency is null || travelers is < 1 or > 20 || input.Budget is < 0) return null;
        return trip with
        {
            Title = title,
            Destination = destination,
            Origin = TripRules.Clean(input.Origin ?? trip.Origin, 120),
            Start = start.Value,
            End = end.Value,
            Travelers = travelers,
            Abroad = input.Abroad ?? trip.Abroad,
            Budget = input.Budget ?? trip.Budget,
            Currency = currency,
            Preferences = TripRules.Clean(input.Preferences ?? trip.Preferences),
            Constraints = TripRules.Clean(input.Constraints ?? trip.Constraints),
            UpdatedAt = _clock.GetUtcNow()
        };
    }

    private static TripOperation<TripView> Validate(TripInput input) => Validate<TripView>(input);

    private static TripOperation<T> Validate<T>(TripInput input)
    {
        if (TripRules.Clean(input.Title) is null) return TripOperation<T>.Invalid("title", "Give the trip a name.");
        if (TripRules.Clean(input.Destination) is null) return TripOperation<T>.Invalid("destination", "Where are you going?");
        if (CheckDates(input.Start, input.End) is { } bad) return TripOperation<T>.Invalid(bad.Field, bad.Message);
        if (TripRules.NormalizeCurrency(input.Currency ?? "EUR") is null)
            return TripOperation<T>.Invalid("currency", "Use a three-letter currency such as EUR.");
        if (input.Travelers is < 1 or > 20) return TripOperation<T>.Invalid("travelers", "Between 1 and 20 travellers.");
        return TripOperation<T>.Invalid("budget", "The budget must be zero or more.");
    }

    private async Task<TripOperation<TripView>> MutateAsync(Guid ownerId, Guid id,
        Func<Trip, TripOperation<Trip>> change, CancellationToken cancellationToken)
    {
        var trips = (await LoadAsync(ownerId, cancellationToken)).ToList();
        var index = trips.FindIndex(t => t.Id == id);
        if (index < 0) return TripOperation<TripView>.NotFound();
        var result = change(trips[index]);
        if (!result.Succeeded)
            return new TripOperation<TripView>(null, result.Failure, result.Field, result.Message);
        var updated = result.Value! with { UpdatedAt = _clock.GetUtcNow() };
        trips[index] = updated;
        await SaveAsync(ownerId, trips, cancellationToken);
        return TripOperation<TripView>.Ok(TripPlanner.Build(updated, await TodayAsync(ownerId, cancellationToken)));
    }

    private async Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var local = TimeZoneInfo.Utc;
        if (zone is not null)
        {
            var view = await zone.GetAsync(ownerId, cancellationToken);
            if (Workflows.LocalClock.TryFind(view.HomeTimeZone, out var found)) local = found;
        }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), local).DateTime);
    }

    private async Task<IReadOnlyList<Trip>> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await settings.GetAsync<TripState>(ownerId, TripState.Section, cancellationToken))?.Items ?? [];

    private Task SaveAsync(Guid ownerId, IReadOnlyList<Trip> trips, CancellationToken cancellationToken) =>
        settings.SaveAsync(ownerId, TripState.Section, new TripState(trips), cancellationToken);
}
