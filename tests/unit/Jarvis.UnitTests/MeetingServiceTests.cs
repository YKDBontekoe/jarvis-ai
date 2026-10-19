using Jarvis.Application.Decisions;
using Jarvis.Application.Inbox;
using Jarvis.Application.Meetings;
using Jarvis.Application.People;
using Jarvis.Application.Projects;
using Jarvis.Domain.Files;
using Jarvis.Domain.Inbox;
using Jarvis.Domain.People;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class MeetingServiceTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Other = Guid.CreateVersion7();
    private static readonly DateOnly Today = new(2026, 10, 17);

    private sealed class MemoryRepository : IMeetingRepository
    {
        public readonly Dictionary<Guid, MeetingRecord> Rows = [];
        public Task<IReadOnlyList<MeetingRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MeetingRecord>>(Rows.Values.Where(r => r.OwnerId == ownerId).ToArray());
        public Task<MeetingRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.TryGetValue(id, out var row) && row.OwnerId == ownerId ? row : null);
        public Task<int> CountAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.Values.Count(r => r.OwnerId == ownerId));
        public Task AddAsync(MeetingRecord record, CancellationToken cancellationToken)
        {
            Rows[record.Id] = record;
            return Task.CompletedTask;
        }
        public Task<bool> UpdateAsync(MeetingRecord record, CancellationToken cancellationToken)
        {
            var found = Rows.TryGetValue(record.Id, out var row) && row.OwnerId == record.OwnerId;
            if (found) Rows[record.Id] = record;
            return Task.FromResult(found);
        }
        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.TryGetValue(id, out var row) && row.OwnerId == ownerId && Rows.Remove(id));
    }

    private sealed class Harness
    {
        public readonly MemoryRepository Repository = new();
        public readonly List<CommitmentDraft> Commitments = [];
        public readonly List<CreateDecisionRequest> Decisions = [];
        public readonly List<Commitment> Existing = [];

        public IMeetingService Service => new MeetingService(Repository,
            Fake<IProjectStore>.Create(
                ("GetAsync", args => (Guid)args[0]! == ProjectId && (Guid)args[1]! == Owner ? Project : null),
                ("GetContentsAsync", _ => new ProjectContents([], [], []))),
            Fake<IProjectFocusService>.Create(("DescribeAsync", _ => (ProjectNextActionView?)null)),
            Fake<ICommitmentService>.Create(
                ("TodayAsync", _ => Today),
                ("ListAsync", _ => (IReadOnlyList<Commitment>)Existing.ToArray()),
                ("CreateAsync", args =>
                {
                    var draft = (CommitmentDraft)args[1]!;
                    Commitments.Add(draft);
                    return InboxOperation<Commitment>.Ok(new Commitment(Guid.CreateVersion7(), Owner, draft.Direction!,
                        draft.Counterparty ?? "Someone", draft.Description!, draft.DueOn, "open", false, draft.Source,
                        null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
                })),
            Fake<IDecisionService>.Create(
                ("ListAsync", _ => (IReadOnlyList<DecisionView>)[]),
                ("CreateAsync", args =>
                {
                    var request = (CreateDecisionRequest)args[1]!;
                    Decisions.Add(request);
                    return new DecisionView(Guid.CreateVersion7(), request.Title, request.Context, request.Prediction,
                        request.Probability, request.ReviewOn, "open", null, null, null, DateTimeOffset.UtcNow);
                })),
            Fake<IPeopleService>.Create(("GetAsync", args =>
                (Guid)args[0]! == RosaId && (Guid)args[1]! == Owner ? Rosa : null)));
    }

    private static readonly Guid ProjectId = Guid.CreateVersion7();
    private static readonly ProjectRecord Project = new(ProjectId, "Kitchen", null, null, "blue",
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, 0, 0, "Cook for ten", "active", null);
    private static readonly Guid RosaId = Guid.CreateVersion7();
    private static readonly Person Rosa = new(RosaId, Owner, "Rosa Visser", null, null, null, null, null, null, null,
        null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static async Task<MeetingRecord> Meeting(Harness harness, params MeetingParticipant[] participants)
    {
        var created = await harness.Service.CreateAsync(Owner, new MeetingInput("Kitchen sync", null, ProjectId, "Align",
            MeetingTemplates.ProjectUpdate, participants.Length == 0 ? [new MeetingParticipant("Rosa")] : participants), default);
        Assert.True(created.Succeeded, created.Message);
        return created.Value!;
    }

    [Fact]
    public async Task A_linked_person_must_be_the_owners_and_brings_their_own_name()
    {
        var harness = new Harness();
        var ok = await harness.Service.CreateAsync(Owner, new MeetingInput("Sync", null, null, null, null,
            [new MeetingParticipant("whatever the request says", RosaId)]), default);
        Assert.Equal("Rosa Visser", ok.Value!.Document.Participants.Single().Name);

        var foreign = await harness.Service.CreateAsync(Other, new MeetingInput("Sync", null, null, null, null,
            [new MeetingParticipant("Rosa", RosaId)]), default);
        Assert.Equal("participants", foreign.Field);
        Assert.Equal("projectId", (await harness.Service.CreateAsync(Other, new MeetingInput("Sync", null, ProjectId, null, null, null), default)).Field);
        Assert.Equal("title", (await harness.Service.CreateAsync(Owner, new MeetingInput(" ", null, null, null, null, null), default)).Field);
        Assert.Equal("template", (await harness.Service.CreateAsync(Owner, new MeetingInput("Sync", null, null, null, "nonsense", null), default)).Field);
    }

    [Fact]
    public async Task Meetings_are_private_to_their_owner()
    {
        var harness = new Harness();
        var meeting = await Meeting(harness);

        Assert.Null(await harness.Service.GetAsync(meeting.Id, Other, default));
        Assert.Empty(await harness.Service.ListAsync(Other, default));
        Assert.Equal(MeetingFailure.NotFound, (await harness.Service.PrepareAsync(meeting.Id, Other, default)).Failure);
        Assert.Equal(MeetingFailure.NotFound, (await harness.Service.DebriefAsync(meeting.Id, Other, "I'll send it", default)).Failure);
        Assert.Equal(MeetingFailure.NotFound, (await harness.Service.FollowUpAsync(meeting.Id, Other, default)).Failure);
        Assert.False(await harness.Service.DeleteAsync(meeting.Id, Other, default));
    }

    [Fact]
    public async Task Prep_fills_an_empty_agenda_but_never_overwrites_the_owners_own()
    {
        var harness = new Harness();
        harness.Existing.Add(new Commitment(Guid.CreateVersion7(), Owner, CommitmentDirections.IOwe, "Rosa", "Send the quotes",
            Today.AddDays(-2), "open", false, CommitmentSources.Chat, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var meeting = await Meeting(harness);

        var first = (await harness.Service.PrepareAsync(meeting.Id, Owner, default)).Value!;
        Assert.NotEmpty(first.Document.Agenda);
        Assert.Contains(first.Document.Prep!.Items, item => item.Text.StartsWith("You owe Rosa: Send the quotes"));

        await harness.Service.UpdateAsync(meeting.Id, Owner, new MeetingEdit(null, null, null, null,
            [new AgendaItem("x", "My own point", true)]), default);
        var second = (await harness.Service.PrepareAsync(meeting.Id, Owner, default)).Value!;
        Assert.Equal(["My own point"], second.Document.Agenda.Select(item => item.Text));
    }

    [Fact]
    public async Task Reading_a_debrief_creates_no_commitment_and_no_decision()
    {
        var harness = new Harness();
        var meeting = await Meeting(harness);

        var result = await harness.Service.DebriefAsync(meeting.Id, Owner,
            "I'll send Rosa the quote by Friday. We decided to go with Bosch. Rosa will confirm the plumber.", default);

        Assert.Equal(3, result.Value!.Document.Candidates.Count);
        Assert.All(result.Value.Document.Candidates, candidate => Assert.Equal(CandidateStates.Pending, candidate.State));
        Assert.Empty(harness.Commitments);
        Assert.Empty(harness.Decisions);
    }

    [Fact]
    public async Task Accepting_a_promise_creates_one_commitment_from_the_meeting_and_accepting_again_adds_nothing()
    {
        var harness = new Harness();
        var meeting = await Meeting(harness);
        var read = await harness.Service.DebriefAsync(meeting.Id, Owner, "I'll send Rosa the quote by Friday.", default);
        var candidate = read.Value!.Document.Candidates.Single();

        var accepted = await harness.Service.AcceptAsync(meeting.Id, Owner, candidate.Id, new CandidateAcceptance(), default);
        var again = await harness.Service.AcceptAsync(meeting.Id, Owner, candidate.Id, new CandidateAcceptance(), default);

        var draft = Assert.Single(harness.Commitments);
        Assert.Equal(CommitmentDirections.IOwe, draft.Direction);
        Assert.Equal("Rosa", draft.Counterparty);
        Assert.Equal(CommitmentSources.Meeting, draft.Source);
        Assert.Equal(new DateOnly(2026, 10, 23), draft.DueOn);
        var result = accepted.Value!.Document.Candidates.Single();
        Assert.Equal(CandidateStates.Accepted, result.State);
        Assert.StartsWith("commitment:", result.ResultRef);
        Assert.Equal(result.ResultRef, again.Value!.Document.Candidates.Single().ResultRef);
    }

    [Fact]
    public async Task The_owner_can_edit_the_description_person_and_date_when_accepting()
    {
        var harness = new Harness();
        var meeting = await Meeting(harness, new MeetingParticipant("Rosa"), new MeetingParticipant("Tom"));
        var candidate = (await harness.Service.DebriefAsync(meeting.Id, Owner, "I'll send the quote next week.", default))
            .Value!.Document.Candidates.Single();
        Assert.Null(candidate.DueOn);
        Assert.Equal("next week", candidate.DueText);

        await harness.Service.AcceptAsync(meeting.Id, Owner, candidate.Id,
            new CandidateAcceptance("Send Tom the revised quote", "Tom", new DateOnly(2026, 10, 21)), default);

        var draft = Assert.Single(harness.Commitments);
        Assert.Equal("Send Tom the revised quote", draft.Description);
        Assert.Equal("Tom", draft.Counterparty);
        Assert.Equal(new DateOnly(2026, 10, 21), draft.DueOn);
    }

    [Fact]
    public async Task A_decision_is_only_logged_when_the_owner_gives_confidence_and_a_review_day()
    {
        var harness = new Harness();
        var meeting = await Meeting(harness);
        var candidate = (await harness.Service.DebriefAsync(meeting.Id, Owner, "We decided to go with the Bosch boiler.", default))
            .Value!.Document.Candidates.Single();

        var missing = await harness.Service.AcceptAsync(meeting.Id, Owner, candidate.Id, new CandidateAcceptance(), default);
        Assert.Equal(MeetingFailure.Invalid, missing.Failure);
        Assert.Empty(harness.Decisions);

        var logged = await harness.Service.AcceptAsync(meeting.Id, Owner, candidate.Id,
            new CandidateAcceptance(Probability: .7, ReviewOn: new DateOnly(2026, 12, 1)), default);
        Assert.True(logged.Succeeded);
        var decision = Assert.Single(harness.Decisions);
        Assert.Equal("Decided in Kitchen sync", decision.Title);
        Assert.Contains("Bosch", decision.Context);
        Assert.StartsWith("decision:", logged.Value!.Document.Candidates.Single().ResultRef);
    }

    [Fact]
    public async Task A_question_cannot_be_accepted_and_a_dismissal_changes_nothing_but_the_candidate()
    {
        var harness = new Harness();
        var meeting = await Meeting(harness);
        var read = await harness.Service.DebriefAsync(meeting.Id, Owner,
            "Who pays for the fitting? I'll send Rosa the quote.", default);
        var question = read.Value!.Document.Candidates.Single(c => c.Kind == CandidateKinds.Question);
        var promise = read.Value.Document.Candidates.Single(c => c.Kind == CandidateKinds.IOwe);

        Assert.Equal(MeetingFailure.Invalid,
            (await harness.Service.AcceptAsync(meeting.Id, Owner, question.Id, new CandidateAcceptance(), default)).Failure);
        var dismissed = await harness.Service.DismissAsync(meeting.Id, Owner, promise.Id, default);

        Assert.Equal(CandidateStates.Dismissed, dismissed.Value!.Document.Candidates.Single(c => c.Id == promise.Id).State);
        Assert.Empty(harness.Commitments);
        Assert.Empty(harness.Decisions);
        Assert.Equal(MeetingFailure.NotFound, (await harness.Service.DismissAsync(meeting.Id, Owner, "nope", default)).Failure);
        // Once accepted, a candidate cannot be dismissed behind the commitment's back.
        var other = (await harness.Service.DebriefAsync(meeting.Id, Owner, "I'll book the room.", default)).Value!
            .Document.Candidates.Single(c => c.Text.Contains("book"));
        await harness.Service.AcceptAsync(meeting.Id, Owner, other.Id, new CandidateAcceptance(), default);
        Assert.Equal(MeetingFailure.Conflict, (await harness.Service.DismissAsync(meeting.Id, Owner, other.Id, default)).Failure);
    }

    [Fact]
    public async Task Reading_the_debrief_again_keeps_what_was_accepted_and_dismissed()
    {
        var harness = new Harness();
        var meeting = await Meeting(harness);
        var read = await harness.Service.DebriefAsync(meeting.Id, Owner, "I'll book the room. I'll send the quote.", default);
        var room = read.Value!.Document.Candidates.Single(c => c.Text.Contains("room"));
        var quote = read.Value.Document.Candidates.Single(c => c.Text.Contains("quote"));
        await harness.Service.AcceptAsync(meeting.Id, Owner, room.Id, new CandidateAcceptance(), default);
        await harness.Service.DismissAsync(meeting.Id, Owner, quote.Id, default);

        var again = await harness.Service.DebriefAsync(meeting.Id, Owner, "I'll book the room. I'll send the quote. I'll call Rosa.", default);

        Assert.Equal(CandidateStates.Accepted, again.Value!.Document.Candidates.Single(c => c.Id == room.Id).State);
        Assert.Equal(CandidateStates.Dismissed, again.Value.Document.Candidates.Single(c => c.Id == quote.Id).State);
        Assert.Equal(3, again.Value.Document.Candidates.Count);
        Assert.Single(harness.Commitments);
    }

    [Fact]
    public async Task The_follow_up_draft_uses_only_accepted_items_and_shared_agenda_lines_and_is_never_sent()
    {
        var harness = new Harness();
        var meeting = await Meeting(harness);
        await harness.Service.UpdateAsync(meeting.Id, Owner, new MeetingEdit(null, null, null, "Rosa seemed unhappy about the budget",
            [new AgendaItem("1", "Tile choice", true), new AgendaItem("2", "Is Rosa overspending?", false)]), default);
        var read = await harness.Service.DebriefAsync(meeting.Id, Owner,
            "We decided to go with Bosch. I'll send Rosa the quote by Friday. Rosa will confirm the plumber. I'll skip the call.", default);
        foreach (var candidate in read.Value!.Document.Candidates.Where(c => c.Text.Contains("quote") || c.Text.Contains("plumber")))
            await harness.Service.AcceptAsync(meeting.Id, Owner, candidate.Id, new CandidateAcceptance(), default);

        var draft = (await harness.Service.FollowUpAsync(meeting.Id, Owner, default)).Value!;

        Assert.False(draft.Sent);
        Assert.Contains("Tile choice", draft.Body);
        Assert.Contains("I'll send Rosa the quote by Friday. (by 23 October)", draft.Body);
        Assert.Contains("Rosa will confirm the plumber.", draft.Body);
        Assert.DoesNotContain("Is Rosa overspending", draft.Body);
        Assert.DoesNotContain("unhappy", draft.Body);
        Assert.DoesNotContain("go with Bosch", draft.Body); // a decision that was not accepted
        Assert.DoesNotContain("skip the call", draft.Body);
    }

    [Fact]
    public async Task A_follow_up_with_nothing_accepted_says_so_instead_of_inventing_content()
    {
        var harness = new Harness();
        var meeting = await Meeting(harness);

        var draft = (await harness.Service.FollowUpAsync(meeting.Id, Owner, default)).Value!;

        Assert.Empty(draft.Included);
        Assert.Contains("Accept what was decided or promised first", draft.Body);
    }

    [Fact]
    public async Task Edits_are_trimmed_and_bounded_and_the_private_notes_have_a_limit()
    {
        var harness = new Harness();
        var meeting = await Meeting(harness);

        var edited = await harness.Service.UpdateAsync(meeting.Id, Owner, new MeetingEdit("  New title ", null, null, null,
            [new AgendaItem("z", "  First  ", true), new AgendaItem("y", "   ", true)]), default);
        Assert.Equal("New title", edited.Value!.Title);
        Assert.Equal(["First"], edited.Value.Document.Agenda.Select(item => item.Text));

        Assert.Equal("privateNotes", (await harness.Service.UpdateAsync(meeting.Id, Owner,
            new MeetingEdit(null, null, null, new string('x', 4001), null), default)).Field);
        Assert.Equal("agenda", (await harness.Service.UpdateAsync(meeting.Id, Owner, new MeetingEdit(null, null, null, null,
            Enumerable.Range(0, 21).Select(i => new AgendaItem($"{i}", $"Point {i}")).ToArray()), default)).Field);
        Assert.Equal("text", (await harness.Service.DebriefAsync(meeting.Id, Owner, "  ", default)).Field);
    }
}
