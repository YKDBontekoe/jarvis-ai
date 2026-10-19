using Jarvis.Agents.Improvements;
using Jarvis.Agents.Skills;
using Jarvis.Application.Improvements;
using Jarvis.Application.Learning;
using Jarvis.Application.Skills;
using Microsoft.Agents.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class SkillOutcomeTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000d1a1");
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 3, 0, 0, TimeSpan.Zero);

    private static SkillRecord Skill(string name = "trip-planning", int runs = 0, int successes = 0, int useCount = 0,
        DateTimeOffset? countedAt = null, int version = 1, DateTimeOffset? updatedAt = null) =>
        new(Guid.NewGuid(), Owner, name, "Use when planning trips.", "1. Ask dates.", SkillSources.Learned,
            SkillStatuses.Active, false, version, useCount, null, Now.AddDays(-40), updatedAt ?? Now.AddDays(-40),
            runs, successes, countedAt);

    private static TurnTraceRecord Run(string skill, string outcome = "completed", string toolOutcome = ToolOutcomes.Completed,
        double hoursAgo = 24, Guid? message = null, Guid? conversation = null) =>
        new(Guid.NewGuid(), Owner, conversation ?? Guid.NewGuid(), message ?? Guid.NewGuid(), null, TurnKinds.Interactive,
            [], [skill], [new TurnToolCall("SearchMemory", toolOutcome, 40)], 300, outcome, Now.AddHours(-hoursAgo));

    private static LearningSignalRecord Signal(string kind, Guid? message = null, Guid? conversation = null) =>
        new(Guid.NewGuid(), Owner, kind, conversation ?? Guid.NewGuid(), message, null, null, null, null, Now.AddHours(-20));

    [Theory]
    [InlineData(0, 0, 0.0, 0.0)]
    [InlineData(3, 3, 0.43, 0.45)]
    [InlineData(10, 10, 0.71, 0.73)]
    [InlineData(5, 10, 0.23, 0.25)]
    [InlineData(0, 5, 0.0, 0.01)]
    public void Wilson_lower_bound_rewards_proof_not_just_a_high_share(int successes, int runs, double low, double high) =>
        Assert.InRange(SkillOutcomeRules.Wilson(successes, runs), low, high);

    [Fact]
    public void A_skill_with_too_few_runs_scores_neutral_and_proven_skills_rank_around_it()
    {
        var unknown = Skill(runs: 2, successes: 2);
        var proven = Skill(runs: 10, successes: 10);
        var poor = Skill(runs: 10, successes: 3);

        Assert.Equal(SkillOutcomeRules.NeutralScore, SkillOutcomeRules.Score(unknown));
        Assert.True(SkillOutcomeRules.Score(proven) > SkillOutcomeRules.Score(unknown));
        Assert.True(SkillOutcomeRules.Score(poor) < SkillOutcomeRules.Score(unknown));
    }

    [Theory]
    [InlineData(2, 0, false)]
    [InlineData(3, 3, false)]
    [InlineData(5, 3, true)]
    [InlineData(5, 4, false)]
    [InlineData(10, 2, true)]
    public void A_skill_needs_refining_when_enough_runs_mostly_went_badly(int runs, int successes, bool expected) =>
        Assert.Equal(expected, SkillOutcomeRules.NeedsRefining(Skill(runs: runs, successes: successes)));

    [Fact]
    public void A_run_goes_well_when_it_finished_no_tool_failed_and_it_was_not_judged_bad()
    {
        var skill = Skill();
        var good = Run(skill.Name);
        var failedTool = Run(skill.Name, toolOutcome: ToolOutcomes.Failed);
        var failedRun = Run(skill.Name, outcome: "failed");
        var down = Run(skill.Name);
        var again = Run(skill.Name);
        var conversation = Guid.NewGuid();
        var corrected = Run(skill.Name, conversation: conversation, hoursAgo: 20);
        var other = Run("another-skill");
        var signals = new[]
        {
            Signal(LearningSignalKinds.ThumbsDown, down.MessageId), Signal(LearningSignalKinds.Regenerate, again.MessageId),
            Signal(LearningSignalKinds.Correction, Guid.NewGuid(), conversation)
        };

        var delta = SkillOutcomeEngine.Tally([good, failedTool, failedRun, down, again, corrected, other], signals,
            [skill], Now)[skill.Id];

        Assert.Equal((6, 1), (delta.Runs, delta.Successes));
        Assert.Equal(Now - SkillOutcomeRules.CountingDelay, delta.CountedThrough);
    }

    [Fact]
    public void Only_new_turns_old_enough_to_be_rated_are_counted_and_each_only_once()
    {
        var skill = Skill();
        var old = Run(skill.Name, hoursAgo: 48);
        var fresh = Run(skill.Name, hoursAgo: 1);
        var first = SkillOutcomeEngine.Tally([old, fresh], [], [skill], Now);
        Assert.Equal(1, first[skill.Id].Runs);

        var counted = skill with { OutcomeCountedAt = first[skill.Id].CountedThrough };
        Assert.Empty(SkillOutcomeEngine.Tally([old, fresh], [], [counted], Now));
        Assert.Equal(1, SkillOutcomeEngine.Tally([old, fresh], [], [counted], Now.AddHours(8))[skill.Id].Runs);
    }

    [Fact]
    public void A_rewritten_skill_does_not_inherit_turns_from_before_its_last_change()
    {
        var skill = Skill(version: 3, updatedAt: Now.AddHours(-30));
        var before = Run(skill.Name, hoursAgo: 40);
        var after = Run(skill.Name, hoursAgo: 24);

        var delta = SkillOutcomeEngine.Tally([before, after], [], [skill], Now)[skill.Id];

        Assert.Equal(1, delta.Runs);
    }

    [Fact]
    public async Task The_skill_index_puts_proven_skills_first_and_poor_ones_after_untested_ones()
    {
        var repository = new InMemorySkillRepository();
        foreach (var (name, runs, successes, uses) in new[]
                 {
                     ("untested-popular", 0, 0, 50), ("poor", 10, 2, 90), ("proven", 10, 10, 1), ("untested-new", 0, 0, 0)
                 })
        {
            var saved = (await repository.UpsertAsync(Owner,
                new SkillDraft(name, $"About {name}.", "Body text long enough."), SkillSources.Learned,
                SkillStatuses.Active, true, null, CancellationToken.None))!;
            repository.Items[repository.Items.FindIndex(item => item.Id == saved.Id)] = saved with
            {
                OutcomeRuns = runs, OutcomeSuccesses = successes, UseCount = uses
            };
        }

        var client = new SkillTests.CapturingClient();
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            AIContextProviders = [new SkillsContextProvider(repository, Owner, null)]
        });
        await agent.RunAsync("hello");

        var order = new[] { "proven", "untested-popular", "untested-new", "poor" }
            .Select(name => client.LastPrompt.IndexOf($"- {name}:", StringComparison.Ordinal)).ToArray();
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.Order().ToArray(), order);
    }

    // ---- refinement evidence and drafts ----

    [Fact]
    public void Only_unlocked_active_skills_that_mostly_go_badly_are_candidates_worst_first()
    {
        var worst = Skill("worst", runs: 10, successes: 1);
        var bad = Skill("bad", runs: 5, successes: 3);
        var locked = Skill("locked", runs: 10, successes: 1) with { IsLocked = true };
        var disabled = Skill("off", runs: 10, successes: 1) with { Status = SkillStatuses.Disabled };
        var fine = Skill("fine", runs: 10, successes: 9);

        var candidates = SkillRefineEngine.Candidates([bad, fine, locked, disabled, worst]);

        Assert.Equal(["worst", "bad"], candidates.Select(skill => skill.Name));
    }

    [Fact]
    public void Evidence_names_failing_tools_and_error_kinds_and_counts_badly_rated_runs_only()
    {
        var skill = Skill(runs: 4, successes: 1);
        var down = Run(skill.Name, toolOutcome: ToolOutcomes.Failed);
        var failing = Run(skill.Name, toolOutcome: ToolOutcomes.Failed);
        var fine = Run(skill.Name);
        var elsewhere = Run("another-skill", toolOutcome: ToolOutcomes.Failed);
        var signals = new[]
        {
            Signal(LearningSignalKinds.ThumbsDown, down.MessageId),
            new LearningSignalRecord(Guid.NewGuid(), Owner, LearningSignalKinds.ToolFailure, Guid.NewGuid(), null, null,
                "SearchMemory", null, "input", Now.AddHours(-20))
        };

        var evidence = SkillRefineEngine.Collect(skill, [down, failing, fine, elsewhere], signals);

        Assert.Equal((4, 1, 1), (evidence.Runs, evidence.Successes, evidence.BadlyRated));
        Assert.Equal([new KeyValuePair<string, int>("SearchMemory", 2)], evidence.FailedTools);
        Assert.Equal(["input"], evidence.ErrorKinds);
    }

    [Theory]
    [InlineData("{\"description\": \"Use when planning trips.\", \"instructions\": \"1. Check the inputs first.\\n2. Ask for dates.\"}", true)]
    [InlineData("```json\n{\"description\": \"Use when planning trips.\", \"instructions\": \"1. Check the inputs first.\"}\n```", true)]
    [InlineData("{\"description\": \"Use when planning trips.\", \"instructions\": \"1. Ask dates.\"}", false)]
    [InlineData("{\"description\": \"Use when planning trips.\", \"instructions\": \"1. See https://example.com for details.\"}", false)]
    [InlineData("{\"description\": \"\", \"instructions\": \"\"}", false)]
    [InlineData("not json", false)]
    public void Redrafts_must_change_the_text_and_stay_clean(string reply, bool usable)
    {
        var skill = Skill() with { Instructions = "1. Ask dates." };

        var draft = ModelSkillRefiner.Parse(skill, reply);

        Assert.Equal(usable, draft is not null);
        if (draft is not null) Assert.Equal(skill.Name, draft.Name);
    }
}
