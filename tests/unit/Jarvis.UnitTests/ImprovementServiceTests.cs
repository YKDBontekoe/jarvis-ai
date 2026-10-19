using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Improvements;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Application.Skills;
using Jarvis.Domain.Improvements;
using Jarvis.Domain.Memory;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ImprovementServiceTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000a1a1");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000a1a2");
    private static readonly Guid Profile = Guid.Parse("01996b8c-6000-7000-8000-00000000a1a3");
    private static readonly Guid Source = Guid.Parse("01996b8c-6000-7000-8000-00000000a1a4");
    private static readonly DateTimeOffset Start = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    private static MemoryCandidate Memory(string content = "The user is allergic to peanuts.", float confidence = 0.9f,
        Guid? profile = null) =>
        new("fact", content, 0.8f, confidence, profile ?? Profile, Source, "Noticed in your recent chats.");

    [Fact]
    public async Task A_clear_memory_is_saved_under_its_profile_and_recorded_so_it_can_be_undone()
    {
        var world = new World();

        var outcome = await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(), autoApply: true, default);

        Assert.Equal(MemoryOutcome.Saved, outcome);
        var created = Assert.Single(world.Created);
        Assert.Equal(("fact", "The user is allergic to peanuts.", Profile, Source), (created.Kind, created.Content,
            created.ProfileId, created.SourceId));
        var view = Assert.Single(await world.Service().ListAsync(Owner, default));
        Assert.Equal(ImprovementStatuses.Applied, view.Status);
        Assert.True(view.CanUndo);
        Assert.Empty(world.Notifications.Created);
    }

    [Fact]
    public async Task A_middling_memory_waits_for_review_and_is_announced_without_its_text()
    {
        var world = new World();

        var outcome = await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(confidence: 0.7f), true, default);

        Assert.Equal(MemoryOutcome.Proposed, outcome);
        Assert.Empty(world.Created);
        Assert.Equal(ImprovementStatuses.Pending, Assert.Single(await world.Service().ListAsync(Owner, default)).Status);
        var note = Assert.Single(world.Notifications.Created);
        Assert.Equal(ImprovementRules.NotificationType, note.Type);
        Assert.DoesNotContain("peanuts", note.Title + note.Body);
    }

    [Fact]
    public async Task With_auto_apply_off_even_a_certain_memory_waits()
    {
        var world = new World();

        var outcome = await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(confidence: 0.99f), false, default);

        Assert.Equal(MemoryOutcome.Proposed, outcome);
        Assert.Empty(world.Created);
    }

    [Theory]
    [InlineData(0.59f)]
    [InlineData(0.1f)]
    public async Task An_unsure_memory_is_not_even_proposed(float confidence)
    {
        var world = new World();

        Assert.Equal(MemoryOutcome.Dropped,
            await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(confidence: confidence), true, default));
        Assert.Empty(await world.Repository.ListAsync(Owner, default));
    }

    [Fact]
    public async Task Undoing_a_saved_memory_deletes_it_and_it_is_never_saved_again()
    {
        var world = new World();
        await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(), true, default);
        var id = Assert.Single(await world.Repository.ListAsync(Owner, default)).Id;

        Assert.True(await world.Service().UndoAsync(id, Owner, default));

        Assert.Single(world.Deleted);
        Assert.Equal(ImprovementStatuses.Undone, (await world.Repository.GetAsync(id, Owner, default))!.Status);
        Assert.Equal(MemoryOutcome.Dropped,
            await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(), true, default));
        Assert.Single(world.Created);
        Assert.Empty(await world.Service().ListAsync(Owner, default));
    }

    [Fact]
    public async Task A_dismissed_memory_is_not_offered_or_saved_again_and_same_wording_in_other_case_counts_as_same()
    {
        var world = new World();
        await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(confidence: 0.7f), true, default);
        var id = Assert.Single(await world.Repository.ListAsync(Owner, default)).Id;

        Assert.True(await world.Service().DismissAsync(id, Owner, default));

        Assert.Equal(MemoryOutcome.Dropped, await world.Service().SaveOrProposeMemoryAsync(Owner,
            Memory("the user is  ALLERGIC to peanuts.", 0.95f), true, default));
        Assert.Empty(world.Created);
    }

    [Fact]
    public async Task A_waiting_memory_that_becomes_certain_is_saved_once_and_the_row_is_reused()
    {
        var world = new World();
        await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(confidence: 0.7f), true, default);

        var outcome = await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(confidence: 0.95f), true, default);

        Assert.Equal(MemoryOutcome.Saved, outcome);
        Assert.Single(await world.Repository.ListAsync(Owner, default));
        Assert.Equal(ImprovementStatuses.Applied, (await world.Repository.ListAsync(Owner, default))[0].Status);
        Assert.Single(world.Created);
    }

    [Fact]
    public async Task Accepting_a_waiting_memory_saves_it_once_even_if_accepted_twice()
    {
        var world = new World();
        await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(confidence: 0.7f), true, default);
        var id = Assert.Single(await world.Repository.ListAsync(Owner, default)).Id;

        var first = await world.Service().AcceptAsync(id, Owner, default);
        var second = await world.Service().AcceptAsync(id, Owner, default);

        Assert.Equal(ImprovementStatuses.Accepted, first!.Status);
        Assert.Equal(first.Status, second!.Status);
        Assert.Single(world.Created);
        Assert.False(await world.Service().DismissAsync(id, Owner, default));
    }

    [Fact]
    public async Task Proposals_belong_to_one_owner()
    {
        var world = new World();
        await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(confidence: 0.7f), true, default);
        var id = Assert.Single(await world.Repository.ListAsync(Owner, default)).Id;

        Assert.Null(await world.Service().AcceptAsync(id, Other, default));
        Assert.False(await world.Service().DismissAsync(id, Other, default));
        Assert.False(await world.Service().UndoAsync(id, Other, default));
        Assert.Empty(await world.Service().ListAsync(Other, default));
        Assert.Empty(world.Created);
    }

    [Fact]
    public async Task A_skill_proposal_is_validated_saved_active_and_undo_turns_it_off()
    {
        var world = new World();
        var payload = JsonSerializer.Serialize(new SkillPayload("inbox-triage",
            "Triage the inbox into reply, delegate and archive.", "1. Search mail.\n2. Group by sender."),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await world.Service().SyncAsync(Owner, "seq:", [Skill("seq:a>b", payload)], default);
        var id = Assert.Single(await world.Repository.ListAsync(Owner, default)).Id;

        var accepted = await world.Service().AcceptAsync(id, Owner, default);

        Assert.Equal(ImprovementStatuses.Accepted, accepted!.Status);
        var skill = Assert.Single(world.Skills.Items);
        Assert.Equal((SkillSources.Learned, SkillStatuses.Active), (skill.Source, skill.Status));
        Assert.True(await world.Service().UndoAsync(id, Owner, default));
        Assert.Equal(SkillStatuses.Disabled, world.Skills.Items.Single().Status);
    }

    [Fact]
    public async Task A_skill_proposal_that_no_longer_validates_cannot_be_accepted()
    {
        var world = new World();
        var payload = JsonSerializer.Serialize(new SkillPayload("x", "short", "tiny"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await world.Service().SyncAsync(Owner, "seq:", [Skill("seq:bad", payload)], default);
        var id = Assert.Single(await world.Repository.ListAsync(Owner, default)).Id;

        Assert.Null(await world.Service().AcceptAsync(id, Owner, default));
        Assert.Empty(world.Skills.Items);
        Assert.Equal(ImprovementStatuses.Pending, (await world.Repository.GetAsync(id, Owner, default))!.Status);
    }

    [Fact]
    public async Task A_review_to_disable_a_skill_is_reversible_and_a_note_changes_nothing()
    {
        var world = new World();
        var skill = (await world.Skills.UpsertAsync(Owner, new SkillDraft("meal-plan",
            "Plan weekly meals around the diet.", "1. Check preferences.\n2. Propose dinners."), SkillSources.Learned,
            SkillStatuses.Active, true, null, default))!;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await world.Service().SyncAsync(Owner, "review:", [
            Review("review:skill:meal-plan", JsonSerializer.Serialize(new ReviewPayload(ReviewActions.DisableSkill, "meal-plan"), json)),
            Review("review:tool:search", JsonSerializer.Serialize(new ReviewPayload(ReviewActions.Note, "search"), json))
        ], default);
        var all = await world.Repository.ListAsync(Owner, default);
        var disable = all.Single(item => item.Fingerprint == "review:skill:meal-plan");
        var note = all.Single(item => item.Fingerprint == "review:tool:search");

        Assert.Equal(ImprovementStatuses.Accepted, (await world.Service().AcceptAsync(disable.Id, Owner, default))!.Status);
        Assert.Equal(SkillStatuses.Disabled, world.Skills.Items.Single().Status);
        Assert.True(await world.Service().UndoAsync(disable.Id, Owner, default));
        Assert.Equal(SkillStatuses.Active, world.Skills.Items.Single().Status);

        Assert.Equal(ImprovementStatuses.Accepted, (await world.Service().AcceptAsync(note.Id, Owner, default))!.Status);
        Assert.Equal(skill.Id, world.Skills.Items.Single().Id);
    }

    [Fact]
    public async Task Sync_adds_refreshes_and_drops_pending_rows_but_leaves_decisions_alone()
    {
        var world = new World();
        var service = world.Service();
        Assert.Equal(2, await service.SyncAsync(Owner, "seq:", [Skill("seq:a", "{}", "A"), Skill("seq:b", "{}", "B")], default));
        var b = (await world.Repository.FindByFingerprintAsync(Owner, "seq:b", default))!;
        await service.DismissAsync(b.Id, Owner, default);

        // a is refreshed in place, b stays dismissed and is not re-added, c is new.
        var added = await service.SyncAsync(Owner, "seq:",
            [Skill("seq:a", "{}", "A again", confidence: 0.9), Skill("seq:b", "{}", "B"), Skill("seq:c", "{}", "C")], default);

        Assert.Equal(1, added);
        var rows = await world.Repository.ListAsync(Owner, default);
        Assert.Equal("A again", rows.Single(item => item.Fingerprint == "seq:a").Title);
        Assert.Equal(ImprovementStatuses.Dismissed, rows.Single(item => item.Fingerprint == "seq:b").Status);

        // c vanishes from the data: pending rows follow it, the dismissal stays.
        await service.SyncAsync(Owner, "seq:", [Skill("seq:a", "{}", "A again")], default);
        rows = await world.Repository.ListAsync(Owner, default);
        Assert.DoesNotContain(rows, item => item.Fingerprint == "seq:c");
        Assert.Contains(rows, item => item.Fingerprint == "seq:b");
    }

    [Fact]
    public async Task Sync_only_touches_its_own_prefix_and_valid_candidates()
    {
        var world = new World();
        await world.Service().SyncAsync(Owner, "seq:", [Skill("seq:a", "{}")], default);

        await world.Service().SyncAsync(Owner, "review:", [
            Review("review:x", "{}"),
            Review("seq:wrong-prefix", "{}"),
            new ProposalCandidate("bogus", "review:y", "Bad kind", "e", 0.5, "{}"),
            Review("review:empty", "")
        ], default);

        var fingerprints = (await world.Repository.ListAsync(Owner, default)).Select(item => item.Fingerprint).Order();
        Assert.Equal(["review:x", "seq:a"], fingerprints);
    }

    [Fact]
    public async Task Pending_proposals_are_capped()
    {
        var world = new World();
        var many = Enumerable.Range(0, ImprovementRules.MaxPending + 10)
            .Select(i => Review($"review:{i}", "{}")).ToArray();

        var added = await world.Service().SyncAsync(Owner, "review:", many, default);

        Assert.Equal(ImprovementRules.MaxPending, added);
    }

    [Fact]
    public async Task Pushes_for_new_suggestions_are_at_most_weekly()
    {
        var world = new World();
        await world.Service().SyncAsync(Owner, "seq:", [Skill("seq:a", "{}")], default);
        world.Now = Start.AddDays(2);
        await world.Service().SyncAsync(Owner, "seq:", [Skill("seq:a", "{}"), Skill("seq:b", "{}")], default);
        Assert.Single(world.Notifications.Created);

        world.Now = Start.AddDays(8);
        await world.Service().SyncAsync(Owner, "seq:", [Skill("seq:a", "{}"), Skill("seq:b", "{}"), Skill("seq:c", "{}")], default);
        Assert.Equal(2, world.Notifications.Created.Count);
    }

    [Fact]
    public async Task The_list_shows_pending_first_and_only_recent_undoable_changes()
    {
        var world = new World();
        await world.Service().SaveOrProposeMemoryAsync(Owner, Memory("Old fact about the user.", 0.95f), true, default);
        world.Now = Start.AddDays(ImprovementRules.UndoWindow.Days + 5);
        await world.Service().SaveOrProposeMemoryAsync(Owner, Memory("A new waiting fact.", 0.7f), true, default);

        var listed = await world.Service().ListAsync(Owner, default);

        var only = Assert.Single(listed);
        Assert.Equal(ImprovementStatuses.Pending, only.Status);
    }

    [Fact]
    public async Task Audit_records_ids_and_kind_but_never_the_proposal_text()
    {
        var world = new World();

        await world.Service().SaveOrProposeMemoryAsync(Owner, Memory(), true, default);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal("improvement.applied", entry.Action);
        Assert.DoesNotContain("peanuts", entry.Metadata);
        Assert.Contains("memory", entry.Metadata);
    }

    private static ProposalCandidate Lesson(string tool = "SearchMail", double confidence = 0.8,
        string text = "Check the inputs first.") =>
        new(ImprovementKinds.Lesson, LessonRules.Fingerprint(tool, Start), $"Lesson: {text}", "It went wrong twice.",
            confidence, JsonSerializer.Serialize(new LessonPayload(text, [tool]),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Fact]
    public async Task A_confident_lesson_is_in_force_at_once_and_audited_without_its_text()
    {
        var world = new World();

        Assert.True(await world.Service().ProposeLessonAsync(Owner, Lesson(), autoApply: true, default));

        var lesson = Assert.Single(await world.Service().LessonsInForceAsync(Owner, default));
        Assert.Equal("Check the inputs first.", lesson.Payload.Text);
        Assert.Equal(["SearchMail"], lesson.Payload.Tools);
        Assert.Empty(world.Notifications.Created);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal("improvement.applied", entry.Action);
        Assert.DoesNotContain("Check the inputs", entry.Metadata);
    }

    [Fact]
    public async Task A_lesson_waits_for_the_owner_when_auto_apply_is_off_or_confidence_is_low()
    {
        var world = new World();

        Assert.True(await world.Service().ProposeLessonAsync(Owner, Lesson("A"), autoApply: false, default));
        Assert.True(await world.Service().ProposeLessonAsync(Owner, Lesson("B", confidence: 0.7), autoApply: true, default));

        Assert.Empty(await world.Service().LessonsInForceAsync(Owner, default));
        var pending = (await world.Service().ListAsync(Owner, default)).Where(v => v.Kind == ImprovementKinds.Lesson).ToArray();
        Assert.Equal(2, pending.Length);
        Assert.All(pending, view => Assert.Equal(ImprovementStatuses.Pending, view.Status));

        var accepted = await world.Service().AcceptAsync(pending[0].Id, Owner, default);

        Assert.Equal(ImprovementStatuses.Accepted, accepted!.Status);
        Assert.Single(await world.Service().LessonsInForceAsync(Owner, default));
    }

    [Fact]
    public async Task A_lesson_the_owner_undid_or_declined_is_not_proposed_again()
    {
        var world = new World();
        await world.Service().ProposeLessonAsync(Owner, Lesson(), true, default);
        var view = Assert.Single(await world.Service().ListAsync(Owner, default));

        Assert.True(await world.Service().UndoAsync(view.Id, Owner, default));

        Assert.Empty(await world.Service().LessonsInForceAsync(Owner, default));
        Assert.False(await world.Service().ProposeLessonAsync(Owner, Lesson(), true, default));
    }

    [Fact]
    public async Task Only_a_lesson_fingerprint_and_kind_are_accepted_and_the_cap_on_lessons_in_force_holds()
    {
        var world = new World();
        Assert.False(await world.Service().ProposeLessonAsync(Owner,
            Lesson() with { Fingerprint = "review:tool:x" }, true, default));
        Assert.False(await world.Service().ProposeLessonAsync(Owner, Lesson() with { Kind = ImprovementKinds.Review }, true, default));

        for (var i = 0; i < LessonRules.MaxInForce + 2; i++)
            await world.Service().ProposeLessonAsync(Owner, Lesson($"Tool{i}"), true, default);

        Assert.Equal(LessonRules.MaxInForce, (await world.Service().LessonsInForceAsync(Owner, default)).Count);
    }

    [Fact]
    public async Task Counts_add_up_and_a_lesson_that_hurt_more_than_it_helped_is_switched_off()
    {
        var world = new World();
        await world.Service().ProposeLessonAsync(Owner, Lesson(), true, default);
        var id = Assert.Single(await world.Service().LessonsInForceAsync(Owner, default)).ProposalId;

        var first = await world.Service().ApplyLessonTalliesAsync(Owner,
            new Dictionary<Guid, LessonTally> { [id] = new(2, 2, Start, Start) }, Start, default);
        Assert.Equal(0, first);
        var counted = Assert.Single(await world.Service().LessonsInForceAsync(Owner, default)).Payload;
        Assert.Equal((2, 2, Start), (counted.Helpful, counted.Harmful, counted.CountedThrough));

        var second = await world.Service().ApplyLessonTalliesAsync(Owner,
            new Dictionary<Guid, LessonTally> { [id] = new(0, 3, Start, Start.AddDays(1)) }, Start.AddDays(1), default);

        Assert.Equal(1, second);
        Assert.Empty(await world.Service().LessonsInForceAsync(Owner, default));
        Assert.Contains(world.Audit.Entries, entry => entry.Action == "improvement.lesson_disabled");
    }

    [Fact]
    public async Task A_lesson_not_shown_for_two_months_is_retired()
    {
        var world = new World();
        await world.Service().ProposeLessonAsync(Owner, Lesson(), true, default);
        var id = Assert.Single(await world.Service().LessonsInForceAsync(Owner, default)).ProposalId;
        var later = Start.AddDays(61);

        var retired = await world.Service().ApplyLessonTalliesAsync(Owner,
            new Dictionary<Guid, LessonTally> { [id] = new(0, 0, null, later) }, later, default);

        Assert.Equal(1, retired);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == "improvement.lesson_retired");
    }

    // ---- the nightly miner, end to end over the real service ----

    private static TurnTraceRecord FailingTurn(string tool, double hoursAgo = 24, Guid[]? lessons = null,
        Guid? message = null) =>
        new(Guid.NewGuid(), Owner, Guid.NewGuid(), message, null, "interactive", [], [],
            [new TurnToolCall(tool, ToolOutcomes.Failed, 40)], 200, "completed", Start.AddHours(-hoursAgo), null, lessons);

    private static ImprovementMiner Miner(World world, StubLearningStore store, StubLessonDrafter drafter) =>
        new(store, world.Repository, world.Service(), world.Skills, Fake<IMemoryService>.Create(),
            Fake<ISkillDrafter>.Create(), world.Settings, new FixedTime(() => world.Now), drafter);

    [Fact]
    public async Task The_miner_drafts_a_lesson_for_a_failing_tool_and_does_not_repeat_it_that_month()
    {
        var world = new World();
        var store = new StubLearningStore { Traces = [FailingTurn("SearchMail"), FailingTurn("SearchMail", 30), FailingTurn("SearchMail", 40)] };
        var drafter = new StubLessonDrafter();

        var first = await Miner(world, store, drafter).RefreshAsync(Owner, default);
        var second = await Miner(world, store, drafter).RefreshAsync(Owner, default);

        Assert.Equal(1, first.Lessons);
        Assert.Equal(0, second.Lessons);
        Assert.Equal(["SearchMail"], drafter.Asked);
        var lesson = Assert.Single(await world.Service().LessonsInForceAsync(Owner, default));
        Assert.Equal(["SearchMail"], lesson.Payload.Tools);
    }

    [Fact]
    public async Task Two_incidents_make_a_pending_lesson_not_one_in_force()
    {
        var world = new World();
        var store = new StubLearningStore { Traces = [FailingTurn("SearchMail"), FailingTurn("SearchMail", 30)] };

        await Miner(world, store, new StubLessonDrafter()).RefreshAsync(Owner, default);

        Assert.Empty(await world.Service().LessonsInForceAsync(Owner, default));
        Assert.Equal(ImprovementStatuses.Pending,
            Assert.Single(await world.Service().ListAsync(Owner, default), v => v.Kind == ImprovementKinds.Lesson).Status);
    }

    [Fact]
    public async Task A_tool_that_already_has_a_lesson_in_force_is_not_drafted_again()
    {
        var world = new World();
        await world.Service().ProposeLessonAsync(Owner, Lesson("SearchMail") with
        {
            Fingerprint = LessonRules.Fingerprint("SearchMail", Start.AddMonths(-2))
        }, true, default);
        var store = new StubLearningStore { Traces = [FailingTurn("SearchMail"), FailingTurn("SearchMail", 30)] };
        var drafter = new StubLessonDrafter();

        var result = await Miner(world, store, drafter).RefreshAsync(Owner, default);

        Assert.Equal(0, result.Lessons);
        Assert.Empty(drafter.Asked);
    }

    [Fact]
    public async Task A_night_drafts_at_most_three_lessons()
    {
        var world = new World();
        var store = new StubLearningStore
        {
            Traces = new[] { "A", "B", "C", "D" }.SelectMany(tool => new[] { FailingTurn(tool), FailingTurn(tool, 30) }).ToList()
        };
        var drafter = new StubLessonDrafter();

        var result = await Miner(world, store, drafter).RefreshAsync(Owner, default);

        Assert.Equal(LessonRules.MaxDraftsPerRun, result.Lessons);
        Assert.Equal(LessonRules.MaxDraftsPerRun, drafter.Asked.Count);
    }

    [Fact]
    public async Task Last_nights_counts_are_applied_before_new_lessons_are_weighed()
    {
        var world = new World();
        await world.Service().ProposeLessonAsync(Owner, Lesson("SearchMail") with
        {
            Fingerprint = LessonRules.Fingerprint("SearchMail", Start.AddMonths(-2))
        }, true, default);
        var id = Assert.Single(await world.Service().LessonsInForceAsync(Owner, default)).ProposalId;
        var shown = Enumerable.Range(0, 3).Select(i => FailingTurn("SearchMail", 24 + i, [id], Guid.NewGuid())).ToList();
        var store = new StubLearningStore
        {
            Traces = shown,
            Signals = shown.Select(turn => new LearningSignalRecord(Guid.NewGuid(), Owner, LearningSignalKinds.ThumbsDown,
                turn.ConversationId, turn.MessageId, null, null, null, null, Start.AddHours(-20))).ToList()
        };

        await Miner(world, store, new StubLessonDrafter()).RefreshAsync(Owner, default);

        Assert.DoesNotContain(await world.Service().LessonsInForceAsync(Owner, default), l => l.ProposalId == id);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == "improvement.lesson_disabled");
    }

    [Fact]
    public async Task The_miner_adds_last_nights_skill_runs_to_the_skills_current_version()
    {
        var world = new World();
        var skill = (await world.Skills.UpsertAsync(Owner,
            new SkillDraft("trip-planning", "Use when planning trips.", "1. Ask for dates."), SkillSources.Learned,
            SkillStatuses.Active, true, null, default))!;
        TurnTraceRecord Run(double hoursAgo, string outcome) =>
            new(Guid.NewGuid(), Owner, Guid.NewGuid(), null, null, "interactive", [], ["trip-planning"],
                [new TurnToolCall("SearchMemory", ToolOutcomes.Completed, 30)], 100, outcome, Start.AddHours(-hoursAgo));
        var store = new StubLearningStore { Traces = [Run(24, "completed"), Run(30, "completed"), Run(26, "failed")] };

        await Miner(world, store, new StubLessonDrafter()).RefreshAsync(Owner, default);
        await Miner(world, store, new StubLessonDrafter()).RefreshAsync(Owner, default);

        var counted = world.Skills.Items.Single(item => item.Id == skill.Id);
        Assert.Equal((3, 2), (counted.OutcomeRuns, counted.OutcomeSuccesses));
    }

    // ---- skill refinements ----

    private static async Task<SkillRecord> BadSkill(World world, string name = "trip-planning", int runs = 6,
        int successes = 2, bool locked = false)
    {
        var saved = (await world.Skills.UpsertAsync(Owner,
            new SkillDraft(name, "Use when planning trips.", "1. Ask for dates and budget."), SkillSources.Learned,
            SkillStatuses.Active, true, null, default))!;
        var index = world.Skills.Items.FindIndex(item => item.Id == saved.Id);
        world.Skills.Items[index] = saved with { OutcomeRuns = runs, OutcomeSuccesses = successes, IsLocked = locked };
        return world.Skills.Items[index];
    }

    private static ImprovementMiner Miner(World world, StubLearningStore store, StubSkillRefiner refiner) =>
        new(store, world.Repository, world.Service(), world.Skills, Fake<IMemoryService>.Create(),
            Fake<ISkillDrafter>.Create(), world.Settings, new FixedTime(() => world.Now), null, refiner);

    [Fact]
    public async Task A_skill_that_mostly_goes_badly_gets_a_pending_redraft_that_never_applies_by_itself()
    {
        var world = new World();
        var skill = await BadSkill(world);
        var store = new StubLearningStore { Traces = [FailingTurnWithSkill("trip-planning", "SearchMail")] };
        var refiner = new StubSkillRefiner();

        var result = await Miner(world, store, refiner).RefreshAsync(Owner, default);

        Assert.Equal(1, result.Refinements);
        var proposal = Assert.Single(await world.Service().ListAsync(Owner, default), v => v.Kind == ImprovementKinds.SkillRefine);
        Assert.Equal(ImprovementStatuses.Pending, proposal.Status);
        Assert.Contains("2 of 7", proposal.Evidence); // the seeded failing turn was counted first
        Assert.Contains("SearchMail", proposal.Evidence);
        Assert.Equal("1. Ask for dates and budget.", world.Skills.Items.Single(item => item.Id == skill.Id).Instructions);
        Assert.Equal(skill.Version, refiner.AskedVersions.Single());
    }

    [Theory]
    [InlineData(6, 5, false)]
    [InlineData(2, 0, false)]
    [InlineData(6, 2, true)]
    public async Task Only_skills_with_enough_runs_and_mostly_bad_ones_are_redrafted(int runs, int successes, bool offered)
    {
        var world = new World();
        await BadSkill(world, runs: runs, successes: successes);

        var result = await Miner(world, new StubLearningStore(), new StubSkillRefiner()).RefreshAsync(Owner, default);

        Assert.Equal(offered ? 1 : 0, result.Refinements);
    }

    [Fact]
    public async Task Locked_skills_are_not_redrafted_and_a_decided_version_is_not_offered_again()
    {
        var world = new World();
        await BadSkill(world, "owner-written", locked: true);
        await BadSkill(world, "agent-written");
        var refiner = new StubSkillRefiner();
        var miner = Miner(world, new StubLearningStore(), refiner);

        await miner.RefreshAsync(Owner, default);
        var proposal = Assert.Single(await world.Service().ListAsync(Owner, default), v => v.Kind == ImprovementKinds.SkillRefine);
        await world.Service().DismissAsync(proposal.Id, Owner, default);
        await miner.RefreshAsync(Owner, default);

        Assert.Equal("agent-written", Assert.Single(refiner.Asked));
        Assert.DoesNotContain(await world.Service().ListAsync(Owner, default), v => v.Kind == ImprovementKinds.SkillRefine && v.Status == ImprovementStatuses.Pending);
    }

    [Fact]
    public async Task At_most_two_skills_are_redrafted_a_night()
    {
        var world = new World();
        foreach (var name in new[] { "skill-a", "skill-b", "skill-c" }) await BadSkill(world, name);
        var refiner = new StubSkillRefiner();

        var result = await Miner(world, new StubLearningStore(), refiner).RefreshAsync(Owner, default);

        Assert.Equal(SkillRefineRules.MaxDraftsPerRun, result.Refinements);
        Assert.Equal(SkillRefineRules.MaxDraftsPerRun, refiner.Asked.Count);
    }

    [Fact]
    public async Task Accepting_a_redraft_rewrites_the_skill_and_undo_puts_the_old_text_back()
    {
        var world = new World();
        var skill = await BadSkill(world);
        await Miner(world, new StubLearningStore(), new StubSkillRefiner()).RefreshAsync(Owner, default);
        var proposal = Assert.Single(await world.Service().ListAsync(Owner, default), v => v.Kind == ImprovementKinds.SkillRefine);

        var accepted = await world.Service().AcceptAsync(proposal.Id, Owner, default);

        Assert.Equal(ImprovementStatuses.Accepted, accepted!.Status);
        var rewritten = world.Skills.Items.Single(item => item.Id == skill.Id);
        Assert.Contains("Check the inputs first", rewritten.Instructions);
        Assert.Equal((skill.Version + 1, SkillStatuses.Active, 0), (rewritten.Version, rewritten.Status, rewritten.OutcomeRuns));

        Assert.True(await world.Service().UndoAsync(proposal.Id, Owner, default));

        var restored = world.Skills.Items.Single(item => item.Id == skill.Id);
        Assert.Equal("1. Ask for dates and budget.", restored.Instructions);
        Assert.Equal(skill.Version + 2, restored.Version);
    }

    [Fact]
    public async Task A_redraft_written_for_an_older_version_is_refused()
    {
        var world = new World();
        var skill = await BadSkill(world);
        await Miner(world, new StubLearningStore(), new StubSkillRefiner()).RefreshAsync(Owner, default);
        var proposal = Assert.Single(await world.Service().ListAsync(Owner, default), v => v.Kind == ImprovementKinds.SkillRefine);
        await world.Skills.UpsertAsync(Owner, new SkillDraft(skill.Name, "Use when planning trips.", "1. Newer text by the owner."),
            SkillSources.User, SkillStatuses.Active, false, null, default);

        Assert.Null(await world.Service().AcceptAsync(proposal.Id, Owner, default));

        Assert.Equal("1. Newer text by the owner.", world.Skills.Items.Single(item => item.Id == skill.Id).Instructions);
    }

    private static TurnTraceRecord FailingTurnWithSkill(string skill, string tool) =>
        new(Guid.NewGuid(), Owner, Guid.NewGuid(), null, null, "interactive", [], [skill],
            [new TurnToolCall(tool, ToolOutcomes.Failed, 40)], 200, "completed", Start.AddHours(-30));

    private sealed class StubSkillRefiner : ISkillRefiner
    {
        public List<string> Asked { get; } = [];
        public List<int> AskedVersions { get; } = [];

        public Task<SkillDraft?> RefineAsync(Guid ownerId, SkillRecord skill, SkillRefineEvidence evidence,
            CancellationToken cancellationToken)
        {
            Asked.Add(skill.Name);
            AskedVersions.Add(skill.Version);
            return Task.FromResult<SkillDraft?>(new SkillDraft(skill.Name, skill.Description,
                "1. Check the inputs first.\n2. Ask for dates and budget."));
        }
    }

    [Fact]
    public async Task With_proposals_switched_off_the_miner_does_nothing()
    {
        var world = new World();
        await world.Settings.SaveAsync(Owner, Jarvis.Application.Settings.SettingsSections.Learning,
            Jarvis.Application.Settings.LearningSettings.Default with { ProposeImprovements = false }, default);
        var store = new StubLearningStore { Traces = [FailingTurn("SearchMail"), FailingTurn("SearchMail", 30)] };
        var drafter = new StubLessonDrafter();

        var result = await Miner(world, store, drafter).RefreshAsync(Owner, default);

        Assert.Equal(new MiningResult(0, 0, 0), result);
        Assert.Empty(drafter.Asked);
    }

    private sealed class StubLessonDrafter : ILessonDrafter
    {
        public List<string> Asked { get; } = [];

        public Task<string?> DraftAsync(Guid ownerId, LessonFinding finding, CancellationToken cancellationToken)
        {
            Asked.Add(finding.Tool);
            return Task.FromResult<string?>($"Check the inputs before calling {finding.Tool}.");
        }
    }

    private sealed class StubLearningStore : Jarvis.Application.Learning.ILearningStore
    {
        public List<TurnTraceRecord> Traces { get; set; } = [];
        public List<LearningSignalRecord> Signals { get; set; } = [];

        public Task AddSignalAsync(LearningSignalRecord signal, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddTraceAsync(TurnTraceRecord trace, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<LearningSignalRecord>> ListSignalsAsync(Guid ownerId, DateTimeOffset since,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LearningSignalRecord>>(Signals);

        public Task<IReadOnlyList<TurnTraceRecord>> ListTracesAsync(Guid ownerId, DateTimeOffset since, int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TurnTraceRecord>>(Traces);

        public Task<int> PruneAsync(Guid ownerId, DateTimeOffset tracesBefore, DateTimeOffset signalsBefore,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private static ProposalCandidate Skill(string fingerprint, string payload, string title = "Skill idea",
        double confidence = 0.6) => new(ImprovementKinds.Skill, fingerprint, title, "Seen 3 times.", confidence, payload);

    private static ProposalCandidate Review(string fingerprint, string payload) =>
        new(ImprovementKinds.Review, fingerprint, "Look at this", "Evidence.", 0.5, payload);

    private sealed class World
    {
        public DateTimeOffset Now { get; set; } = Start;
        public InMemoryImprovementRepository Repository { get; } = new();
        public InMemorySkillRepository Skills { get; } = new();
        public RecordingNotifications Notifications { get; } = new();
        public InMemorySettingsStore Settings { get; } = new();
        public RecordingAuditStore Audit { get; } = new();
        public List<MemoryRecord> Created { get; } = [];
        public List<Guid> Deleted { get; } = [];

        public ImprovementService Service() => new(Repository,
            Fake<IMemoryService>.Create(
                ("CreateAsync", args =>
                {
                    var record = new MemoryRecord(Guid.NewGuid(), (Guid)args[0]!, (string)args[1]!, (string)args[2]!,
                        (float)args[3]!, (float)args[4]!, (string)args[8]!, (Guid?)args[9], Now, Now, null, false,
                        ProfileId: (Guid?)args[10]);
                    Created.Add(record);
                    return record;
                }),
                ("DeleteAsync", args =>
                {
                    Deleted.Add((Guid)args[0]!);
                    return Task.CompletedTask;
                })),
            Skills, Notifications, Settings, Audit, new FixedTime(() => Now));
    }

    private sealed class FixedTime(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }

    private sealed class RecordingAuditStore : IAuditEventStore
    {
        public List<(string Action, string Metadata)> Entries { get; } = [];

        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass,
            bool success, Guid? approvalId, string? metadataJson, CancellationToken cancellationToken,
            Guid? agentRunId = null)
        {
            Entries.Add((action, metadataJson ?? string.Empty));
            return Task.FromResult(new AuditEventRecord(Guid.NewGuid(), agentRunId, tool, action, riskClass,
                approvalId, DateTimeOffset.UtcNow, success, metadataJson));
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditEventRecord>>([]);
    }
}
