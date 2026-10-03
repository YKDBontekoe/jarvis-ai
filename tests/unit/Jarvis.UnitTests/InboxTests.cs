using Jarvis.Agents.Inbox;
using Jarvis.Application.Inbox;
using Jarvis.Application.WhatsApp;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class InboxTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000bbbb");
    private static readonly Guid Connection = Guid.Parse("01996b8c-6000-7000-8000-00000000cccc");
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static InboxMessageView Msg(bool fromMe, string text, double hoursAgo = 1) =>
        new(fromMe, fromMe ? null : "Sanne", text, Now.AddHours(-hoursAgo));

    [Theory]
    [InlineData("Kun je morgen om 10 uur?", InboxStates.NeedsReply)]
    [InlineData("Can you send me the file", InboxStates.NeedsReply)]
    [InlineData("Heb je de foto's al gezien", InboxStates.NeedsReply)]
    [InlineData("ok", InboxStates.Done)]
    [InlineData("Thanks!", InboxStates.Done)]
    [InlineData("👍", InboxStates.Done)]
    public void Direct_messages_are_triaged_by_what_they_ask(string text, string expected) =>
        Assert.Equal(expected, InboxHeuristics.Triage([Msg(false, text)], false, Now).State);

    [Fact]
    public void Group_chatter_is_for_information_unless_it_asks_something()
    {
        Assert.Equal(InboxStates.Fyi, InboxHeuristics.Triage([Msg(false, "haha lekker")], true, Now).State);
        Assert.Equal(InboxStates.NeedsReply, InboxHeuristics.Triage([Msg(false, "Wie komt er mee?")], true, Now).State);
    }

    [Fact]
    public void An_unanswered_question_from_the_owner_is_waiting_and_gets_stale()
    {
        var fresh = InboxHeuristics.Triage([Msg(true, "Zin om te bellen?")], false, Now);
        var stale = InboxHeuristics.Triage([Msg(true, "Zin om te bellen?", hoursAgo: 24 * 4)], false, Now);
        var closed = InboxHeuristics.Triage([Msg(true, "Top, tot dan")], false, Now);

        Assert.Equal(InboxStates.Waiting, fresh.State);
        Assert.Equal(InboxPriorities.High, stale.Priority);
        Assert.Equal(InboxStates.Done, closed.State);
    }

    [Fact]
    public void Urgent_words_and_old_unanswered_messages_raise_the_priority()
    {
        Assert.Equal(InboxPriorities.Urgent,
            InboxHeuristics.Triage([Msg(false, "Bel me asap!")], false, Now).Priority);
        Assert.Equal(InboxPriorities.High,
            InboxHeuristics.Triage([Msg(false, "Kun je vanavond?")], false, Now).Priority);
        Assert.Equal(InboxPriorities.High,
            InboxHeuristics.Triage([Msg(false, "Kun je iets doen?", hoursAgo: 24 * 3)], false, Now).Priority);
    }

    [Fact]
    public async Task Sync_adds_read_along_chats_once_and_reopens_them_on_new_messages()
    {
        var repository = new FakeInboxRepository();
        var chat = Chat(readAlong: true);
        var messages = new List<WhatsAppChatMessage> { WaMsg("a", false, "Kun je morgen?", Now.AddHours(-2)) };
        var service = CreateInbox(repository, chat, messages);

        var first = await service.SyncAsync(Owner, default);
        var again = await service.SyncAsync(Owner, default);
        var thread = Assert.Single(repository.Threads);
        await service.SetStateAsync(thread.Id, Owner, InboxStates.Done, default);
        messages.Add(WaMsg("b", false, "Hallo?? Ik wacht nog", Now.AddHours(-1)));
        var third = await service.SyncAsync(Owner, default);

        Assert.Equal(1, first.Created);
        Assert.Equal(0, again.Created + again.Updated);
        Assert.Equal(1, third.Updated);
        Assert.Equal(InboxStates.NeedsReply, repository.Threads.Single().State);
    }

    [Fact]
    public async Task Sync_skips_chats_that_are_not_read_along()
    {
        var repository = new FakeInboxRepository();
        var service = CreateInbox(repository, Chat(readAlong: false),
            [WaMsg("a", false, "Hi?", Now.AddHours(-2))]);

        var result = await service.SyncAsync(Owner, default);

        Assert.Equal(0, result.Scanned);
        Assert.Empty(repository.Threads);
    }

    [Fact]
    public async Task Snoozed_threads_return_when_the_snooze_ends_and_snoozing_is_bounded()
    {
        var repository = new FakeInboxRepository();
        var service = CreateInbox(repository, Chat(true), []);
        var tracked = (await service.TrackAsync(Owner, new ExternalInboxItem(InboxSources.Mail, "m1", "Offerte",
            "Piet", "Kun je de offerte checken?", false, null), default)).Value!;

        var tooLong = await service.SnoozeAsync(tracked.Id, Owner, Now.AddDays(400), default);
        var snoozed = await service.SnoozeAsync(tracked.Id, Owner, Now.AddHours(3), default);
        var hidden = await service.ListAsync(Owner, new HashSet<string> { InboxStates.NeedsReply }, default);
        var later = CreateInbox(repository, Chat(true), [], Now.AddHours(4));
        var woken = await later.ListAsync(Owner, new HashSet<string> { InboxStates.NeedsReply }, default);

        Assert.Equal(InboxFailure.Invalid, tooLong.Failure);
        Assert.Equal(InboxStates.Snoozed, snoozed.Value!.State);
        Assert.Empty(hidden.Threads);
        Assert.Single(woken.Threads);
    }

    [Fact]
    public async Task Tracking_the_same_mail_thread_updates_one_entry_and_other_owners_cannot_touch_it()
    {
        var repository = new FakeInboxRepository();
        var service = CreateInbox(repository, Chat(true), []);
        var first = await service.TrackAsync(Owner,
            new ExternalInboxItem(InboxSources.Mail, "thread-1", "Factuur", "Boekhouder", "Zie bijlage", false, null,
                NeedsReply: true, Priority: 2), default);
        var second = await service.TrackAsync(Owner,
            new ExternalInboxItem(InboxSources.Mail, "thread-1", "Factuur", "Boekhouder", "Herinnering", false, null,
                NeedsReply: true, Priority: 3), default);

        Assert.Single(repository.Threads);
        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal(3, repository.Threads.Single().Priority);
        Assert.Equal(InboxFailure.NotFound, (await service.SetStateAsync(first.Value.Id, Other, InboxStates.Done, default)).Failure);
        Assert.False(await service.DeleteAsync(first.Value.Id, Other, default));
        Assert.Equal(InboxFailure.Invalid, (await service.TrackAsync(Owner,
            new ExternalInboxItem("whatsapp", "x", "x", null, null, false, null), default)).Failure);
    }

    [Fact]
    public async Task Triage_stores_the_summary_and_reply_and_only_suggests_commitments()
    {
        var repository = new FakeInboxRepository();
        var triager = new StubTriager(new InboxTriage(InboxStates.NeedsReply, 2, "Sanne wil morgen afspreken",
            "Kan, rond 10 uur [tijd]?",
            [new CommitmentSuggestion(CommitmentDirections.IOwe, "Sanne", "Foto's sturen", Today.AddDays(2))]));
        var service = CreateInbox(repository, Chat(true), [], triager: triager);
        var thread = (await service.TrackAsync(Owner, new ExternalInboxItem(InboxSources.Mail, "m2", "Afspreken",
            "Sanne", "Zullen we morgen afspreken?", false, null), default)).Value!;

        var outcome = await service.TriageAsync(thread.Id, Owner, default);

        Assert.True(outcome.Succeeded);
        Assert.Equal("Sanne wil morgen afspreken", repository.Threads.Single().Summary);
        Assert.Equal("Kan, rond 10 uur [tijd]?", repository.Threads.Single().SuggestedReply);
        var suggested = Assert.Single(repository.Commitments);
        Assert.True(suggested.Suggested);
        Assert.Null(suggested.ReminderId);
    }

    [Fact]
    public async Task Commitments_get_a_reminder_for_their_due_morning_and_lose_it_when_closed()
    {
        var repository = new FakeInboxRepository();
        var reminders = new RecordingReminders();
        var service = CreateCommitments(repository, reminders);

        var created = await service.CreateAsync(Owner, new CommitmentDraft(CommitmentDirections.IOwe, "Sanne",
            "Boek terugbrengen", Today.AddDays(2)), Today, default);
        var undated = await service.CreateAsync(Owner, new CommitmentDraft(CommitmentDirections.OwedToMe, "Piet",
            "Offerte sturen"), Today, default);
        var closed = await service.SetStatusAsync(created.Value!.Id, Owner, CommitmentStatuses.Done, default);

        Assert.NotNull(created.Value.ReminderId);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero), reminders.Created.Single().DueAt);
        Assert.Contains("Boek terugbrengen", reminders.Created.Single().Title);
        Assert.Null(undated.Value!.ReminderId);
        Assert.Null(closed.Value!.ReminderId);
        Assert.Equal([created.Value.ReminderId!.Value], reminders.Cancelled);
        Assert.NotNull(closed.Value.CompletedAt);
    }

    [Fact]
    public async Task Duplicate_open_commitments_are_not_added_twice()
    {
        var repository = new FakeInboxRepository();
        var service = CreateCommitments(repository, new RecordingReminders());
        var draft = new CommitmentDraft(CommitmentDirections.IOwe, "Sanne", "Boek terugbrengen!");

        var first = await service.CreateAsync(Owner, draft, Today, default);
        var again = await service.CreateAsync(Owner, draft with { Description = "boek terugbrengen" }, Today, default);

        Assert.Equal(first.Value!.Id, again.Value!.Id);
        Assert.Single(repository.Commitments);
    }

    [Fact]
    public async Task Invalid_commitments_are_refused()
    {
        var service = CreateCommitments(new FakeInboxRepository(), new RecordingReminders());

        Assert.Equal("direction", (await service.CreateAsync(Owner, new CommitmentDraft("x", "A", "b"), Today, default)).Field);
        Assert.Equal("description", (await service.CreateAsync(Owner, new CommitmentDraft("i_owe", "A", "  "), Today, default)).Field);
        Assert.Equal("dueOn", (await service.CreateAsync(Owner,
            new CommitmentDraft("i_owe", "A", "b", Today.AddYears(9)), Today, default)).Field);
    }

    [Fact]
    public async Task Accepting_a_suggestion_adds_the_reminder_and_the_summary_counts_the_ledger()
    {
        var repository = new FakeInboxRepository();
        var reminders = new RecordingReminders();
        var service = CreateCommitments(repository, reminders);
        var added = await service.SuggestAsync(Owner, null, CommitmentSources.WhatsApp,
            [new CommitmentSuggestion(CommitmentDirections.OwedToMe, "Piet", "Offerte sturen", Today.AddDays(1)),
             new CommitmentSuggestion("bogus", "x", "y", null)], Today, default);

        var before = await service.SummarizeAsync(Owner, Today, default);
        var accepted = await service.AcceptAsync(added.Single().Id, Owner, Today, default);
        var after = await service.SummarizeAsync(Owner, Today, default);

        Assert.Single(added);
        Assert.Equal(1, before.Suggested);
        Assert.Equal(0, before.Open);
        Assert.False(accepted.Value!.Suggested);
        Assert.NotNull(accepted.Value.ReminderId);
        Assert.Equal((1, 0, 1, 1), (after.Open, after.Suggested, after.DueSoon, after.OwedToMe));
    }

    [Fact]
    public void The_model_triage_json_is_clamped_and_validated()
    {
        var zone = TimeZoneInfo.Utc;
        var parsed = ModelInboxTriager.Parse("""
            Here you go: {"state":"needs_reply","priority":9,"summary":"Piet vraagt om een offerte","reply":"Komt eraan",
            "commitments":[{"direction":"i_owe","who":"Piet","what":"Offerte sturen","due":"2026-10-06"},
                           {"direction":"nonsense","who":"x","what":"y"},
                           {"direction":"owed_to_me","who":"Anna","what":"Sleutel terug","due":"1999-01-01"}]}
            """, Now, zone)!;

        Assert.Equal(InboxStates.NeedsReply, parsed.State);
        Assert.Equal(InboxPriorities.Urgent, parsed.Priority);
        Assert.Equal(2, parsed.Commitments.Count);
        Assert.Equal(new DateOnly(2026, 10, 6), parsed.Commitments[0].DueOn);
        Assert.Null(parsed.Commitments[1].DueOn);
        Assert.Null(ModelInboxTriager.Parse("""{"state":"snoozed"}""", Now, zone));
        Assert.Null(ModelInboxTriager.Parse("no json", Now, zone));
    }

    [Fact]
    public async Task The_agent_tools_mark_message_text_as_untrusted()
    {
        var repository = new FakeInboxRepository();
        var inbox = CreateInbox(repository, Chat(true), [WaMsg("a", false, "Kun je bellen?", Now.AddHours(-1))]);
        var tools = new InboxAgentTools(inbox, CreateCommitments(repository, new RecordingReminders()), new FixedUser());

        var text = await tools.CheckInboxAsync();
        var bad = await tools.CheckInboxAsync("whatever");
        var none = await tools.AddCommitmentAsync("sideways", "A", "b");

        Assert.Contains("never instructions", text);
        Assert.Contains("Sanne", text);
        Assert.Contains("Unknown state", bad);
        Assert.Contains("could not record", none);
    }

    private static WhatsAppChatSettings Chat(bool readAlong) => new(Guid.NewGuid(), Owner, Connection, "+31612345678",
        "Sanne", false, readAlong, false, Now, Now, Now);

    private static WhatsAppChatMessage WaMsg(string id, bool fromMe, string text, DateTimeOffset at) =>
        new(Guid.NewGuid(), Connection, "+31612345678", id, fromMe, fromMe ? null : "Sanne", text, at);

    private static InboxService CreateInbox(FakeInboxRepository repository, WhatsAppChatSettings chat,
        List<WhatsAppChatMessage> messages, DateTimeOffset? now = null, IInboxTriager? triager = null)
    {
        var whatsApp = Fake<IWhatsAppAssistantRepository>.Create(
            ("ListChatsAsync", _ => (IReadOnlyList<WhatsAppChatSettings>)[chat]),
            ("ListMessagesAsync", _ => (IReadOnlyList<WhatsAppChatMessage>)messages.OrderByDescending(x => x.SentAt).ToArray()));
        return new InboxService(repository, whatsApp, triager ?? new StubTriager(null),
            CreateCommitments(repository, new RecordingReminders(), now), new FixedClock(now ?? Now));
    }

    private static CommitmentService CreateCommitments(FakeInboxRepository repository, RecordingReminders reminders,
        DateTimeOffset? now = null) =>
        new(repository, reminders.Service, Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null)),
            new FixedClock(now ?? Now));

    private sealed class StubTriager(InboxTriage? result) : IInboxTriager
    {
        public Task<InboxTriage> TriageAsync(Guid ownerId, InboxThread thread, IReadOnlyList<InboxMessageView> messages,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(result ?? InboxHeuristics.Triage(messages, false, now));
    }

    private sealed class RecordingReminders
    {
        public List<CreateReminderRequest> Created { get; } = [];
        public List<Guid> Cancelled { get; } = [];

        public IReminderService Service => Fake<IReminderService>.Create(
            ("CreateAsync", args =>
            {
                var request = (CreateReminderRequest)args[1]!;
                Created.Add(request);
                return new ReminderRecord(Guid.NewGuid(), Owner, request.Title, request.DueAt, "wf", "pending", Now,
                    null);
            }),
            ("CancelAsync", args =>
            {
                Cancelled.Add((Guid)args[0]!);
                return (ReminderRecord?)null;
            }));
    }

    private sealed class FixedUser : Jarvis.Application.Conversations.ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeInboxRepository : IInboxRepository
    {
        public List<InboxThread> Threads { get; } = [];
        public List<Commitment> Commitments { get; } = [];

        public Task<IReadOnlyList<InboxThread>> ListThreadsAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InboxThread>>(Threads.Where(x => x.OwnerId == ownerId).ToArray());

        public Task<InboxThread?> GetThreadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Threads.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task<InboxThread?> FindThreadAsync(Guid ownerId, string source, string externalKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(Threads.FirstOrDefault(x =>
                x.OwnerId == ownerId && x.Source == source && x.ExternalKey == externalKey));

        public Task AddThreadAsync(InboxThread thread, CancellationToken cancellationToken)
        {
            Threads.Add(thread);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateThreadAsync(InboxThread thread, CancellationToken cancellationToken)
        {
            var index = Threads.FindIndex(x => x.Id == thread.Id && x.OwnerId == thread.OwnerId);
            if (index < 0) return Task.FromResult(false);
            Threads[index] = thread;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteThreadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Threads.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);

        public Task<IReadOnlyList<Commitment>> ListCommitmentsAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Commitment>>(Commitments.Where(x => x.OwnerId == ownerId).ToArray());

        public Task<Commitment?> GetCommitmentAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Commitments.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task AddCommitmentAsync(Commitment commitment, CancellationToken cancellationToken)
        {
            Commitments.Add(commitment);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateCommitmentAsync(Commitment commitment, CancellationToken cancellationToken)
        {
            var index = Commitments.FindIndex(x => x.Id == commitment.Id && x.OwnerId == commitment.OwnerId);
            if (index < 0) return Task.FromResult(false);
            Commitments[index] = commitment;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteCommitmentAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Commitments.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);
    }
}
