using Jarvis.Application.Preferences;
using Jarvis.Application.Trips;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class TripTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fd11");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fd12");
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 18);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Time = Now;
        public override DateTimeOffset GetUtcNow() => Time;
    }

    private static TripInput Input(string title = "Lisbon", DateOnly? start = null, DateOnly? end = null,
        decimal? budget = 1500) =>
        new(title, "Lisbon", "Amsterdam", start ?? new DateOnly(2026, 11, 12), end ?? new DateOnly(2026, 11, 15), 2, true,
            budget, "EUR", "food and walking", null);

    private sealed class World
    {
        public readonly InMemorySettingsStore Settings = new();
        public readonly Clock Time = new();
        public TripService Service => new(Settings, null, Time);
    }

    private static OptionInput Flight(decimal? price = 240, DateOnly? quoted = null, DateOnly? start = null,
        DateOnly? end = null, string? url = "https://airline.example/offer/1", string availability = "available") =>
        new("transport", "TAP 08:15", price, "EUR", quoted ?? new DateOnly(2026, 10, 16), start ?? new DateOnly(2026, 11, 12),
            end ?? new DateOnly(2026, 11, 15), url, "Free cancellation until 5 Nov", new DateOnly(2026, 11, 5), availability, null);

    private static async Task<TripView> Created(World world) =>
        (await world.Service.CreateAsync(Owner, Input(), default)).Value!;

    [Fact]
    public async Task A_trip_needs_a_name_a_place_sane_dates_and_a_real_currency()
    {
        var service = new World().Service;

        Assert.Equal("title", (await service.CreateAsync(Owner, Input(title: " "), default)).Field);
        Assert.Equal("end", (await service.CreateAsync(Owner, Input(end: new DateOnly(2026, 11, 1)), default)).Field);
        Assert.Equal("end", (await service.CreateAsync(Owner, Input(end: new DateOnly(2027, 3, 1)), default)).Field);
        Assert.Equal("currency", (await service.CreateAsync(Owner, Input() with { Currency = "euros" }, default)).Field);
        Assert.True((await service.CreateAsync(Owner, Input(), default)).Succeeded);
    }

    [Fact]
    public async Task A_price_without_a_date_a_source_or_travel_dates_is_never_shown_as_current()
    {
        var world = new World();
        var trip = await Created(world);
        var service = world.Service;

        var noSource = await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(url: null), default);
        var noDates = await service.AddOptionAsync(Owner, trip.Trip.Id,
            Flight() with { ForStart = null, ForEnd = null, Title = "No dates" }, default);
        var noDate = await service.AddOptionAsync(Owner, trip.Trip.Id,
            Flight() with { QuotedOn = null, Title = "No quote date" }, default);

        Assert.All(noDate.Value!.Options, o => Assert.Equal(QuoteStates.Unquoted, o.Quote));
        Assert.Contains("not a current fact", noDate.Value.Options[1].QuoteNote);
        Assert.Equal(3, noDate.Value.Options.Count);
        Assert.NotNull(noSource.Value);
        Assert.NotNull(noDates.Value);
    }

    [Fact]
    public async Task A_recent_sourced_quote_for_these_dates_is_current_and_an_old_one_is_outdated_with_its_age()
    {
        var world = new World();
        var trip = await Created(world);
        var service = world.Service;

        var fresh = await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(), default);
        Assert.Equal(QuoteStates.Current, fresh.Value!.Options[0].Quote);
        Assert.Equal("EUR 240.00", fresh.Value.Options[0].PriceText);
        Assert.Contains("Quoted 16 Oct from its source", fresh.Value.Options[0].QuoteNote);

        world.Time.Time = Now.AddDays(10);
        var later = await service.GetAsync(Owner, trip.Trip.Id, default);
        Assert.Equal(QuoteStates.Outdated, later!.Options[0].Quote);
        Assert.Contains("Quoted 12 days ago", later.Options[0].QuoteNote);
    }

    [Fact]
    public async Task Options_are_validated_and_a_future_quote_or_a_non_web_source_is_refused()
    {
        var world = new World();
        var trip = await Created(world);
        var service = world.Service;

        Assert.Equal("kind", (await service.AddOptionAsync(Owner, trip.Trip.Id, Flight() with { Kind = "taxi" }, default)).Field);
        Assert.Equal("price", (await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(price: -5), default)).Field);
        Assert.Equal("currency", (await service.AddOptionAsync(Owner, trip.Trip.Id, Flight() with { Currency = "??" }, default)).Field);
        Assert.Equal("sourceUrl", (await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(url: "javascript:alert(1)"), default)).Field);
        Assert.Equal("quotedOn", (await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(quoted: new DateOnly(2026, 12, 1)), default)).Field);
        Assert.Equal("title", (await service.AddOptionAsync(Owner, trip.Trip.Id, Flight() with { Title = " " }, default)).Field);
    }

    [Fact]
    public async Task Choosing_picks_one_transport_and_one_stay_never_a_sold_out_option_and_books_nothing()
    {
        var world = new World();
        var trip = await Created(world);
        var service = world.Service;
        await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(), default);
        var second = await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(280) with { Title = "KLM" }, default);
        var soldOut = await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(availability: "sold_out") with { Title = "Gone" }, default);
        var ids = soldOut.Value!.Options.Select(o => o.Option.Id).ToArray();

        await service.ChooseOptionAsync(Owner, trip.Trip.Id, ids[0], true, default);
        var both = await service.ChooseOptionAsync(Owner, trip.Trip.Id, ids[1], true, default);
        var refused = await service.ChooseOptionAsync(Owner, trip.Trip.Id, ids[2], true, default);

        Assert.Equal(["KLM"], both.Value!.Options.Where(o => o.Option.Chosen).Select(o => o.Option.Title));
        Assert.Equal("option", refused.Field);
        Assert.Equal(280m, both.Value.Budget.Chosen);
        Assert.Equal(1220m, both.Value.Budget.Remaining);
        Assert.NotNull(second.Value);
    }

    [Fact]
    public async Task Other_currencies_are_listed_not_converted_and_unpriced_choices_are_counted()
    {
        var world = new World();
        var trip = await Created(world);
        var service = world.Service;
        var a = await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(), default);
        var b = await service.AddOptionAsync(Owner, trip.Trip.Id,
            new OptionInput("stay", "Hotel", 300, "GBP", new DateOnly(2026, 10, 16), new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 15),
                "https://hotel.example/1", null, null, "available", null), default);
        var c = await service.AddOptionAsync(Owner, trip.Trip.Id,
            new OptionInput("activity", "Tram tour", null, null, null, null, null, null, null, null, null, null), default);
        foreach (var option in c.Value!.Options) await service.ChooseOptionAsync(Owner, trip.Trip.Id, option.Option.Id, true, default);

        var view = (await service.GetAsync(Owner, trip.Trip.Id, default))!;

        Assert.Equal(240m, view.Budget.Chosen);
        Assert.Equal(["Hotel: GBP 300.00"], view.Budget.NotSummed);
        Assert.Equal(1, view.Budget.ChosenWithoutPrice);
        Assert.NotNull(a.Value);
        Assert.NotNull(b.Value);
    }

    private static ItemInput Item(string title, int fromHour, int fromMinute, int toHour, int toMinute, int travel = 0,
        int day = 13, bool verified = false) =>
        new(new DateOnly(2026, 11, day), new TimeOnly(fromHour, fromMinute), new TimeOnly(toHour, toMinute), title,
            "activity", "Alfama", travel, null, null, verified, null);

    [Fact]
    public async Task The_itinerary_flags_overlaps_short_buffers_and_closing_times_without_blocking_them()
    {
        var world = new World();
        var trip = await Created(world);
        var service = world.Service;
        await service.AddItemAsync(Owner, trip.Trip.Id, Item("Castle", 10, 0, 12, 0, verified: true), default);
        await service.AddItemAsync(Owner, trip.Trip.Id, Item("Museum", 11, 30, 13, 0), default);
        await service.AddItemAsync(Owner, trip.Trip.Id, Item("Lunch", 13, 10, 14, 0, travel: 20), default);
        var closing = await service.AddItemAsync(Owner, trip.Trip.Id,
            Item("Garden", 16, 0, 19, 0) with { ClosesAt = new TimeOnly(18, 0) }, default);

        var day = closing.Value!.Days.Single(d => d.Date == new DateOnly(2026, 11, 13));

        Assert.Equal(4, day.Items.Count);
        Assert.Contains(day.Items[1].Flags, f => f.Code == "overlap");
        Assert.Contains(day.Items[2].Flags, f => f.Code == "buffer");
        Assert.Contains(day.Items[3].Flags, f => f.Code == "opening" && f.Text.Contains("past closing at 18:00"));
        Assert.DoesNotContain(day.Items[0].Flags, f => f.Code == "unverified");
        Assert.Contains(day.Items[1].Flags, f => f.Code == "unverified");
    }

    [Fact]
    public async Task A_packed_day_is_told_to_leave_room_and_an_item_outside_the_trip_is_refused()
    {
        var world = new World();
        var trip = await Created(world);
        var service = world.Service;

        var packed = await service.AddItemAsync(Owner, trip.Trip.Id, Item("Everything", 9, 0, 20, 0), default);
        var outside = await service.AddItemAsync(Owner, trip.Trip.Id, Item("Next week", 10, 0, 11, 0, day: 25), default);
        var reversed = await service.AddItemAsync(Owner, trip.Trip.Id, Item("Backwards", 15, 0, 14, 0), default);

        var day = packed.Value!.Days.Single(d => d.Date == new DateOnly(2026, 11, 13));
        Assert.Equal(60, day.FreeMinutes);
        Assert.Contains(day.Flags, f => f.Code == "full");
        Assert.Equal("date", outside.Field);
        Assert.Equal("end", reversed.Field);
        Assert.Equal(720, packed.Value.Days.Single(d => d.Date == new DateOnly(2026, 11, 12)).FreeMinutes);
    }

    [Fact]
    public async Task The_checklist_follows_the_trips_facts_and_never_claims_a_visa_is_not_needed()
    {
        var world = new World();
        var trip = await Created(world);
        var service = world.Service;
        var abroad = trip.Checklist;
        var domestic = (await service.CreateAsync(Owner, Input("Weekend") with { Abroad = false, Travelers = 1 }, default)).Value!.Checklist;

        var documents = abroad.Single(e => e.Id == "documents");
        Assert.Contains("Check the official government source", documents.Note);
        Assert.Contains("not proof you may enter", documents.Note);
        Assert.Contains(abroad, e => e.Id == "travellers" && e.Text.Contains("all 2 travellers"));
        Assert.DoesNotContain(domestic, e => e.Id == "documents");
        Assert.DoesNotContain(domestic, e => e.Id == "travellers");
        Assert.Contains("3 nights", abroad.Single(e => e.Id == "clothes").Text);

        var toggled = await service.ToggleChecklistAsync(Owner, trip.Trip.Id, "tickets", true, default);
        Assert.True(toggled.Value!.Checklist.Single(e => e.Id == "tickets").Done);
        Assert.Equal(TripFailure.NotFound, (await service.ToggleChecklistAsync(Owner, trip.Trip.Id, "nope", true, default)).Failure);
    }

    [Fact]
    public async Task Holds_are_proposed_for_departures_check_in_and_cancellation_deadlines_and_none_are_created()
    {
        var world = new World();
        var trip = await Created(world);
        var service = world.Service;
        var option = await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(), default);
        await service.ChooseOptionAsync(Owner, trip.Trip.Id, option.Value!.Options[0].Option.Id, true, default);
        var view = await service.AddItemAsync(Owner, trip.Trip.Id,
            new ItemInput(new DateOnly(2026, 11, 12), new TimeOnly(8, 15), new TimeOnly(11, 0), "Flight to Lisbon", "travel", "AMS", 90, null, null, true, null), default);

        Assert.Contains(view.Value!.Holds, h => h.Why == "Departure" && h.At == new TimeOnly(8, 15));
        Assert.Contains(view.Value.Holds, h => h.Why == "Free cancellation ends the next day" && h.Date == new DateOnly(2026, 11, 4));
    }

    [Fact]
    public async Task Changing_dates_shifts_the_days_unverifies_the_times_and_sends_old_quotes_back_for_a_recheck()
    {
        var world = new World();
        var trip = await Created(world);
        var service = world.Service;
        await service.AddOptionAsync(Owner, trip.Trip.Id, Flight(), default);
        await service.AddItemAsync(Owner, trip.Trip.Id, Item("Castle", 10, 0, 12, 0, verified: true), default);
        await service.AddItemAsync(Owner, trip.Trip.Id, Item("Departure lunch", 12, 0, 13, 0, day: 15), default);

        var preview = await service.PreviewDatesAsync(Owner, trip.Trip.Id, new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 21), default);
        var unchanged = (await service.GetAsync(Owner, trip.Trip.Id, default))!;
        var applied = await service.ApplyDatesAsync(Owner, trip.Trip.Id, new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 21), default);

        Assert.Equal(7, preview.Value!.DaysShifted);
        Assert.Equal(1, preview.Value.ItemsOutsideNewDates);
        Assert.Contains("TAP 08:15", preview.Value.OptionsToRecheck);
        Assert.Contains(preview.Value.Notes, n => n.Contains("3 days instead of 4"));
        Assert.Equal(new DateOnly(2026, 11, 12), unchanged.Trip.Start);
        var after = applied.Value!;
        Assert.Equal(new DateOnly(2026, 11, 19), after.Trip.Start);
        Assert.Equal(new DateOnly(2026, 11, 20), after.Trip.Items.Single(i => i.Title == "Castle").Date);
        Assert.All(after.Trip.Items, i => Assert.False(i.Verified));
        Assert.Equal(QuoteStates.OtherDates, after.Options[0].Quote);
        Assert.Contains("not for these dates", after.Options[0].QuoteNote);
        Assert.Equal("end", (await service.ApplyDatesAsync(Owner, trip.Trip.Id, new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 1), default)).Field);
    }

    [Fact]
    public async Task Unknowns_are_listed_plainly_and_trips_are_private_to_their_owner()
    {
        var world = new World();
        var service = world.Service;
        var trip = (await service.CreateAsync(Owner, Input(budget: null), default)).Value!;

        var unknowns = trip.Unknowns;
        Assert.Contains("No budget set.", unknowns);
        Assert.Contains("No transport chosen yet.", unknowns);
        Assert.Contains("No stay chosen yet.", unknowns);
        Assert.Null(await service.GetAsync(Other, trip.Trip.Id, default));
        Assert.False(await service.DeleteAsync(Other, trip.Trip.Id, default));
        Assert.Equal(TripFailure.NotFound, (await service.AddOptionAsync(Other, trip.Trip.Id, Flight(), default)).Failure);
        Assert.Empty(await service.ListAsync(Other, default));
        Assert.Single(await service.ListAsync(Owner, default));
    }

    [Fact]
    public async Task The_agent_tools_refuse_bad_dates_and_never_choose_for_the_user()
    {
        var world = new World();
        var tools = new Jarvis.Agents.Trips.TripAgentTools(world.Service, new FixedUser());

        Assert.Equal("Dates must be yyyy-MM-dd.", await tools.CreateTripAsync("x", "y", "soon", "later"));
        var created = await tools.CreateTripAsync("Lisbon", "Lisbon", "2026-11-12", "2026-11-15", 2, 1500, "EUR", true);
        Assert.Contains("Nothing is booked", created);
        var id = (await world.Service.ListAsync(Owner, default)).Single().Id.ToString();

        var added = await tools.AddTripOptionAsync(id, "transport", "TAP", 240, "EUR", "2026-10-16", "https://a.example/1", "2026-11-12", "2026-11-15");
        var text = await tools.GetTripAsync(id);

        Assert.Contains("do not choose for them", added);
        Assert.Contains("Quoted 16 Oct from its source", text);
        Assert.Contains("Unknown: No stay chosen yet.", text);
        Assert.DoesNotContain("[chosen]", text);
    }

    private sealed class FixedUser : Jarvis.Application.Conversations.ICurrentUser
    {
        public Guid OwnerId => Owner;
    }
}
