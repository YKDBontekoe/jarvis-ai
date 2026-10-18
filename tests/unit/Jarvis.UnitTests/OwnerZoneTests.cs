using Jarvis.Application.Preferences;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class OwnerZoneTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fc01");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fc02");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 23, 30, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class World
    {
        public readonly InMemorySettingsStore Settings = new();
        public string? BriefZone;
        public bool BriefEnabled = true;
        public int Recurring;

        public IDailyBriefingRepository Briefings => Fake<IDailyBriefingRepository>.Create(("GetAsync", _ =>
            BriefZone is null ? null : new DailyBriefingPreferenceRecord(Owner, BriefEnabled, new TimeOnly(8, 0), BriefZone, "", null, null)));

        public OwnerPreferenceService Service => new(Settings, Briefings,
            Fake<IReminderRepository>.Create(("ListRemindersAsync", _ => (IReadOnlyList<ReminderRecord>)
                Enumerable.Range(0, Recurring).Select(_ => new ReminderRecord(Guid.NewGuid(), Owner, "r", Now, "w", "scheduled", Now, null, "daily")).ToArray())),
            new Clock());
    }

    [Fact]
    public async Task With_nothing_chosen_anywhere_utc_is_admitted_as_a_default_not_a_choice()
    {
        var view = await new World().Service.GetAsync(Owner, default);

        Assert.Equal("UTC", view.HomeTimeZone);
        Assert.Equal(ZoneSources.Default, view.Source);
        Assert.False(view.Chosen);
        Assert.Contains("not chosen", view.Explanation);
    }

    [Fact]
    public async Task A_briefing_zone_is_borrowed_and_labelled_as_such_while_a_utc_briefing_counts_as_nothing_chosen()
    {
        var borrowed = new World { BriefZone = "Europe/Amsterdam" };
        var utcBrief = new World { BriefZone = "UTC" };

        var view = await borrowed.Service.GetAsync(Owner, default);
        var fallback = await utcBrief.Service.GetAsync(Owner, default);

        Assert.Equal("Europe/Amsterdam", view.HomeTimeZone);
        Assert.Equal(ZoneSources.Brief, view.Source);
        Assert.False(view.Chosen);
        Assert.Contains("borrows", view.Explanation);
        Assert.Equal(ZoneSources.Default, fallback.Source);
    }

    [Fact]
    public async Task A_chosen_zone_wins_and_is_per_owner_and_clearing_it_goes_back_to_the_fallback()
    {
        var world = new World { BriefZone = "America/New_York" };
        var service = world.Service;

        var set = await service.SetHomeZoneAsync(Owner, "Europe/Amsterdam", default);

        Assert.True(set.Succeeded);
        Assert.Equal(ZoneSources.Chosen, set.View!.Source);
        Assert.True(set.View.Chosen);
        Assert.Null(set.View.Explanation);
        Assert.Equal(ZoneSources.Brief, (await service.GetAsync(Other, default)).Source);
        var cleared = await service.SetHomeZoneAsync(Owner, "  ", default);
        Assert.Equal(ZoneSources.Brief, cleared.View!.Source);
        Assert.Equal("America/New_York", cleared.View.HomeTimeZone);
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("../../etc/passwd")]
    [InlineData("Europe/Amsterdam; drop")]
    public async Task Unknown_zones_are_refused_and_nothing_is_stored(string id)
    {
        var world = new World();

        var result = await world.Service.SetHomeZoneAsync(Owner, id, default);

        Assert.False(result.Succeeded);
        Assert.Equal("timeZoneId", result.Field);
        Assert.Equal(ZoneSources.Default, (await world.Service.GetAsync(Owner, default)).Source);
    }

    [Fact]
    public void One_precedence_for_every_feature_action_then_schedule_then_home()
    {
        var home = new OwnerZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"), "Europe/Amsterdam", ZoneSources.Chosen);

        Assert.Equal(new ZoneChoice("Asia/Tokyo", ZoneSources.Schedule), ZonePolicy.Resolve("Asia/Tokyo", "America/New_York", home));
        Assert.Equal(new ZoneChoice("America/New_York", ZoneSources.Schedule), ZonePolicy.Resolve(null, "America/New_York", home));
        Assert.Equal(new ZoneChoice("Europe/Amsterdam", ZoneSources.Chosen), ZonePolicy.Resolve(null, null, home));
        Assert.Equal(new ZoneChoice("Europe/Amsterdam", ZoneSources.Chosen), ZonePolicy.Resolve("Not/AZone", "", home));
    }

    [Fact]
    public async Task The_preview_says_what_moves_and_what_does_not_without_changing_anything()
    {
        var world = new World { BriefZone = "America/New_York", Recurring = 3 };

        var preview = await world.Service.PreviewAsync(Owner, "Europe/Amsterdam", default);

        Assert.NotNull(preview);
        Assert.Equal("Europe/Amsterdam", preview.Proposed);
        Assert.Equal("America/New_York", preview.Current);
        Assert.Equal("Sun 18 Oct 01:30", preview.NowThere);
        Assert.Contains(preview.Effects, e => e.Area == "Morning brief" && e.Text.Contains("Stays at 08:00 in America/New_York"));
        Assert.Contains(preview.Effects, e => e.Area == "Repeating reminders" && e.Text.StartsWith("3 keep their own time zone"));
        Assert.Contains(preview.Effects, e => e.Text.Contains("Nothing already scheduled moves"));
        Assert.Equal(ZoneSources.Brief, (await world.Service.GetAsync(Owner, default)).Source);
        Assert.Null(await world.Service.PreviewAsync(Owner, "Nope/Nope", default));
    }

    [Fact]
    public void A_local_time_that_does_not_exist_moves_forward_and_a_repeated_one_uses_the_first_occurrence()
    {
        var amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

        // 2026-03-29 02:30 never happens in Amsterdam (clocks jump 02:00 to 03:00): it runs at 03:30 CEST.
        var skipped = LocalClock.Resolve(new DateTime(2026, 3, 29, 2, 30, 0), amsterdam);
        // 2026-10-25 02:30 happens twice; the first one (still CEST, +02:00) is used.
        var repeated = LocalClock.Resolve(new DateTime(2026, 10, 25, 2, 30, 0), amsterdam);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), skipped);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), repeated);
    }

    [Fact]
    public void Midnight_rolls_over_in_the_owners_zone_not_in_utc()
    {
        var amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        var there = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, amsterdam).DateTime);
        var utc = DateOnly.FromDateTime(Now.UtcDateTime);
        var behind = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, newYork).DateTime);

        Assert.Equal(new DateOnly(2026, 10, 18), there);
        Assert.Equal(new DateOnly(2026, 10, 17), utc);
        Assert.Equal(new DateOnly(2026, 10, 17), behind);
    }
}
