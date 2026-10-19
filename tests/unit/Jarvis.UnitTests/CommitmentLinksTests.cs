using Jarvis.Application.Inbox;
using Jarvis.Application.People;
using Jarvis.Application.Projects;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;
using Jarvis.Domain.People;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CommitmentLinksTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fc01");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fc02");
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);

    private static Commitment Promise(string who = "Rosa de Vries", string what = "Send the deck",
        DateOnly? due = null, string direction = CommitmentDirections.OwedToMe, bool suggested = false,
        string status = CommitmentStatuses.Open, Guid? person = null) =>
        new(Guid.NewGuid(), Owner, direction, who, what, due, status, suggested, CommitmentSources.Chat, null, null,
            null, Now.AddDays(-20), Now.AddDays(-20), null, person);

    private sealed class Setup
    {
        public Commitment Promise = CommitmentLinksTests.Promise();
        public readonly Guid ProjectId = Guid.NewGuid();
        public readonly Person Person = new(Guid.NewGuid(), Owner, "Rosa de Vries", null, null, null, null, null, null,
            null, null, null, null, Now, Now);

        public CommitmentService Service => new(
            Fake<IInboxRepository>.Create(
                ("GetCommitmentAsync", args => (Guid)args[0]! == Promise.Id && (Guid)args[1]! == Owner ? Promise : null),
                ("UpdateCommitmentAsync", args =>
                {
                    Promise = (Commitment)args[0]!;
                    return true;
                })),
            Fake<IReminderService>.Create(),
            Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null)),
            new FixedClock(), null,
            Fake<IProjectStore>.Create(("GetAsync", args =>
                (Guid)args[0]! == ProjectId && (Guid)args[1]! == Owner ? Project() : null)),
            Fake<IPeopleRepository>.Create(("GetAsync", args =>
                (Guid)args[0]! == Person.Id && (Guid)args[1]! == Owner ? Person : null)));

        private ProjectRecord Project() => new(ProjectId, "Kitchen", null, null, "blue", Now, Now, 0, 0, 0);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task A_promise_can_be_linked_to_a_project_and_a_person_and_unlinked()
    {
        var s = new Setup();

        var linked = await s.Service.SetLinksAsync(s.Promise.Id, Owner, s.ProjectId, s.Person.Id, false, false, default);
        Assert.Equal((s.ProjectId, s.Person.Id), (linked.Value!.ProjectId, linked.Value.PersonId));

        var cleared = await s.Service.SetLinksAsync(s.Promise.Id, Owner, null, null, true, false, default);
        Assert.Null(cleared.Value!.ProjectId);
        Assert.Equal(s.Person.Id, cleared.Value.PersonId);
    }

    [Fact]
    public async Task Links_to_things_that_are_not_the_owners_or_missing_are_refused()
    {
        var s = new Setup();

        var project = await s.Service.SetLinksAsync(s.Promise.Id, Owner, Guid.NewGuid(), null, false, false, default);
        var person = await s.Service.SetLinksAsync(s.Promise.Id, Owner, null, Guid.NewGuid(), false, false, default);
        var stranger = await s.Service.SetLinksAsync(s.Promise.Id, Other, s.ProjectId, null, false, false, default);

        Assert.Equal("projectId", project.Field);
        Assert.Equal("personId", person.Field);
        Assert.Equal(InboxFailure.NotFound, stranger.Failure);
        Assert.Null(s.Promise.ProjectId);
    }

    [Fact]
    public void A_linked_promise_belongs_to_that_person_whatever_the_name_says_and_never_to_another()
    {
        var person = new Setup().Person;
        var linked = Promise("Ro", "Linked by owner", person: person.Id);
        var elsewhere = Promise("Rosa de Vries", "Linked to someone else", person: Guid.NewGuid());
        var byName = Promise("ROSA DE VRIES", "Same name, no link");

        Assert.True(PersonFollowThroughBuilder.IsCounterparty(person, linked));
        Assert.False(PersonFollowThroughBuilder.IsCounterparty(person, elsewhere));
        Assert.True(PersonFollowThroughBuilder.IsCounterparty(person, byName));
    }

    [Fact]
    public void Only_open_accepted_owed_to_me_promises_that_are_three_days_late_get_a_draft_in_the_chosen_language()
    {
        var drafts = CommitmentFollowUpRules.Build(
        [
            Promise(what: "Late enough", due: Today.AddDays(-3)),
            Promise(what: "Not late enough", due: Today.AddDays(-2)),
            Promise(what: "I owe it", due: Today.AddDays(-9), direction: CommitmentDirections.IOwe),
            Promise(what: "Suggested", due: Today.AddDays(-9), suggested: true),
            Promise(what: "Done", due: Today.AddDays(-9), status: CommitmentStatuses.Done),
            Promise(what: "No date")
        ], Today, new FollowUpState(), "nl");

        var draft = Assert.Single(drafts);
        Assert.Equal(3, draft.DaysOverdue);
        Assert.StartsWith("Hoi Rosa,", draft.Draft);
        Assert.Contains("Late enough", draft.Draft);
        Assert.StartsWith("Hi Rosa,", CommitmentFollowUpRules.Draft(Promise(due: Today), "en"));
    }

    [Fact]
    public void Drafts_are_capped_and_longest_overdue_first()
    {
        var many = Enumerable.Range(0, 8).Select(i => Promise(what: $"P{i}", due: Today.AddDays(-5 - i))).ToArray();

        var drafts = CommitmentFollowUpRules.Build(many, Today, new FollowUpState(), "en");

        Assert.Equal(CommitmentFollowUpRules.MaxDrafts, drafts.Count);
        Assert.Equal("P7", drafts[0].Description);
    }

    private sealed class Quiet
    {
        public readonly InMemorySettingsStore Settings = new();
        public readonly Commitment Promise = CommitmentLinksTests.Promise(due: Today.AddDays(-10));

        public CommitmentFollowUpService Service => new(
            Fake<ICommitmentService>.Create(
                ("TodayAsync", _ => Today),
                ("ListAsync", _ => (IReadOnlyList<Commitment>)[Promise]),
                ("GetAsync", args => (Guid)args[0]! == Promise.Id && (Guid)args[1]! == Owner ? Promise : null)),
            Settings);
    }

    [Fact]
    public async Task Snoozing_hides_a_nudge_until_its_day_and_dismissing_hides_it_for_good()
    {
        var q = new Quiet();
        Assert.Single(await q.Service.ListAsync(Owner, null, default));

        Assert.True(await q.Service.SnoozeAsync(Owner, q.Promise.Id, 7, default));
        Assert.Empty(await q.Service.ListAsync(Owner, null, default));
        var state = await q.Settings.GetAsync<FollowUpState>(Owner, FollowUpState.Section, default);
        Assert.False(state!.IsQuiet(q.Promise.Id, Today.AddDays(7)));

        Assert.True(await q.Service.DismissAsync(Owner, q.Promise.Id, default));
        Assert.Empty(await q.Service.ListAsync(Owner, null, default));
        Assert.True((await q.Settings.GetAsync<FollowUpState>(Owner, FollowUpState.Section, default))!
            .IsQuiet(q.Promise.Id, Today.AddYears(1)));
    }

    [Fact]
    public async Task Bad_snooze_lengths_and_other_owners_promises_are_refused()
    {
        var q = new Quiet();

        Assert.False(await q.Service.SnoozeAsync(Owner, q.Promise.Id, 0, default));
        Assert.False(await q.Service.SnoozeAsync(Owner, q.Promise.Id, 61, default));
        Assert.False(await q.Service.SnoozeAsync(Other, q.Promise.Id, 7, default));
        Assert.False(await q.Service.DismissAsync(Other, q.Promise.Id, default));
        Assert.Single(await q.Service.ListAsync(Owner, null, default));
    }
}
