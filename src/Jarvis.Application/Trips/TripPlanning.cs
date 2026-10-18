namespace Jarvis.Application.Trips;

public static class QuoteStates
{
    public const string Current = "current";
    public const string Outdated = "outdated";
    public const string Unquoted = "unquoted";
    public const string OtherDates = "other_dates";
}

public sealed record OptionView(TripOption Option, string Quote, string QuoteNote, string? PriceText);

public sealed record ItemFlag(string Code, string Text);

public sealed record DayItemView(TripItem Item, IReadOnlyList<ItemFlag> Flags);

public sealed record DayView(DateOnly Date, IReadOnlyList<DayItemView> Items, int FreeMinutes, IReadOnlyList<ItemFlag> Flags);

public sealed record BudgetView(string Currency, decimal? Budget, decimal Chosen, decimal? Remaining,
    IReadOnlyList<string> NotSummed, int ChosenWithoutPrice, int ChosenOutdated);

public sealed record ChecklistEntry(string Id, string Group, string Text, string? Note, bool Done);

public sealed record Hold(string Title, DateOnly Date, TimeOnly? At, string Why);

public sealed record TripView(
    Trip Trip,
    IReadOnlyList<OptionView> Options,
    IReadOnlyList<DayView> Days,
    BudgetView Budget,
    IReadOnlyList<ChecklistEntry> Checklist,
    IReadOnlyList<Hold> Holds,
    IReadOnlyList<string> Unknowns);

public sealed record ReplanPreview(DateOnly NewStart, DateOnly NewEnd, int DaysShifted, int ItemsMoved,
    int ItemsOutsideNewDates, IReadOnlyList<string> OptionsToRecheck, IReadOnlyList<string> Notes);

/// <summary>
/// Everything computed from a trip's facts. Pure and deterministic: a price is shown as current only with a recent
/// quote for the trip's dates, an itinerary item is verified only when the owner says so, and nothing here books
/// or writes anywhere outside the trip.
/// </summary>
public static class TripPlanner
{
    public static TripView Build(Trip trip, DateOnly today) => new(
        trip,
        trip.Options.Select(o => OptionView(trip, o, today)).ToArray(),
        Days(trip),
        Budget(trip, today),
        Checklist(trip),
        Holds(trip),
        Unknowns(trip, today));

    public static OptionView OptionView(Trip trip, TripOption option, DateOnly today)
    {
        var state = QuoteState(trip, option, today);
        var note = state switch
        {
            QuoteStates.Unquoted => option.Price is null
                ? "No price yet."
                : "No quote date, source or travel dates, so this price is not a current fact. Check it.",
            QuoteStates.Outdated => $"Quoted {(today.DayNumber - option.QuotedOn!.Value.DayNumber)} days ago. Prices change: check it again.",
            QuoteStates.OtherDates => $"Quoted for {option.ForStart:d MMM}–{option.ForEnd:d MMM}, not for these dates. Check it again.",
            _ => $"Quoted {option.QuotedOn:d MMM}{(option.SourceUrl is null ? "" : " from its source")}."
        };
        var text = option.Price is { } price && option.Currency is { } currency ? TripRules.Money(price, currency) : null;
        return new OptionView(option, state, note, text);
    }

    public static string QuoteState(Trip trip, TripOption option, DateOnly today)
    {
        if (option.Price is null || option.QuotedOn is null || option.SourceUrl is null) return QuoteStates.Unquoted;
        if (option.Kind != TripKinds.Activity)
        {
            // A fare or a room is only a fact for the dates it was quoted for.
            if (option.ForStart is not { } start || option.ForEnd is not { } end) return QuoteStates.Unquoted;
            if (start != trip.Start || end != trip.End) return QuoteStates.OtherDates;
        }
        return today.DayNumber - option.QuotedOn.Value.DayNumber > TripRules.QuoteFreshDays
            ? QuoteStates.Outdated
            : QuoteStates.Current;
    }

    public static BudgetView Budget(Trip trip, DateOnly today)
    {
        decimal sum = 0;
        var other = new List<string>();
        var unpriced = 0;
        var outdated = 0;
        foreach (var option in trip.Options.Where(o => o.Chosen))
        {
            if (option.Price is not { } price || option.Currency is null) { unpriced++; continue; }
            if (QuoteState(trip, option, today) != QuoteStates.Current) outdated++;
            if (option.Currency == trip.Currency) sum += price;
            else other.Add($"{option.Title}: {TripRules.Money(price, option.Currency)}");
        }
        return new BudgetView(trip.Currency, trip.Budget, sum, trip.Budget is { } b ? b - sum : null, other, unpriced, outdated);
    }

    public static IReadOnlyList<DayView> Days(Trip trip)
    {
        var days = new List<DayView>();
        for (var date = trip.Start; date <= trip.End; date = date.AddDays(1))
        {
            var items = trip.Items.Where(i => i.Date == date).OrderBy(i => i.Start).ToArray();
            var flags = new List<ItemFlag>();
            var views = new List<DayItemView>();
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                var itemFlags = new List<ItemFlag>();
                if (item.End <= item.Start)
                    itemFlags.Add(new ItemFlag("order", "It ends before it starts."));
                if (i > 0)
                {
                    var previous = items[i - 1];
                    var gap = (int)(item.Start.ToTimeSpan() - previous.End.ToTimeSpan()).TotalMinutes;
                    if (gap < 0)
                        itemFlags.Add(new ItemFlag("overlap", $"Overlaps “{previous.Title}”."));
                    else if (gap < item.TravelMinutesBefore + TripRules.MinBufferMinutes)
                        itemFlags.Add(new ItemFlag("buffer",
                            $"Only {gap} min after “{previous.Title}”; getting there takes {item.TravelMinutesBefore} min plus a buffer."));
                }
                if (item.OpensAt is { } opens && item.Start < opens)
                    itemFlags.Add(new ItemFlag("opening", $"Starts before it opens at {opens:HH\\:mm} (as noted)."));
                if (item.ClosesAt is { } closes && item.End > closes)
                    itemFlags.Add(new ItemFlag("opening", $"Runs past closing at {closes:HH\\:mm} (as noted)."));
                if (!item.Verified)
                    itemFlags.Add(new ItemFlag("unverified", item.OpensAt is null && item.Kind == TripKinds.Activity
                        ? "Opening hours not checked."
                        : "Not verified against a source."));
                views.Add(new DayItemView(item, itemFlags));
            }

            var free = FreeMinutes(items);
            if (free < TripRules.MinFreeMinutesPerDay && items.Length > 0)
                flags.Add(new ItemFlag("full", $"Only {free} min of unplanned time. Leave room."));
            days.Add(new DayView(date, views, free, flags));
        }
        return days;
    }

    private static int FreeMinutes(IReadOnlyList<TripItem> items)
    {
        var start = TripRules.DayStarts.ToTimeSpan();
        var end = TripRules.DayEnds.ToTimeSpan();
        var busy = items
            .Select(i => (Start: i.Start.ToTimeSpan() - TimeSpan.FromMinutes(i.TravelMinutesBefore), End: i.End.ToTimeSpan()))
            .Select(range => (Start: range.Start < start ? start : range.Start, End: range.End > end ? end : range.End))
            .Where(range => range.End > range.Start)
            .OrderBy(range => range.Start).ToList();
        var total = (int)(end - start).TotalMinutes;
        var merged = TimeSpan.Zero;
        var cursor = start;
        foreach (var range in busy)
        {
            var from = range.Start < cursor ? cursor : range.Start;
            if (range.End > from) { merged += range.End - from; cursor = range.End; }
        }
        return Math.Max(0, total - (int)merged.TotalMinutes);
    }

    public static IReadOnlyList<ChecklistEntry> Checklist(Trip trip)
    {
        var nights = Math.Max(1, trip.End.DayNumber - trip.Start.DayNumber);
        var entries = new List<(string Id, string Group, string Text, string? Note)>
        {
            ("tickets", "Before you go", "Tickets and bookings saved where you can open them offline", null),
            ("money", "Before you go", "Payment cards and some cash for small places", null),
            ("phone", "Before you go", "Charger, power bank and any adapters", null),
            ("clothes", "Pack", $"Clothes for {nights} night{(nights == 1 ? "" : "s")} and the weather at {trip.Destination}",
                "Check the forecast closer to the day."),
            ("meds", "Pack", "Medication and anything you cannot easily buy there", null),
            ("home", "Before you leave home", "Water, bins, heating and post sorted", null)
        };
        if (trip.Abroad)
            entries.Insert(0, ("documents", "Documents",
                "Passport or ID, and any visa or entry paperwork",
                "Requirements change. Check the official government source for your nationality and destination; this list is not proof you may enter."));
        if (trip.Travelers > 1)
            entries.Add(("travellers", "Before you go", $"Names and documents of all {trip.Travelers} travellers match the bookings", null));
        if (trip.Options.Any(o => o.Chosen && o.Kind == TripKinds.Stay))
            entries.Add(("checkin", "Before you go", "Check-in time and how to get the key", null));
        foreach (var option in trip.Options.Where(o => o.Chosen && o.CancelBy is not null))
            entries.Add(($"cancel-{option.Id}", "Before you go", $"Decide on “{option.Title}” before the free cancellation ends on {option.CancelBy:d MMM}", null));
        return entries.Select(e => new ChecklistEntry(e.Id, e.Group, e.Text, e.Note, trip.ChecklistDone.Contains(e.Id))).ToArray();
    }

    /// <summary>Calendar holds and reminders worth proposing. Nothing is written to a calendar by this.</summary>
    public static IReadOnlyList<Hold> Holds(Trip trip)
    {
        var holds = new List<Hold>();
        foreach (var item in trip.Items.Where(i => i.Kind is TripKinds.Travel or TripKinds.Stay).OrderBy(i => i.Date).ThenBy(i => i.Start))
            holds.Add(new Hold(item.Title, item.Date, item.Start, item.Kind == TripKinds.Travel ? "Departure" : "Check-in"));
        foreach (var option in trip.Options.Where(o => o.Chosen && o.CancelBy is not null))
            holds.Add(new Hold($"Decide on {option.Title}", option.CancelBy!.Value.AddDays(-1), null, "Free cancellation ends the next day"));
        return holds.OrderBy(h => h.Date).ThenBy(h => h.At).ToArray();
    }

    public static IReadOnlyList<string> Unknowns(Trip trip, DateOnly today)
    {
        var unknowns = new List<string>();
        if (trip.Budget is null) unknowns.Add("No budget set.");
        foreach (var kind in TripKinds.OptionKinds.Where(k => k != TripKinds.Activity))
            if (!trip.Options.Any(o => o.Kind == kind && o.Chosen))
                unknowns.Add(kind == TripKinds.Transport ? "No transport chosen yet." : "No stay chosen yet.");
        foreach (var option in trip.Options.Where(o => o.Chosen))
        {
            var state = QuoteState(trip, option, today);
            if (state != QuoteStates.Current) unknowns.Add($"“{option.Title}”: {OptionView(trip, option, today).QuoteNote}");
            if (option.Availability != Availability.Available)
                unknowns.Add($"“{option.Title}”: availability is {(option.Availability == Availability.SoldOut ? "sold out" : "not confirmed")}.");
        }
        var unverified = trip.Items.Count(i => !i.Verified);
        if (unverified > 0) unknowns.Add($"{unverified} itinerary item{(unverified == 1 ? "" : "s")} not verified against a source.");
        return unknowns;
    }

    /// <summary>
    /// Shows what changing the dates would do: days shift together keeping their times, items that no longer fit
    /// are named, and quotes made for the old dates are marked for a recheck. Nothing is changed by previewing.
    /// </summary>
    public static ReplanPreview Preview(Trip trip, DateOnly newStart, DateOnly newEnd)
    {
        var shift = newStart.DayNumber - trip.Start.DayNumber;
        var moved = trip.Items.Count;
        var outside = trip.Items.Count(i => i.Date.AddDays(shift) > newEnd || i.Date.AddDays(shift) < newStart);
        var recheck = trip.Options.Where(o => o.Kind != TripKinds.Activity && o.Price is not null)
            .Select(o => o.Title).ToArray();
        var notes = new List<string>();
        if (shift != 0) notes.Add($"Every day moves {Math.Abs(shift)} day{(Math.Abs(shift) == 1 ? "" : "s")} {(shift > 0 ? "later" : "earlier")}; times of day stay the same.");
        if (newEnd.DayNumber - newStart.DayNumber != trip.End.DayNumber - trip.Start.DayNumber)
            notes.Add($"The trip is now {newEnd.DayNumber - newStart.DayNumber + 1} days instead of {trip.End.DayNumber - trip.Start.DayNumber + 1}.");
        if (recheck.Length > 0) notes.Add("Prices and availability were quoted for the old dates. They are marked to check again.");
        if (trip.Items.Any(i => i.Kind is TripKinds.Travel or TripKinds.Stay)) notes.Add("Any calendar holds or reminders you created for the old dates need updating.");
        return new ReplanPreview(newStart, newEnd, shift, moved, outside, recheck, notes);
    }

    public static Trip Replan(Trip trip, DateOnly newStart, DateOnly newEnd, DateTimeOffset now)
    {
        var shift = newStart.DayNumber - trip.Start.DayNumber;
        return trip with
        {
            Start = newStart,
            End = newEnd,
            Items = trip.Items.Select(i => i with { Date = i.Date.AddDays(shift), Verified = false }).ToArray(),
            // The old quote no longer matches these dates: keep it, but its ForStart/ForEnd now differ.
            UpdatedAt = now
        };
    }
}
