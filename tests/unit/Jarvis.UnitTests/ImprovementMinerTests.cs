using System.Text.Json;
using Jarvis.Application.Improvements;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;
using Jarvis.Agents.Improvements;
using Jarvis.Domain.Memory;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ImprovementMinerTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000b1a1");
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 3, 0, 0, TimeSpan.Zero);

    private static TurnTraceRecord Trace(Guid conversation, string[] tools, int daysAgo = 1, string outcome = "completed",
        Guid? messageId = null, Guid[]? memories = null, string[]? skills = null, string toolOutcome = "completed") =>
        new(Guid.NewGuid(), Owner, conversation, messageId, null, TurnKinds.Interactive, memories ?? [], skills ?? [],
            tools.Select(tool => new TurnToolCall(tool, toolOutcome, 100)).ToArray(), 1_000, outcome,
            Now.AddDays(-daysAgo));

    private static TurnTraceRecord Failing(string tool, bool failed, int daysAgo = 1) =>
        new(Guid.NewGuid(), Owner, Guid.NewGuid(), null, null, TurnKinds.Interactive, [], [],
            [new TurnToolCall(tool, failed ? ToolOutcomes.Failed : ToolOutcomes.Completed, 50)], 100, "completed",
            Now.AddDays(-daysAgo));

    private static LearningSignalRecord Thumb(Guid messageId, string kind, int daysAgo = 1) =>
        new(Guid.NewGuid(), Owner, kind, Guid.NewGuid(), messageId, null, null, null, null, Now.AddDays(-daysAgo));

    [Fact]
    public void A_sequence_used_three_times_across_two_conversations_is_a_skill_candidate()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var traces = new[]
        {
            Trace(a, ["SearchMail", "GetCalendar", "CreateTask"]),
            Trace(a, ["SearchMail", "GetCalendar", "CreateTask"], 2),
            Trace(b, ["SearchMail", "GetCalendar", "CreateTask"], 3)
        };

        var finding = Assert.Single(SkillMinerEngine.Mine(traces, Now));

        Assert.Equal(["SearchMail", "GetCalendar", "CreateTask"], finding.Tools);
        Assert.Equal((3, 2), (finding.Runs, finding.Conversations));
        Assert.Equal("seq:SearchMail>GetCalendar>CreateTask", finding.Fingerprint);
    }

    [Fact]
    public void One_conversation_too_few_runs_old_runs_and_failed_runs_do_not_count()
    {
        var a = Guid.NewGuid();
        var oneConversation = Enumerable.Range(1, 4).Select(day => Trace(a, ["A", "B"], day)).ToArray();
        Assert.Empty(SkillMinerEngine.Mine(oneConversation, Now));

        var twoRuns = new[] { Trace(a, ["A", "B"]), Trace(Guid.NewGuid(), ["A", "B"]) };
        Assert.Empty(SkillMinerEngine.Mine(twoRuns, Now));

        var old = Enumerable.Range(0, 4).Select(_ => Trace(Guid.NewGuid(), ["A", "B"], 40)).ToArray();
        Assert.Empty(SkillMinerEngine.Mine(old, Now));

        var failed = Enumerable.Range(0, 4).Select(_ => Trace(Guid.NewGuid(), ["A", "B"], outcome: "failed")).ToArray();
        Assert.Empty(SkillMinerEngine.Mine(failed, Now));

        var toolsFailed = Enumerable.Range(0, 4)
            .Select(_ => Trace(Guid.NewGuid(), ["A", "B"], toolOutcome: ToolOutcomes.Failed)).ToArray();
        Assert.Empty(SkillMinerEngine.Mine(toolsFailed, Now));
    }

    [Fact]
    public void A_sequence_that_only_lives_inside_a_longer_one_is_not_reported_and_skill_tools_are_ignored()
    {
        var traces = Enumerable.Range(0, 4)
            .Select(i => Trace(Guid.NewGuid(), ["LoadSkill", "A", "A", "B", "C"], i + 1)).ToArray();

        var finding = Assert.Single(SkillMinerEngine.Mine(traces, Now));

        Assert.Equal(["A", "B", "C"], finding.Tools);
    }

    [Fact]
    public void A_skill_in_three_thumbs_down_replies_and_at_most_one_up_is_flagged_but_balanced_ones_are_not()
    {
        var traces = new List<TurnTraceRecord>();
        var signals = new List<LearningSignalRecord>();
        for (var i = 0; i < 3; i++)
        {
            var message = Guid.NewGuid();
            traces.Add(Trace(Guid.NewGuid(), [], messageId: message, skills: ["meal-plan", "weekly-review"]));
            signals.Add(Thumb(message, LearningSignalKinds.ThumbsDown));
        }

        for (var i = 0; i < 3; i++)
        {
            var message = Guid.NewGuid();
            traces.Add(Trace(Guid.NewGuid(), [], messageId: message, skills: ["weekly-review"]));
            signals.Add(Thumb(message, LearningSignalKinds.ThumbsUp));
        }

        var findings = ReviewMinerEngine.Mine(traces, signals, Now, Now.AddDays(-30));

        var finding = Assert.Single(findings);
        Assert.Equal((ReviewFinding.SkillKind, "meal-plan", 3), (finding.Kind, finding.Target, finding.Count));
    }

    [Fact]
    public void A_memory_behind_poorly_rated_replies_is_flagged_by_its_id_and_the_last_thumb_wins()
    {
        var memory = Guid.NewGuid();
        var traces = new List<TurnTraceRecord>();
        var signals = new List<LearningSignalRecord>();
        for (var i = 0; i < 3; i++)
        {
            var message = Guid.NewGuid();
            traces.Add(Trace(Guid.NewGuid(), [], messageId: message, memories: [memory]));
            signals.Add(Thumb(message, LearningSignalKinds.ThumbsDown));
        }

        // A reply first rated down and then up counts as up.
        var changed = Guid.NewGuid();
        traces.Add(Trace(Guid.NewGuid(), [], messageId: changed, memories: [memory]));
        signals.Add(Thumb(changed, LearningSignalKinds.ThumbsDown, 3));
        signals.Add(Thumb(changed, LearningSignalKinds.ThumbsUp, 2));

        var finding = Assert.Single(ReviewMinerEngine.Mine(traces, signals, Now, Now.AddDays(-30)));

        Assert.Equal((ReviewFinding.MemoryKind, memory.ToString("N"), 3, 1), (finding.Kind, finding.Target,
            finding.Count, finding.Positive));
    }

    [Fact]
    public void A_tool_that_mostly_fails_is_flagged_but_a_mostly_working_or_rarely_used_one_is_not()
    {
        var traces = Enumerable.Range(0, 6).Select(_ => Failing("PostSlack", true))
            .Concat(Enumerable.Range(0, 2).Select(_ => Failing("PostSlack", false)))
            .Concat(Enumerable.Range(0, 5).Select(_ => Failing("GoodTool", true)))
            .Concat(Enumerable.Range(0, 10).Select(_ => Failing("GoodTool", false)))
            .Concat(Enumerable.Range(0, 3).Select(_ => Failing("Rare", true)))
            .Concat(Enumerable.Range(0, 6).Select(_ => Failing("Old", true, 20)))
            .ToArray();

        var finding = Assert.Single(ReviewMinerEngine.Mine(traces, [], Now, Now.AddDays(-30)));

        Assert.Equal(("PostSlack", 6, 8), (finding.Target, finding.Count, finding.Total));
        Assert.Equal("review:tool:PostSlack:2026-10", ReviewMinerEngine.Fingerprint(finding, Now));
    }

    [Fact]
    public void Drafted_skills_are_validated_and_secret_looking_ones_are_refused()
    {
        var good = ModelSkillDrafter.Parse("""
            ```json
            {"name":"Inbox Triage","description":"Triage the inbox into reply and archive.","instructions":"1. SearchMail.\n2. GetCalendar to find free time."}
            ```
            """);
        Assert.Equal("inbox-triage", good!.Name);

        Assert.Null(ModelSkillDrafter.Parse("not json"));
        Assert.Null(ModelSkillDrafter.Parse("""{"name":"x","description":"short","instructions":"tiny"}"""));
        Assert.Null(ModelSkillDrafter.Parse(
            """{"name":"leaky","description":"Use when posting updates.","instructions":"1. Use the api key: sk-abcdefghijklmnop1234 for posting."}"""));
        Assert.Null(ModelSkillDrafter.Parse(null));
    }

    [Fact]
    public async Task Mining_files_skill_and_review_proposals_and_drafts_at_most_two_a_night()
    {
        var world = new World();
        // Three different sequences, each seen enough times.
        foreach (var pair in new[] { ("A", "B"), ("C", "D"), ("E", "F") })
            for (var i = 0; i < 3; i++)
                world.Traces.Add(Trace(Guid.NewGuid(), [pair.Item1, pair.Item2], i + 1));
        foreach (var _ in Enumerable.Range(0, 6)) world.Traces.Add(Failing("PostSlack", true));

        var result = await world.Miner().RefreshAsync(Owner, default);

        Assert.Equal((ImprovementMiner.MaxDraftsPerRun, 1), (result.Skills, result.Reviews));
        Assert.Equal(ImprovementMiner.MaxDraftsPerRun, world.Drafted.Count);
        var all = await world.Repository.ListAsync(Owner, default);
        Assert.Equal(3, all.Count);
        Assert.All(all, item => Assert.Equal(ImprovementStatuses.Pending, item.Status));

        // The next night picks up the third and does not draft the first two again.
        world.Drafted.Clear();
        var second = await world.Miner().RefreshAsync(Owner, default);
        Assert.Equal(1, second.Skills);
        Assert.Single(world.Drafted);
        Assert.Equal(4, (await world.Repository.ListAsync(Owner, default)).Count);
    }

    [Fact]
    public async Task A_dismissed_sequence_is_not_drafted_or_offered_again()
    {
        var world = new World();
        for (var i = 0; i < 3; i++) world.Traces.Add(Trace(Guid.NewGuid(), ["A", "B"], i + 1));
        await world.Miner().RefreshAsync(Owner, default);
        var row = Assert.Single(await world.Repository.ListAsync(Owner, default));
        await world.Service().DismissAsync(row.Id, Owner, default);
        world.Drafted.Clear();

        var again = await world.Miner().RefreshAsync(Owner, default);

        Assert.Equal(0, again.Skills);
        Assert.Empty(world.Drafted);
        Assert.Equal(ImprovementStatuses.Dismissed, Assert.Single(await world.Repository.ListAsync(Owner, default)).Status);
    }

    [Fact]
    public async Task A_pending_proposal_disappears_when_the_pattern_stops_and_nothing_runs_when_switched_off()
    {
        var world = new World();
        for (var i = 0; i < 3; i++) world.Traces.Add(Trace(Guid.NewGuid(), ["A", "B"], i + 1));
        await world.Miner().RefreshAsync(Owner, default);
        Assert.Single(await world.Repository.ListAsync(Owner, default));

        world.Traces.Clear();
        await world.Miner().RefreshAsync(Owner, default);
        Assert.Empty(await world.Repository.ListAsync(Owner, default));

        for (var i = 0; i < 3; i++) world.Traces.Add(Trace(Guid.NewGuid(), ["A", "B"], i + 1));
        await world.Settings.SaveAsync(Owner, SettingsSections.Learning,
            LearningSettings.Default with { ProposeImprovements = false }, default);
        var off = await world.Miner().RefreshAsync(Owner, default);
        Assert.Equal((0, 0), (off.Skills, off.Reviews));
        Assert.Empty(await world.Repository.ListAsync(Owner, default));
    }

    [Fact]
    public async Task A_skill_or_memory_review_needs_the_thing_to_still_exist_and_disable_is_only_for_active_skills()
    {
        var world = new World();
        var existingMemory = Guid.NewGuid();
        world.Memory = new MemoryRecord(existingMemory, Owner, "preference", "Prefers tea over coffee in the morning.",
            0.5f, 0.9f, "conversation", null, Now, Now, null, false);
        await world.Skills.UpsertAsync(Owner, new SkillDraft("meal-plan", "Plan weekly meals around the diet.",
            "1. Check preferences.\n2. Propose dinners."), SkillSources.Learned, SkillStatuses.Active, true, null, default);
        for (var i = 0; i < 3; i++)
        {
            var message = Guid.NewGuid();
            world.Traces.Add(Trace(Guid.NewGuid(), [], messageId: message, memories: [existingMemory, Guid.NewGuid()],
                skills: ["meal-plan", "gone-skill"]));
            world.Signals.Add(Thumb(message, LearningSignalKinds.ThumbsDown));
        }

        await world.Miner().RefreshAsync(Owner, default);

        var titles = (await world.Repository.ListAsync(Owner, default)).Select(item => item.Fingerprint).Order().ToArray();
        Assert.Equal(["review:memory:" + existingMemory.ToString("N"), "review:skill:meal-plan"], titles);
        var skill = (await world.Repository.FindByFingerprintAsync(Owner, "review:skill:meal-plan", default))!;
        Assert.Equal(ReviewActions.DisableSkill,
            JsonSerializer.Deserialize<ReviewPayload>(skill.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Action);
    }

    private sealed class World
    {
        public List<TurnTraceRecord> Traces { get; } = [];
        public List<LearningSignalRecord> Signals { get; } = [];
        public List<string> Drafted { get; } = [];
        public MemoryRecord? Memory { get; set; }
        public InMemoryImprovementRepository Repository { get; } = new();
        public InMemorySkillRepository Skills { get; } = new();
        public InMemorySettingsStore Settings { get; } = new();

        public ImprovementService Service() => new(Repository,
            Fake<IMemoryService>.Create(("GetAsync", args => Task.FromResult(
                Memory is { } m && m.Id == (Guid)args[0]! ? m : null))),
            Skills, new RecordingNotifications(), Settings, new NullAudit(), new FixedClock());

        public ImprovementMiner Miner() => new(
            Fake<ILearningStore>.Create(
                ("ListTracesAsync", _ => Task.FromResult<IReadOnlyList<TurnTraceRecord>>(Traces.ToArray())),
                ("ListSignalsAsync", _ => Task.FromResult<IReadOnlyList<LearningSignalRecord>>(Signals.ToArray()))),
            Repository, Service(), Skills,
            Fake<IMemoryService>.Create(("GetAsync", args => Task.FromResult(
                Memory is { } m && m.Id == (Guid)args[0]! ? m : null))),
            new Drafter(Drafted), Settings, new FixedClock());
    }

    private sealed class Drafter(List<string> drafted) : ISkillDrafter
    {
        public Task<SkillDraft?> DraftAsync(Guid ownerId, ToolSequenceFinding finding, CancellationToken cancellationToken)
        {
            drafted.Add(finding.Key);
            return Task.FromResult<SkillDraft?>(new SkillDraft("flow-" + finding.Key.Replace('>', '-').ToLowerInvariant(),
                "Run these tools in order when asked.", "1. " + string.Join("\n2. ", finding.Tools)));
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
