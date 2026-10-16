using Jarvis.Application.Automations;
using Jarvis.Application.Home;
using Jarvis.Application.People.Radar;
using Jarvis.Application.Routines;
using Jarvis.Application.WhatsApp;
using Jarvis.Domain.People;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class HomeContextRulesTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // Friday 16 October 2026, 09:14 in Amsterdam.
    private static readonly DateTimeOffset Morning = new(2026, 10, 16, 7, 14, 0, TimeSpan.Zero);

    private static HomeContextFacts Facts(DateTimeOffset now, IReadOnlyList<HomeAgendaFact>? agenda = null,
        HomeBriefingNoteDto? brief = null, int? battery = null, bool? charging = null) =>
        new(now, Amsterdam, agenda ?? [], brief, battery, charging, battery is null ? null : now.AddMinutes(-5));

    private static HomeAgendaFact At(string title, DateTimeOffset at, string kind = "reminder") =>
        new(title, at, kind, kind == "reminder" ? Guid.CreateVersion7() : null);

    [Fact]
    public void Something_within_the_hour_leads_with_a_countdown()
    {
        var meeting = At("Joëlle on campus", Morning.AddMinutes(46));

        var hero = HeroSelector.Pick(Facts(Morning, [meeting, At("Rosa", Morning.AddHours(8))]))!;

        Assert.Equal(HomeHeroKinds.Soon, hero.Kind);
        Assert.Equal("Joëlle on campus", hero.Title);
        Assert.Equal(meeting.At, hero.StartsAt);
        Assert.Equal("It starts in 46 min", hero.Reason);
        Assert.Equal(meeting.Id, hero.ReminderId);
        Assert.Contains("Reminder at 10:00", hero.Facts);
    }

    [Fact]
    public void An_unread_brief_leads_the_morning_and_not_later()
    {
        var brief = new HomeBriefingNoteDto(Guid.CreateVersion7(), "Your morning briefing",
            "**A calm Friday.** Two plans.\n- Joëlle at 10", Morning.AddHours(-1), false);

        var hero = HeroSelector.Pick(Facts(Morning, brief: brief))!;
        Assert.Equal(HomeHeroKinds.Brief, hero.Kind);
        Assert.Equal("A calm Friday. Two plans.", hero.Detail);

        Assert.Null(HeroSelector.Pick(Facts(Morning.AddHours(4), brief: brief)));
        Assert.Null(HeroSelector.Pick(Facts(Morning, brief: brief with { Read = true })));
    }

    [Fact]
    public void A_low_battery_beats_the_brief_but_not_a_plan_starting_now()
    {
        var hero = HeroSelector.Pick(Facts(Morning, [At("Rosa", Morning.AddHours(8))], battery: 14))!;
        Assert.Equal(HomeHeroKinds.Battery, hero.Kind);
        Assert.Equal("14% left and 1 more plan today", hero.Detail);

        Assert.Null(HeroSelector.Pick(Facts(Morning, battery: 14, charging: true)));
        Assert.Equal(HomeHeroKinds.Soon,
            HeroSelector.Pick(Facts(Morning, [At("Now", Morning.AddMinutes(10))], battery: 14))!.Kind);
    }

    [Fact]
    public void Late_evening_looks_ahead_to_tomorrow()
    {
        var evening = new DateTimeOffset(2026, 10, 16, 20, 30, 0, TimeSpan.Zero); // 22:30 local
        var birthday = At("Quinty's birthday", new DateTimeOffset(2026, 10, 17, 7, 0, 0, TimeSpan.Zero));
        var party = At("Mannen middag", new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero), "event");

        var hero = HeroSelector.Pick(Facts(evening, [party, birthday]))!;

        Assert.Equal(HomeHeroKinds.WindDown, hero.Kind);
        Assert.Equal("Tomorrow: Quinty's birthday", hero.Title);
        Assert.Equal("Then Mannen middag", hero.Detail);
        Assert.Equal(["Quinty's birthday", "Mannen middag"],
            HeroSelector.Tomorrow(Facts(evening, [party, birthday])).Select(item => item.Title));
    }

    [Fact]
    public void A_dismissed_card_steps_aside_for_the_next_one()
    {
        var facts = Facts(Morning, [At("Joëlle", Morning.AddMinutes(30))], battery: 12);
        var soon = HeroSelector.Pick(facts)!;
        var dismissed = new HomeContextDismissals().With(soon.Id, soon.Kind, mute: false, Morning);

        Assert.Equal(HomeHeroKinds.Battery, HeroSelector.Pick(facts, dismissed)!.Kind);

        var muted = dismissed.With("battery:x", HomeHeroKinds.Battery, mute: true, Morning);
        Assert.Null(HeroSelector.Pick(facts, muted));
        // A week later the dismissed card may come back.
        Assert.False(dismissed.Hides(soon.Id, soon.Kind, Morning.AddDays(8)));
    }

    [Theory]
    [InlineData(0, "Nothing on your plate today. A good day to get ahead.")]
    [InlineData(1, "Just one thing today: Joëlle at 10:00.")]
    [InlineData(2, "Two things today, from 10:00. You are free between 10:00 and 17:00.")]
    public void The_day_line_says_what_the_day_holds(int count, string expected)
    {
        var agenda = new[] { At("Joëlle", Morning.AddMinutes(46)), At("Rosa", Morning.AddHours(7).AddMinutes(46)) };

        Assert.Equal(expected, DayLine.Compose(Facts(Morning, agenda.Take(count).ToArray())));
    }

    [Fact]
    public void The_evening_day_line_points_at_tomorrow()
    {
        var evening = new DateTimeOffset(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);
        var tomorrow = At("Mannen middag", new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("A quiet evening. Tomorrow starts with Mannen middag at 14:00.",
            DayLine.Compose(Facts(evening, [tomorrow])));
    }

    [Fact]
    public void Insights_come_from_routines_quiet_people_and_birthdays()
    {
        var owner = Guid.CreateVersion7();
        var routine = new RoutineSuggestionView(Guid.CreateVersion7(), "Leave for Dordrecht on Fridays",
            "Seen 4 of your last 5 Fridays", .8, new AutomationSimulation("schedule", true, null, [], [], 0, 0), Morning);
        var weak = routine with { Id = Guid.CreateVersion7(), Confidence = .2 };
        var danielle = Person(owner, "Danielle", "Sister");
        var quinty = Person(owner, "Quinty", null) with { BirthdayMonth = 10, BirthdayDay = 17, BirthYear = 2001 };
        var people = new[]
        {
            new HomePersonDto(danielle.Id, "Danielle", "Sister", Guid.CreateVersion7(), "+316", null, null, true, 1.5,
                Morning.AddDays(-21), null)
        };

        var insights = InsightRules.Build([routine, weak], people, [danielle, quinty], Morning, Amsterdam);

        Assert.Equal([HomeInsightKinds.Routine, HomeInsightKinds.Drift, HomeInsightKinds.Birthday],
            insights.Select(item => item.Kind));
        Assert.Equal("Set it up", insights[0].ActionLabel);
        Assert.Equal("3 weeks since you talked to Danielle", insights[1].Title);
        Assert.Equal("You usually talk weekly", insights[1].Evidence);
        Assert.Equal("Quinty turns 25 tomorrow", insights[2].Title);

        var dismissed = new HomeContextDismissals().With(insights[1].Id, HomeInsightKinds.Drift, false, Morning);
        Assert.DoesNotContain(InsightRules.Build([routine], people, [danielle], Morning, Amsterdam, dismissed),
            item => item.Kind == HomeInsightKinds.Drift);
    }

    [Theory]
    [InlineData(10, 16, 0)]
    [InlineData(10, 17, 1)]
    [InlineData(10, 15, 364)]
    [InlineData(2, 29, 135)] // 2027 has no 29 February: the 28th
    public void Days_until_birthday_wrap_around_the_year(int month, int day, int expected) =>
        Assert.Equal(expected, InsightRules.DaysUntilBirthday(month, day, new DateTime(2026, 10, 16)));

    [Fact]
    public void People_rows_join_links_with_the_radar()
    {
        var owner = Guid.CreateVersion7();
        var rosa = Person(owner, "rosa", "Partner");
        var connection = Guid.CreateVersion7();
        var link = new PersonChannelLink(Guid.CreateVersion7(), owner, rosa.Id, connection, "+31633", null, null, null,
            Morning);
        var report = new RadarReport(rosa.Id, "rosa", Morning.AddHours(-14), 40, 42, 20, 22, 18, 20, .5, .5, 1, 14,
            [], [], null, null);

        var row = HomePeople.Build([rosa], [link], [report]).Single();

        Assert.Equal("Partner", row.Relationship);
        Assert.Equal(connection, row.ConnectionId);
        Assert.Equal(20, row.UsualReplyMinutes);
        Assert.Equal(14, row.WaitingHours);
        Assert.False(row.Quiet);
    }

    [Theory]
    [InlineData("[\"Ik ook van jou\", \"Tot vanavond\", \"Hoe laat?\", \"Extra\"]", 3)]
    [InlineData("Here you go:\n```json\n[\"Yes!\", \"yes!\"]\n```", 1)]
    [InlineData("not json", 0)]
    [InlineData("[1, \"ok\"]", 1)]
    public void Quick_replies_parse_a_short_list(string text, int count) =>
        Assert.Equal(count, WhatsAppQuickReplies.Parse(text).Count);

    [Fact]
    public void Quick_replies_drop_long_or_multiline_answers()
    {
        var replies = WhatsAppQuickReplies.Parse($"[\"{new string('a', 80)}\", \"two\\nlines\", \"fine\"]");

        Assert.Equal(["fine"], replies);
    }

    [Fact]
    public void The_quick_reply_cache_keeps_the_newest_entries()
    {
        var cache = new WhatsAppQuickReplyCache();
        var owner = Guid.CreateVersion7();
        var connection = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        cache.Set(owner, connection, "chat", first, ["hi"]);
        Assert.True(cache.TryGet(owner, connection, "chat", first, out var replies));
        Assert.Equal(["hi"], replies);
        Assert.False(cache.TryGet(Guid.CreateVersion7(), connection, "chat", first, out _));

        for (var i = 0; i < WhatsAppQuickReplyCache.Capacity; i++)
            cache.Set(owner, connection, "chat", Guid.CreateVersion7(), ["x"]);
        Assert.False(cache.TryGet(owner, connection, "chat", first, out _));
    }

    private static Person Person(Guid owner, string name, string? relationship) =>
        new(Guid.CreateVersion7(), owner, name, relationship, null, null, null, null, null, null, null, null, null,
            Morning, Morning);
}
