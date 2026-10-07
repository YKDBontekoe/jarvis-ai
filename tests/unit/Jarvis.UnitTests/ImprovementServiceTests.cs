using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Improvements;
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
