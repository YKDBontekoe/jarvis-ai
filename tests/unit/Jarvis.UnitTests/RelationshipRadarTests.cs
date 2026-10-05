using Jarvis.Agents.People;
using Jarvis.Application.People;
using Jarvis.Application.People.Radar;
using Jarvis.Application.Settings;
using Jarvis.Application.WhatsApp;
using Jarvis.Application.Workflows;
using Jarvis.Domain.People;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class RelationshipRadarTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-0000000000a1");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-0000000000a2");
    private static readonly Guid Connection = Guid.Parse("01996b8c-6000-7000-8000-0000000000c1");
    private static readonly Guid PersonId = Guid.Parse("01996b8c-6000-7000-8000-0000000000b1");
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private const string Phone = "+31612345678";

    // ---- helpers to build message history ----

    private enum Starter { Them, Me, Alternate }

    private static DateTimeOffset At(int daysAgo, int hour = 10, int minute = 0) =>
        new DateTimeOffset(Now.Date, TimeSpan.Zero).AddDays(-daysAgo).AddHours(hour).AddMinutes(minute);

    /// <summary>An exchange every <paramref name="step"/> days: one side writes, the other answers after <paramref name="reply"/>.</summary>
    private static List<RadarMessage> Exchanges(int fromDaysAgo, int toDaysAgo, int step, TimeSpan reply,
        Starter starter = Starter.Them)
    {
        var list = new List<RadarMessage>();
        var index = 0;
        for (var daysAgo = fromDaysAgo; daysAgo >= toDaysAgo; daysAgo -= step, index++)
        {
            var theyStart = starter switch
            {
                Starter.Them => true,
                Starter.Me => false,
                _ => index % 2 == 0
            };
            var first = At(daysAgo);
            list.Add(new RadarMessage(!theyStart, first));
            list.Add(new RadarMessage(theyStart, first + reply));
        }

        return list;
    }

    private static RadarReport Analyze(IEnumerable<RadarMessage> messages, int? tone = null, string? reason = null) =>
        RelationshipRadarEngine.Analyze(PersonId, "Anna", messages.ToArray(), Now, tone, reason);

    private static readonly TimeSpan Quick = TimeSpan.FromMinutes(10);

    private static List<RadarMessage> HeavyBaseline(Starter starter = Starter.Them) =>
        Exchanges(119, 31, 2, Quick, starter);

    // ---- engine ----

    [Fact]
    public void A_busy_chat_that_stops_completely_is_flagged_as_going_quiet()
    {
        var report = Analyze(HeavyBaseline());

        var signal = Assert.Single(report.Signals);
        Assert.Equal(RadarSignalKinds.Quiet, signal.Kind);
        Assert.Equal(2, signal.Severity);
        Assert.Equal("No messages in 30 days", signal.Headline);
        Assert.Equal(2, report.Severity);
        Assert.Equal(0, report.RecentPerWeek);
        Assert.True(report.BaselinePerWeek > 6);
    }

    [Fact]
    public void A_big_but_not_total_drop_is_a_note_not_a_nudge()
    {
        // 4 exchanges in the last 30 days (8 messages, about 1.9 a week) against about 7 a week before.
        var messages = HeavyBaseline();
        messages.AddRange(Exchanges(28, 7, 7, Quick));

        var signal = Assert.Single(Analyze(messages).Signals);

        Assert.Equal(RadarSignalKinds.Quiet, signal.Kind);
        Assert.Equal(1, signal.Severity);
        Assert.Equal("The conversation has gone quiet", signal.Headline);
    }

    [Fact]
    public void A_steady_chat_has_nothing_to_report()
    {
        var messages = HeavyBaseline();
        messages.AddRange(Exchanges(29, 1, 2, Quick));

        var report = Analyze(messages);

        Assert.Empty(report.Signals);
        Assert.Equal(0, report.Severity);
        Assert.InRange(report.RecentPerWeek, 6, 8);
    }

    [Fact]
    public void A_chat_that_was_never_busy_cannot_go_quiet()
    {
        // Ten messages before: far too little history to call silence a change.
        var report = Analyze(Exchanges(100, 60, 10, Quick));

        Assert.DoesNotContain(report.Signals, s => s.Kind == RadarSignalKinds.Quiet);
    }

    [Fact]
    public void Replying_much_slower_than_before_is_noticed()
    {
        var messages = HeavyBaseline();
        messages.AddRange(Exchanges(28, 7, 3, TimeSpan.FromHours(4)));

        var report = Analyze(messages);

        var signal = Assert.Single(report.Signals, s => s.Kind == RadarSignalKinds.ReplySlower);
        Assert.Equal(1, signal.Severity);
        Assert.Contains("10 min", signal.Detail);
        Assert.Contains("4 h", signal.Detail);
        Assert.Equal(240, report.MyReplyMinutes);
        Assert.Equal(10, report.BaselineReplyMinutes);
    }

    [Fact]
    public void Slower_replies_need_enough_replies_on_both_sides()
    {
        var messages = HeavyBaseline();
        messages.AddRange(Exchanges(20, 5, 5, TimeSpan.FromHours(5))); // only 4 recent replies

        Assert.DoesNotContain(Analyze(messages).Signals, s => s.Kind == RadarSignalKinds.ReplySlower);
    }

    [Fact]
    public void A_small_difference_in_reply_time_is_not_slower()
    {
        var baseline = Exchanges(119, 31, 2, TimeSpan.FromMinutes(5));
        baseline.AddRange(Exchanges(28, 7, 3, TimeSpan.FromMinutes(30))); // 6x slower but only 25 minutes

        Assert.DoesNotContain(Analyze(baseline).Signals, s => s.Kind == RadarSignalKinds.ReplySlower);
    }

    [Fact]
    public void Doing_nearly_all_the_reaching_out_is_noticed()
    {
        var messages = HeavyBaseline(Starter.Alternate);
        messages.AddRange(Exchanges(29, 1, 2, Quick, Starter.Me));

        var signal = Assert.Single(Analyze(messages).Signals);

        Assert.Equal(RadarSignalKinds.YouInitiate, signal.Kind);
        Assert.Contains("100%", signal.Detail);
        Assert.Contains("49%", signal.Detail); // 22 of the 45 earlier conversations
    }

    [Fact]
    public void Them_doing_all_the_reaching_out_is_noticed_the_other_way_round()
    {
        var messages = HeavyBaseline(Starter.Alternate);
        messages.AddRange(Exchanges(29, 1, 2, Quick, Starter.Them));

        var signal = Assert.Single(Analyze(messages).Signals);

        Assert.Equal(RadarSignalKinds.TheyInitiate, signal.Kind);
    }

    [Fact]
    public void A_chat_that_was_always_one_sided_is_not_a_change()
    {
        var messages = HeavyBaseline(Starter.Me);
        messages.AddRange(Exchanges(29, 1, 2, Quick, Starter.Me));

        Assert.Empty(Analyze(messages).Signals);
    }

    [Theory]
    [InlineData(1, false, 0)]
    [InlineData(3, true, 1)]
    [InlineData(6, true, 2)]
    public void An_unanswered_message_is_flagged_after_two_days_and_urgent_after_five(int daysAgo, bool flagged,
        int severity)
    {
        var report = Analyze([new RadarMessage(false, At(daysAgo))]);

        var signal = report.Signals.SingleOrDefault(s => s.Kind == RadarSignalKinds.Unanswered);
        Assert.Equal(flagged, signal is not null);
        if (signal is null) return;
        Assert.Equal(severity, signal.Severity);
        Assert.Equal(1, report.UnansweredInbound);
        Assert.Contains("you have not replied", signal.Headline);
    }

    [Fact]
    public void Several_unanswered_messages_count_from_the_first()
    {
        var report = Analyze([
            new RadarMessage(true, At(9)),
            new RadarMessage(false, At(4)),
            new RadarMessage(false, At(3)),
            new RadarMessage(false, At(2))
        ]);

        var signal = Assert.Single(report.Signals);
        Assert.Equal(3, report.UnansweredInbound);
        Assert.Contains("3 messages", signal.Headline);
        Assert.Contains("4 days ago", signal.Headline);
    }

    [Fact]
    public void A_message_you_answered_is_not_unanswered()
    {
        var report = Analyze([new RadarMessage(false, At(5)), new RadarMessage(true, At(4))]);

        Assert.Equal(0, report.UnansweredInbound);
        Assert.Empty(report.Signals);
    }

    [Fact]
    public void A_very_old_unanswered_message_is_a_conversation_that_ended()
    {
        var report = Analyze([new RadarMessage(false, At(60))]);

        Assert.DoesNotContain(report.Signals, s => s.Kind == RadarSignalKinds.Unanswered);
    }

    [Fact]
    public void Messages_from_the_future_are_ignored()
    {
        var report = Analyze([new RadarMessage(false, Now.AddHours(3))]);

        Assert.Null(report.LastMessageAt);
        Assert.Equal(0, report.UnansweredInbound);
    }

    [Fact]
    public void Reply_time_counts_from_the_first_message_of_a_run_and_skips_replies_after_two_weeks()
    {
        var messages = new[]
        {
            new RadarMessage(false, At(3, 9, 0)), new RadarMessage(false, At(3, 9, 30)),
            new RadarMessage(true, At(3, 11, 0)),          // answered 2 h after the first of their run
            new RadarMessage(false, At(40)), new RadarMessage(true, At(10)) // 30 days: a new conversation
        };

        var report = Analyze(messages);

        Assert.Equal(120, report.MyReplyMinutes);
    }

    [Fact]
    public void The_weekly_counts_end_with_the_newest_week()
    {
        var report = Analyze([
            new RadarMessage(true, At(1)), new RadarMessage(false, At(2)),
            new RadarMessage(true, At(8)), new RadarMessage(true, At(60)), new RadarMessage(true, At(70))
        ]);

        Assert.Equal(RelationshipRadarEngine.SparklineWeeks, report.WeeklyMessages.Count);
        Assert.Equal(2, report.WeeklyMessages[^1]);
        Assert.Equal(1, report.WeeklyMessages[^2]);
        Assert.Equal(0, report.WeeklyMessages[0]);
        Assert.Equal(At(1), report.LastMessageAt);
    }

    [Theory]
    [InlineData(-2, true)]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    public void A_cool_tone_adds_a_note_that_says_it_is_a_guess(int tone, bool flagged)
    {
        var report = Analyze([new RadarMessage(true, At(1))], tone, "Short replies.");

        var signal = report.Signals.SingleOrDefault(s => s.Kind == RadarSignalKinds.Tone);
        Assert.Equal(flagged, signal is not null);
        if (signal is not null)
        {
            Assert.Equal(1, signal.Severity);
            Assert.EndsWith("This is a guess.", signal.Detail);
        }

        Assert.Equal(tone, report.ToneScore);
    }

    [Fact]
    public void Signals_are_ordered_with_the_most_urgent_first()
    {
        var messages = HeavyBaseline();
        messages.Add(new RadarMessage(false, At(6)));

        var report = Analyze(messages);

        Assert.Equal([RadarSignalKinds.Quiet, RadarSignalKinds.Unanswered],
            report.Signals.Select(s => s.Kind).OrderBy(k => k).ToArray());
        Assert.True(report.Signals[0].Severity >= report.Signals[^1].Severity);
    }

    // ---- matching people to chats ----

    private static Person P(string name) => new(Guid.NewGuid(), Owner, name, null, null, null, null, null, null, null,
        null, null, null, Now, Now);

    private static WhatsAppChatSettings Chat(string name, string chatId = Phone, bool group = false,
        bool readAlong = true) =>
        new(Guid.NewGuid(), Owner, Connection, chatId, name, group, readAlong, false, Now, Now, Now);

    [Fact]
    public void A_full_name_matches_ignoring_case_and_accents()
    {
        var zoe = P("Zoë de Vries");

        var match = Assert.Single(LinkMatcher.Match([zoe, P("Piet")], [Chat("zoe DE vries", "+31611111111")]));

        Assert.Equal(zoe.Id, match.PersonId);
        Assert.Equal("+31611111111", match.ChatId);
    }

    [Fact]
    public void A_shared_first_name_matches_only_when_it_is_unique_on_both_sides()
    {
        var anna = P("Anna Jansen");
        var piet = P("Piet");

        Assert.Single(LinkMatcher.Match([anna, piet], [Chat("Anna", "+3161"), Chat("Karel", "+3162")]));
        // Two chats called Anna: guessing would be wrong half the time.
        Assert.Empty(LinkMatcher.Match([anna], [Chat("Anna", "+3161"), Chat("Anna K", "+3162")]));
        // Two people called Anna.
        Assert.Empty(LinkMatcher.Match([anna, P("Anna Bakker")], [Chat("Anna", "+3161")]));
    }

    [Fact]
    public void A_first_name_under_three_letters_is_too_short_to_guess_from()
    {
        Assert.Empty(LinkMatcher.Match([P("Jo Bakker")], [Chat("Jo", "+3161")]));
        // The same name written in full is an exact match.
        Assert.Single(LinkMatcher.Match([P("Jo")], [Chat("Jo", "+3161")]));
    }

    [Fact]
    public void A_chat_that_is_only_a_phone_number_has_no_name_to_compare()
    {
        Assert.Empty(LinkMatcher.Match([P("31612345678")], [Chat("+31 6 1234 5678")]));
    }

    [Fact]
    public void An_exact_name_wins_over_a_first_name_guess()
    {
        var anna = P("Anna");
        var annaJ = P("Anna Jansen");
        var exact = Chat("Anna", "+3161");

        var matches = LinkMatcher.Match([anna, annaJ], [exact]);

        Assert.Equal(anna.Id, Assert.Single(matches).PersonId);
    }

    // ---- tone analyzer parsing ----

    [Theory]
    [InlineData("""{"warmth": 2, "reason": "Warm and playful."}""", 2, "Warm and playful.")]
    [InlineData("""Sure! {"warmth": -1, "reason": "A bit  distant\nlately"} done""", -1, "A bit distant lately")]
    [InlineData("""{"warmth": 0}""", 0, "")]
    public void The_tone_answer_is_read_from_the_models_json(string text, int warmth, string reason)
    {
        var tone = ModelRadarToneAnalyzer.Parse(text);

        Assert.NotNull(tone);
        Assert.Equal(warmth, tone.Warmth);
        Assert.Equal(reason, tone.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("""{"warmth": 3, "reason": "too warm"}""")]
    [InlineData("""{"warmth": -3}""")]
    [InlineData("""{"warmth": "high"}""")]
    [InlineData("""{"reason": "missing score"}""")]
    [InlineData("""{"warmth": """)]
    public void A_tone_answer_that_is_missing_or_out_of_range_is_dropped(string? text)
    {
        Assert.Null(ModelRadarToneAnalyzer.Parse(text));
    }

    [Fact]
    public void A_long_tone_reason_is_cut()
    {
        var tone = ModelRadarToneAnalyzer.Parse("{\"warmth\": 1, \"reason\": \"" + new string('x', 500) + "\"}");

        Assert.True(tone!.Reason.Length <= ModelRadarToneAnalyzer.MaxReasonLength);
    }

    // ---- service ----

    private sealed class Harness
    {
        public FakeClock Clock { get; } = new(Now);
        public PeopleTests.FakePeopleRepository People { get; } = new();
        public InMemoryLinks Links { get; } = new();
        public ChatStore Store { get; } = new();
        public RecordingNotifications Notifications { get; } = new();
        public InMemorySettingsStore Settings { get; } = new();
        public RecordingScheduler Scheduler { get; } = new();
        public FakeTone Tone { get; } = new();
        public RelationshipRadarService Service { get; }

        public Harness(bool withTone = true)
        {
            Service = new RelationshipRadarService(People, Links, new StoreStats(Store), new StoreChats(Store),
                Settings, Notifications, Scheduler, withTone ? Tone : null, Clock);
            People.People.Add(Person());
        }

        public static Person Person(Guid? id = null, Guid? owner = null, string name = "Anna") =>
            new(id ?? PersonId, owner ?? Owner, name, null, null, null, null, null, null, null, null, null, null,
                Now.AddDays(-200), Now.AddDays(-200));

        public WhatsAppChatSettings AddChat(string name = "Anna", string chatId = Phone, bool group = false,
            bool readAlong = true, Guid? owner = null)
        {
            var chat = new WhatsAppChatSettings(Guid.NewGuid(), owner ?? Owner, Connection, chatId, name, group,
                readAlong, false, Now, Now, Now);
            Store.Chats.Add(chat);
            return chat;
        }

        public void AddMessages(IEnumerable<RadarMessage> messages, string chatId = Phone, string text = "hoi")
        {
            foreach (var message in messages)
                Store.Messages.Add(new WhatsAppChatMessage(Guid.NewGuid(), Connection, chatId, Guid.NewGuid().ToString(),
                    message.FromMe, message.FromMe ? null : "Anna", text, message.SentAt));
        }

        public async Task<PersonLinkView> LinkAsync(string chatId = Phone)
        {
            var result = await Service.LinkAsync(Owner, PersonId, Connection, chatId, default);
            return Assert.IsType<PersonLinkView>(result.Value);
        }
    }

    [Fact]
    public async Task Linking_a_chat_to_a_person_saves_it_and_makes_sure_the_check_in_runs()
    {
        var h = new Harness();
        h.AddChat();

        var link = await h.LinkAsync();

        Assert.Equal("Anna", link.DisplayName);
        Assert.True(link.ReadAlong);
        Assert.Equal(Phone, link.ChatId);
        Assert.Equal([Owner], h.Scheduler.Scheduled);
        Assert.Single(await h.Links.ListAsync(Owner, default));
        Assert.True(await h.Service.HasLinksAsync(Owner, default));
    }

    [Fact]
    public async Task Linking_the_same_chat_to_the_same_person_again_changes_nothing()
    {
        var h = new Harness();
        h.AddChat();
        var first = await h.LinkAsync();

        var again = await h.LinkAsync();

        Assert.Equal(first.Id, again.Id);
        Assert.Single(await h.Links.ListAsync(Owner, default));
    }

    [Fact]
    public async Task A_chat_cannot_belong_to_two_people()
    {
        var h = new Harness();
        var piet = Guid.NewGuid();
        h.People.People.Add(Harness.Person(piet, name: "Piet"));
        h.AddChat();
        await h.LinkAsync();

        var result = await h.Service.LinkAsync(Owner, piet, Connection, Phone, default);

        Assert.Equal(PeopleFailure.Conflict, result.Failure);
        Assert.Single(await h.Links.ListAsync(Owner, default));
    }

    [Fact]
    public async Task Groups_unknown_chats_and_unknown_people_cannot_be_linked()
    {
        var h = new Harness();
        h.AddChat("Family", "123@g.us", group: true);

        var group = await h.Service.LinkAsync(Owner, PersonId, Connection, "123@g.us", default);
        var unknown = await h.Service.LinkAsync(Owner, PersonId, Connection, "+31699999999", default);
        var blank = await h.Service.LinkAsync(Owner, PersonId, Connection, "  ", default);
        var nobody = await h.Service.LinkAsync(Owner, Guid.NewGuid(), Connection, Phone, default);

        Assert.Equal(PeopleFailure.Invalid, group.Failure);
        Assert.Equal(PeopleFailure.Invalid, unknown.Failure);
        Assert.Equal(PeopleFailure.Invalid, blank.Failure);
        Assert.Equal(PeopleFailure.NotFound, nobody.Failure);
        Assert.Empty(await h.Links.ListAsync(Owner, default));
        Assert.Empty(h.Scheduler.Scheduled);
    }

    [Fact]
    public async Task A_person_can_have_a_few_chats_but_not_unlimited()
    {
        var h = new Harness();
        for (var i = 0; i < RadarRules.MaxLinksPerPerson; i++)
        {
            var id = $"+3161000000{i}";
            h.AddChat("Anna", id);
            await h.LinkAsync(id);
        }

        h.AddChat("Anna", "+31610000099");
        var result = await h.Service.LinkAsync(Owner, PersonId, Connection, "+31610000099", default);

        Assert.Equal(PeopleFailure.Invalid, result.Failure);
    }

    [Fact]
    public async Task Another_owners_people_and_chats_are_out_of_reach()
    {
        var h = new Harness();
        var theirs = h.AddChat("Anna", "+31688888888", owner: Other);

        var result = await h.Service.LinkAsync(Owner, PersonId, Connection, theirs.ChatId, default);
        Assert.Equal(PeopleFailure.Invalid, result.Failure);

        h.AddChat();
        var link = await h.LinkAsync();
        Assert.Null(await h.Service.PersonAsync(Other, PersonId, default));
        Assert.Null(await h.Service.LinksAsync(Other, PersonId, default));
        Assert.False(await h.Service.UnlinkAsync(Other, PersonId, link.Id, default));
        Assert.Empty((await h.Service.OverviewAsync(Other, default)).People);
        Assert.Empty(await h.Service.SuggestAsync(Other, default));
    }

    [Fact]
    public async Task Unlinking_removes_the_chat_from_the_person()
    {
        var h = new Harness();
        h.AddChat();
        var link = await h.LinkAsync();

        Assert.False(await h.Service.UnlinkAsync(Owner, Guid.NewGuid(), link.Id, default));
        Assert.True(await h.Service.UnlinkAsync(Owner, PersonId, link.Id, default));

        Assert.Empty((await h.Service.LinksAsync(Owner, PersonId, default))!);
        Assert.Null((await h.Service.PersonAsync(Owner, PersonId, default))!.Report);
    }

    [Fact]
    public async Task The_overview_reports_each_linked_person_with_the_worrying_ones_first()
    {
        var h = new Harness();
        var piet = Guid.NewGuid();
        h.People.People.Add(Harness.Person(piet, name: "Piet"));
        h.AddChat("Anna", Phone);
        h.AddChat("Piet", "+31622222222");
        await h.LinkAsync();
        await h.Service.LinkAsync(Owner, piet, Connection, "+31622222222", default);
        h.AddMessages(HeavyBaseline(), Phone);                        // Anna went quiet
        h.AddMessages(Exchanges(29, 1, 2, Quick), "+31622222222");     // Piet is fine

        var overview = await h.Service.OverviewAsync(Owner, default);

        Assert.Equal(["Anna", "Piet"], overview.People.Select(x => x.Name));
        Assert.Equal(2, overview.People[0].Severity);
        Assert.Equal(0, overview.People[1].Severity);
        Assert.False(overview.ToneEnabled);
    }

    [Fact]
    public async Task A_person_view_has_the_links_and_the_report()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages([new RadarMessage(false, At(4))]);

        var view = await h.Service.PersonAsync(Owner, PersonId, default);

        Assert.Single(view!.Links);
        Assert.Equal(1, view.Report!.UnansweredInbound);
    }

    [Fact]
    public async Task Writing_to_a_linked_chat_counts_as_contact()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages([new RadarMessage(false, At(5)), new RadarMessage(true, At(2)), new RadarMessage(true, At(9))]);

        await h.Service.SyncLastContactAsync(Owner, default);

        Assert.Equal(At(2), (await h.People.GetAsync(PersonId, Owner, default))!.LastContactedAt);
    }

    [Fact]
    public async Task A_newer_contact_that_was_logged_by_hand_is_kept()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages([new RadarMessage(true, At(5))]);
        var logged = At(1);
        h.People.People[0] = h.People.People[0] with { LastContactedAt = logged };

        await h.Service.SyncLastContactAsync(Owner, default);

        Assert.Equal(logged, (await h.People.GetAsync(PersonId, Owner, default))!.LastContactedAt);
    }

    [Fact]
    public async Task Messages_from_them_alone_do_not_count_as_contact()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages([new RadarMessage(false, At(1))]);

        await h.Service.SyncLastContactAsync(Owner, default);

        Assert.Null((await h.People.GetAsync(PersonId, Owner, default))!.LastContactedAt);
    }

    [Fact]
    public async Task The_daily_pass_sends_one_notification_for_a_worrying_person_and_then_waits_a_week()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages(HeavyBaseline());

        var first = await h.Service.DailyAsync(Owner, default);

        Assert.Equal(1, first.Notified);
        var note = Assert.Single(h.Notifications.Created);
        Assert.Equal(RadarRules.NotificationType, note.Type);
        Assert.Equal(PersonId, note.SourceId);
        Assert.StartsWith("Anna:", note.Title);

        h.Clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(0, (await h.Service.DailyAsync(Owner, default)).Notified);
        Assert.Single(h.Notifications.Created);

        h.Clock.Advance(TimeSpan.FromDays(5));
        Assert.Equal(1, (await h.Service.DailyAsync(Owner, default)).Notified);
        Assert.Equal(2, h.Notifications.Created.Count);
    }

    [Fact]
    public async Task Notes_below_the_nudge_bar_never_notify()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages([new RadarMessage(false, At(3))]); // unanswered 3 days: a note, severity 1

        Assert.Equal(0, (await h.Service.DailyAsync(Owner, default)).Notified);
        Assert.Empty(h.Notifications.Created);
        Assert.Equal(1, (await h.Service.OverviewAsync(Owner, default)).People.Single().Severity);
    }

    [Fact]
    public async Task Several_worrying_people_share_one_notification()
    {
        var h = new Harness();
        var ids = new List<(Guid Id, string Chat)>();
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            var chat = $"+3162000000{i}";
            h.People.People.Add(Harness.Person(id, name: $"Friend {i}"));
            h.AddChat($"Friend {i}", chat);
            await h.Service.LinkAsync(Owner, id, Connection, chat, default);
            h.AddMessages(HeavyBaseline(), chat);
            ids.Add((id, chat));
        }

        Assert.Equal(1, (await h.Service.DailyAsync(Owner, default)).Notified);

        var note = Assert.Single(h.Notifications.Created);
        Assert.Null(note.SourceId);
        Assert.Contains("and 2 others", note.Title);
    }

    [Fact]
    public void A_nudge_names_everyone_up_to_five_and_counts_the_rest()
    {
        var reports = Enumerable.Range(0, 7).Select(i => RelationshipRadarEngine.Analyze(Guid.NewGuid(), $"P{i}",
            HeavyBaseline().ToArray(), Now)).ToArray();

        var (title, body) = RelationshipRadarService.DescribeNudge(reports);

        Assert.Equal("P0 and 6 others may need a message", title);
        Assert.Contains("P4:", body);
        Assert.DoesNotContain("P5:", body);
        Assert.EndsWith("and 2 more.", body);

        var (pairTitle, _) = RelationshipRadarService.DescribeNudge(reports.Take(2).ToArray());
        Assert.Equal("P0 and P1 may need a message", pairTitle);
    }

    [Fact]
    public async Task Tone_is_not_checked_unless_the_owner_turned_it_on()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages(Exchanges(25, 1, 2, Quick));

        await h.Service.DailyAsync(Owner, default);

        Assert.Equal(0, h.Tone.Calls);
        Assert.Null((await h.Links.ListAsync(Owner, default)).Single().ToneAt);
    }

    [Fact]
    public async Task With_tone_on_a_chat_with_enough_messages_is_judged_and_the_result_is_kept()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages(Exchanges(25, 1, 2, Quick));
        await h.Service.SaveSettingsAsync(Owner, true, default);
        h.Tone.Result = new RadarTone(-2, "Short and cold.");

        await h.Service.DailyAsync(Owner, default);

        Assert.Equal(1, h.Tone.Calls);
        Assert.Equal(["Anna"], h.Tone.Names);
        Assert.All(h.Tone.Texts, text => Assert.Equal("hoi", text));
        var link = (await h.Links.ListAsync(Owner, default)).Single();
        Assert.Equal(-2, link.ToneScore);
        Assert.Equal("Short and cold.", link.ToneReason);
        var report = (await h.Service.PersonAsync(Owner, PersonId, default))!.Report!;
        Assert.Equal(-2, report.ToneScore);
        Assert.Contains(report.Signals, s => s.Kind == RadarSignalKinds.Tone);
    }

    [Fact]
    public async Task Tone_is_only_refreshed_weekly_and_a_quiet_chat_is_not_asked_about_every_day()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages(Exchanges(25, 1, 2, Quick));
        await h.Service.SaveSettingsAsync(Owner, true, default);

        await h.Service.DailyAsync(Owner, default);
        h.Clock.Advance(TimeSpan.FromDays(2));
        await h.Service.DailyAsync(Owner, default);
        Assert.Equal(1, h.Tone.Calls);

        h.Clock.Advance(TimeSpan.FromDays(6));
        await h.Service.DailyAsync(Owner, default);
        Assert.Equal(2, h.Tone.Calls);

        // Fewer than ten recent messages: nothing is sent to the model, but the look is remembered.
        var quiet = new Harness();
        quiet.AddChat();
        await quiet.LinkAsync();
        quiet.AddMessages([new RadarMessage(true, At(2)), new RadarMessage(false, At(1))]);
        await quiet.Service.SaveSettingsAsync(Owner, true, default);
        await quiet.Service.DailyAsync(Owner, default);
        Assert.Equal(0, quiet.Tone.Calls);
        Assert.NotNull((await quiet.Links.ListAsync(Owner, default)).Single().ToneAt);
    }

    [Fact]
    public async Task A_failing_tone_check_does_not_stop_the_daily_pass()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages(HeavyBaseline());
        h.AddMessages(Exchanges(29, 8, 3, Quick));            // enough recent text for the model to be asked
        h.AddMessages([new RadarMessage(false, At(6))]);       // and a message left unanswered for six days
        await h.Service.SaveSettingsAsync(Owner, true, default);
        h.Tone.Throw = true;

        var result = await h.Service.DailyAsync(Owner, default);

        Assert.Equal(1, h.Tone.Calls);
        Assert.Equal(1, result.Notified);
        Assert.Null((await h.Links.ListAsync(Owner, default)).Single().ToneAt);
    }

    [Fact]
    public async Task Saving_the_tone_choice_keeps_the_nudge_history()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages(HeavyBaseline());
        await h.Service.DailyAsync(Owner, default);

        var saved = await h.Service.SaveSettingsAsync(Owner, true, default);

        Assert.True(saved.ToneEnabled);
        Assert.Equal(Now, saved.LastNudgedAt);
        Assert.True((await h.Service.SettingsAsync(Owner, default)).ToneEnabled);
    }

    [Fact]
    public async Task Suggestions_skip_linked_people_linked_chats_and_groups()
    {
        var h = new Harness();
        var piet = Guid.NewGuid();
        h.People.People.Add(Harness.Person(piet, name: "Piet"));
        h.AddChat("Anna", Phone);
        h.AddChat("Piet", "+31622222222");
        h.AddChat("Piet's club", "999@g.us", group: true);
        await h.LinkAsync();

        var suggestions = await h.Service.SuggestAsync(Owner, default);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal(piet, suggestion.PersonId);
        Assert.Equal("+31622222222", suggestion.ChatId);
    }

    [Fact]
    public async Task Link_candidates_are_one_to_one_unlinked_chats_with_read_along_ones_first()
    {
        var h = new Harness();
        h.AddChat("Zoe", "+31633333333", readAlong: false);
        h.AddChat("Anna", Phone);
        h.AddChat("Bram", "+31644444444");
        h.AddChat("Family", "123@g.us", group: true);
        h.AddChat("Not mine", "+31655555555", owner: Other);
        await h.LinkAsync();

        var candidates = await h.Service.CandidatesAsync(Owner, default);

        Assert.Equal(["Bram", "Zoe"], candidates.Select(c => c.DisplayName).ToArray());
        Assert.True(candidates[0].ReadAlong);
        Assert.False(candidates[1].ReadAlong);
        Assert.Equal(Connection, candidates[0].ConnectionId);
        // Each owner only ever sees their own chats.
        Assert.Equal(["Not mine"], (await h.Service.CandidatesAsync(Other, default)).Select(c => c.DisplayName));
    }

    // ---- daily check-in integration ----

    private static PeopleCheckInService CheckIn(Harness h, bool withRadar = true) =>
        new(h.People, h.Notifications, new NoBriefings(), h.Clock, withRadar ? h.Service : null);

    [Fact]
    public async Task A_person_with_only_a_linked_chat_keeps_the_check_in_running_and_gets_radar_nudges()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages(HeavyBaseline());

        var result = await CheckIn(h).RunAsync(Owner, default);

        Assert.True(result.Continue);
        Assert.Equal(1, result.RadarNotified);
        Assert.Equal(0, result.CheckInsNotified);
        Assert.Equal(RadarRules.NotificationType, Assert.Single(h.Notifications.Created).Type);
    }

    [Fact]
    public async Task Without_the_radar_a_person_with_no_birthday_or_cadence_still_stops_the_check_in()
    {
        var h = new Harness();
        h.AddChat();
        await h.LinkAsync();

        var result = await CheckIn(h, withRadar: false).RunAsync(Owner, default);

        Assert.False(result.Continue);
    }

    [Fact]
    public async Task With_no_links_and_nobody_to_watch_the_check_in_stops_as_before()
    {
        var h = new Harness();

        var result = await CheckIn(h).RunAsync(Owner, default);

        Assert.False(result.Continue);
        Assert.Empty(h.Notifications.Created);
    }

    [Fact]
    public async Task Writing_to_a_linked_chat_means_the_check_in_does_not_nag()
    {
        var h = new Harness();
        // Due: wanted every 7 days, last contact 20 days ago.
        h.People.People[0] = h.People.People[0] with { ContactEveryDays = 7, LastContactedAt = Now.AddDays(-20) };
        h.AddChat();
        await h.LinkAsync();
        h.AddMessages([new RadarMessage(true, At(1))]);

        var result = await CheckIn(h).RunAsync(Owner, default);

        Assert.Equal(0, result.CheckInsNotified);
        Assert.DoesNotContain(h.Notifications.Created, n => n.Type == PeopleCheckInService.CheckInType);
        Assert.Equal(At(1), (await h.People.GetAsync(PersonId, Owner, default))!.LastContactedAt);

        // The same person without a linked chat would have been nudged.
        var plain = new Harness();
        plain.People.People[0] = plain.People.People[0] with
        {
            ContactEveryDays = 7, LastContactedAt = Now.AddDays(-20)
        };
        var nudged = await CheckIn(plain, withRadar: false).RunAsync(Owner, default);
        Assert.Equal(1, nudged.CheckInsNotified);
    }

    [Fact]
    public async Task A_radar_failure_never_stops_birthdays_and_check_ins()
    {
        var h = new Harness();
        h.People.People[0] = h.People.People[0] with { ContactEveryDays = 7, LastContactedAt = Now.AddDays(-20) };
        h.AddChat();
        await h.LinkAsync();
        h.Store.FailStats = true;

        var result = await CheckIn(h).RunAsync(Owner, default);

        Assert.True(result.Continue);
        Assert.Equal(1, result.CheckInsNotified);
        Assert.Equal(0, result.RadarNotified);
    }

    // ---- fakes ----

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }

    private sealed class ChatStore
    {
        public List<WhatsAppChatSettings> Chats { get; } = [];
        public List<WhatsAppChatMessage> Messages { get; } = [];
        public bool FailStats { get; set; }
    }

    private sealed class StoreStats(ChatStore store) : IChatActivityStats
    {
        public Task<IReadOnlyList<ChatActivityStat>> ListAsync(Guid ownerId, IReadOnlyCollection<ChatKey> chats,
            DateTimeOffset since, CancellationToken ct)
        {
            if (store.FailStats) throw new InvalidOperationException("stats down");
            var keys = chats.ToHashSet();
            return Task.FromResult<IReadOnlyList<ChatActivityStat>>(store.Messages
                .Where(m => m.SentAt > since && keys.Contains(new ChatKey(m.ConnectionId, m.ChatId)))
                .Select(m => new ChatActivityStat(m.ConnectionId, m.ChatId, m.FromMe, m.SentAt)).ToArray());
        }
    }

    private sealed class InMemoryLinks : IPersonLinkRepository
    {
        private readonly List<PersonChannelLink> _rows = [];

        public Task<IReadOnlyList<PersonChannelLink>> ListAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PersonChannelLink>>(_rows.Where(x => x.OwnerId == ownerId)
                .OrderBy(x => x.CreatedAt).ToArray());

        public Task<PersonChannelLink?> FindAsync(Guid ownerId, Guid connectionId, string chatId, CancellationToken ct) =>
            Task.FromResult(_rows.FirstOrDefault(x => x.OwnerId == ownerId && x.ConnectionId == connectionId &&
                                                      x.ChatId == chatId));

        public Task AddAsync(PersonChannelLink link, CancellationToken ct)
        {
            _rows.Add(link);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, Guid personId, Guid ownerId, CancellationToken ct) =>
            Task.FromResult(_rows.RemoveAll(x => x.Id == id && x.PersonId == personId && x.OwnerId == ownerId) > 0);

        public Task UpdateToneAsync(Guid id, Guid ownerId, int? score, string? reason, DateTimeOffset at,
            CancellationToken ct)
        {
            var index = _rows.FindIndex(x => x.Id == id && x.OwnerId == ownerId);
            if (index >= 0) _rows[index] = _rows[index] with { ToneScore = score, ToneReason = reason, ToneAt = at };
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingScheduler : IPeopleCheckInScheduler
    {
        public List<Guid> Scheduled { get; } = [];

        public Task SchedulePeopleCheckInAsync(Guid ownerId, CancellationToken ct)
        {
            Scheduled.Add(ownerId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTone : IRadarToneAnalyzer
    {
        public int Calls { get; private set; }
        public bool Throw { get; set; }
        public RadarTone? Result { get; set; } = new(0, "Neutral.");
        public List<string> Names { get; } = [];
        public List<string> Texts { get; } = [];

        public Task<RadarTone?> AnalyzeAsync(Guid ownerId, string personName, IReadOnlyList<RadarMessageText> messages,
            CancellationToken ct)
        {
            Calls++;
            Names.Add(personName);
            Texts.AddRange(messages.Select(m => m.Text));
            if (Throw) throw new InvalidOperationException("model down");
            return Task.FromResult(Result);
        }
    }

    private sealed class NoBriefings : IDailyBriefingRepository
    {
        public Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<DailyBriefingPreferenceRecord?>(null);
        public Task<(DailyBriefingPreferenceRecord Preference, string PreviousWorkflowId)> SaveAsync(Guid ownerId,
            SaveDailyBriefingRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DailyBriefingPreferenceRecord>> ListPendingForSchedulingAsync(
            CancellationToken ct) => throw new NotSupportedException();
        public Task<int> RequeueStaleEnabledAsync(DateTimeOffset utcNow, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task MarkScheduleDispatchedAsync(Guid ownerId, string workflowId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> DeliverAsync(DailyBriefingActivityInput input, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class StoreChats(ChatStore store) : IWhatsAppAssistantRepository
    {
        public Task<IReadOnlyList<WhatsAppChatSettings>> ListChatsAsync(Guid ownerId, Guid? connectionId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WhatsAppChatSettings>>(store.Chats.Where(x => x.OwnerId == ownerId)
                .ToArray());

        public Task<WhatsAppChatSettings?> GetChatAsync(Guid ownerId, Guid connectionId, string chatId,
            CancellationToken ct) =>
            Task.FromResult(store.Chats.SingleOrDefault(x => x.OwnerId == ownerId && x.ConnectionId == connectionId &&
                                                           x.ChatId == chatId));

        public Task<IReadOnlyList<WhatsAppChatMessage>> ListMessagesAsync(Guid ownerId, Guid connectionId,
            string chatId, int limit, DateTimeOffset? before, CancellationToken ct, Guid? beforeId = null) =>
            Task.FromResult<IReadOnlyList<WhatsAppChatMessage>>(store.Messages
                .Where(x => x.ConnectionId == connectionId && x.ChatId == chatId)
                .OrderByDescending(x => x.SentAt).Take(limit).ToArray());

        public Task<bool> MergeChatAliasesAsync(Guid ownerId, Guid connectionId, string phoneId,
            IReadOnlyList<string> aliases, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppChatMessage?> GetMessageAsync(Guid ownerId, Guid connectionId, string chatId,
            Guid messageId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppChatActivity>> ListActivityAsync(Guid ownerId, Guid connectionId,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> MarkReadAsync(Guid ownerId, Guid connectionId, string chatId, Guid messageId,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppChatSettings?> SaveChatAsync(Guid ownerId, Guid connectionId, string chatId,
            string displayName, bool readAlong, bool autoReminders, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListWatchedChatIdsAsync(Guid connectionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<int> StoreObservedAsync(Guid connectionId, IReadOnlyList<ObservedWhatsAppMessage> messages,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<(byte[] Content, string ContentType)?> OpenMediaAsync(Guid ownerId, Guid connectionId,
            Guid messageId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppSearchHit>> SearchAsync(Guid ownerId, string query, Guid? connectionId,
            string? chatId, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> ClearHistoryAsync(Guid ownerId, Guid connectionId, string chatId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task SetAskConversationAsync(Guid ownerId, Guid chatSettingsId, Guid conversationId,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppScanBatch>> ClaimScanBatchesAsync(TimeSpan quiet, int limit,
            int contextSize, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteScanAsync(Guid chatSettingsId, DateTimeOffset scannedThrough, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppCatchUpBatch>> ClaimCatchUpBatchesAsync(TimeSpan quiet, int minUnread,
            int limit, int contextSize, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteCatchUpAsync(Guid chatSettingsId, DateTimeOffset through, WhatsAppCatchUp? catchUp,
            CancellationToken ct) => throw new NotSupportedException();
    }
}
