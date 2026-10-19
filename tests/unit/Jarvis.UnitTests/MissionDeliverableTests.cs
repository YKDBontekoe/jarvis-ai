using Jarvis.Application.Library;
using Jarvis.Application.Missions;
using Jarvis.Domain.Library;
using Jarvis.Domain.Missions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class MissionDeliverableTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000ff01");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000ff02");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static MissionStep Step(string key, int ordinal, string status, string? result = null) =>
        new(Guid.NewGuid(), MissionId, Owner, key, ordinal, MissionRoles.Researcher, $"Step {key}", "do it", [], status,
            null, result, null, Now, Now);

    private static readonly Guid MissionId = Guid.Parse("01996b8c-6000-7000-8000-00000000ff10");

    private static MissionDetail Detail(string status, IReadOnlyList<MissionStep> steps, string? summary = null,
        IReadOnlyList<MissionNote>? notes = null, DateTimeOffset? completed = null) =>
        new(new Mission(MissionId, Owner, "Pick a laptop", "goal", status, null, summary, null, Now, Now, Now,
                completed ?? Now.AddMinutes(12)), steps, notes ?? [], new Dictionary<Guid, string>());

    [Fact]
    public void Compose_turns_a_few_answers_into_a_goal_that_forbids_outside_actions()
    {
        var result = MissionStarter.Compose("decision", new Dictionary<string, string?>
        {
            ["topic"] = "which laptop to buy", ["criteria"] = "weight and battery", ["constraints"] = "under 1500 euro"
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Research a decision: which laptop to buy", result.Composed!.Title);
        Assert.Contains("What matters most: weight and battery", result.Composed.Goal);
        Assert.Contains("Constraints: under 1500 euro", result.Composed.Goal);
        Assert.Contains("name every source", result.Composed.Goal);
        Assert.Contains("Do not send, post, buy, book or change anything outside Jarvis", result.Composed.Goal);
        Assert.True(result.Composed.Goal.Length <= MissionRules.MaxGoalLength);
    }

    [Fact]
    public void Compose_asks_only_for_what_is_needed_and_uses_the_owners_sources_when_given()
    {
        var missing = MissionStarter.Compose("compare", new Dictionary<string, string?> { ["criteria"] = "price" });
        var unknown = MissionStarter.Compose("novel", null);
        var sourced = MissionStarter.Compose("meeting", new Dictionary<string, string?>
        {
            ["meeting"] = "Kitchen sync", ["sources"] = "only my Library"
        });

        Assert.Equal("topic", missing.Field);
        Assert.Contains("needed", missing.Message);
        Assert.Equal("template", unknown.Field);
        Assert.Contains("Use only these sources: only my Library", sourced.Composed!.Goal);
        Assert.DoesNotContain("the web and my Library", sourced.Composed.Goal);
        Assert.Equal(4, MissionStarter.All.Count);
        Assert.All(MissionStarter.All, t => Assert.Contains(t.Questions, q => q.Required));
    }

    [Fact]
    public void A_finished_mission_shows_its_sections_sources_and_what_was_checked_by_code()
    {
        var steps = new[]
        {
            Step("a", 1, StepStatuses.Completed, "Found it at https://example.com/laptops and https://example.com/laptops."),
            Step("b", 2, StepStatuses.Completed, "Compared. See (https://review.example.org/x).")
        };

        var deliverable = DeliverableBuilder.Build(Detail(MissionStatuses.Completed, steps, "Buy the light one."), null);

        Assert.Equal("Buy the light one.", deliverable.Headline);
        Assert.Equal(2, deliverable.Sections.Count);
        Assert.Equal(["https://example.com/laptops", "https://review.example.org/x"], deliverable.Evidence.Select(e => e.Url));
        Assert.Equal("example.com", deliverable.Evidence[0].Host);
        Assert.All(deliverable.Checks, c => Assert.True(c.Ok, c.Label));
        Assert.Empty(deliverable.Limitations);
        Assert.Contains("read it before you rely on it", deliverable.ReviewNote);
        Assert.Contains("Decide what to do with this", Assert.Single(deliverable.NextDecisions));
        Assert.Equal("Took about 12 minutes. Model cost is not tracked per mission, so none is shown.", deliverable.Effort);
    }

    [Fact]
    public void A_failed_or_skipped_step_is_a_limitation_and_a_decision_not_a_success()
    {
        var steps = new[]
        {
            Step("a", 1, StepStatuses.Completed, "Some text without links."),
            Step("b", 2, StepStatuses.Failed),
            Step("c", 3, StepStatuses.Skipped)
        };

        var deliverable = DeliverableBuilder.Build(Detail(MissionStatuses.Failed, steps), null);

        Assert.False(deliverable.Checks.Single(c => c.Id == "nothing-failed").Ok);
        Assert.False(deliverable.Checks.Single(c => c.Id == "sources").Ok);
        Assert.True(deliverable.Checks.Single(c => c.Id == "finished").Ok);
        Assert.Contains("“Step b” did not finish, so its part is missing.", deliverable.Limitations);
        Assert.Contains("You skipped “Step c”; later steps carried on without its result.", deliverable.Limitations);
        Assert.Contains(deliverable.Limitations, l => l.StartsWith("No web links"));
        Assert.Contains("Retry or skip “Step b”.", deliverable.NextDecisions);
        Assert.Equal("Some text without links.", deliverable.Headline);
    }

    [Fact]
    public void An_unfinished_mission_does_not_pass_the_finished_check_and_open_questions_surface()
    {
        var steps = new[] { Step("a", 1, StepStatuses.Completed, "Partial."), Step("b", 2, StepStatuses.Pending) };
        var notes = new[]
        {
            new MissionNote(Guid.NewGuid(), MissionId, Owner, "open_question", "Which budget applies?", "a", Now),
            new MissionNote(Guid.NewGuid(), MissionId, Owner, "price", "100", "a", Now)
        };

        var deliverable = DeliverableBuilder.Build(Detail(MissionStatuses.Running, steps, null, notes), null);

        Assert.False(deliverable.Checks.Single(c => c.Id == "finished").Ok);
        Assert.Equal("open_question: Which budget applies?", Assert.Single(deliverable.NextDecisions));
    }

    [Fact]
    public void A_long_step_text_is_clipped_and_the_markdown_carries_sources_and_the_review_note()
    {
        var steps = new[] { Step("a", 1, StepStatuses.Completed, new string('x', 5000) + " https://example.com/a") };
        var deliverable = DeliverableBuilder.Build(Detail(MissionStatuses.Completed, steps), null);

        var markdown = DeliverableBuilder.ToMarkdown(deliverable);

        Assert.True(deliverable.Sections[0].Text.Length <= DeliverableBuilder.MaxSectionLength);
        Assert.Contains("## Sources\n- https://example.com/a", markdown.Replace("\r", ""));
        Assert.Contains(DeliverableBuilder.ReviewNote, markdown);
    }

    private sealed class World
    {
        public readonly InMemorySettingsStore Settings = new();
        public readonly List<NoteDraft> Saved = [];
        public MissionDetail Mission = Detail(MissionStatuses.Completed,
            [Step("a", 1, StepStatuses.Completed, "Done https://example.com/x")], "Result.");

        public MissionDeliverableService Service => new(
            Fake<IMissionService>.Create(("GetAsync", args => (Guid)args[1]! == Owner && (Guid)args[0]! == MissionId ? Mission : null)),
            Fake<ILibraryService>.Create(("AddNoteAsync", args =>
            {
                Saved.Add((NoteDraft)args[1]!);
                return LibraryOperation<LibraryItem>.Ok(new LibraryItem(Guid.Parse("01996b8c-6000-7000-8000-00000000ff20"), Owner,
                    "report", null, "t", "", [], [], "c", "app", null, Now, Now));
            })),
            Settings);
    }

    [Fact]
    public async Task Saving_writes_one_private_report_and_saving_again_returns_the_same_item()
    {
        var world = new World();

        var first = await world.Service.SaveAsync(Owner, MissionId, default);
        var second = await world.Service.SaveAsync(Owner, MissionId, default);
        var read = await world.Service.GetAsync(Owner, MissionId, default);

        Assert.True(first.Succeeded);
        Assert.Single(world.Saved);
        Assert.Contains("private", world.Saved[0].Tags!);
        Assert.Contains("mission", world.Saved[0].Tags!);
        Assert.Equal(first.Value!.LibraryItemId, second.Value!.LibraryItemId);
        Assert.Equal(first.Value.LibraryItemId, read!.LibraryItemId);
    }

    [Fact]
    public async Task Another_owner_sees_nothing_and_an_empty_mission_has_nothing_to_save()
    {
        var world = new World();
        Assert.Null(await world.Service.GetAsync(Other, MissionId, default));
        Assert.Equal(MissionFailure.NotFound, (await world.Service.SaveAsync(Other, MissionId, default)).Failure);

        world.Mission = Detail(MissionStatuses.Ready, [Step("a", 1, StepStatuses.Pending)]);
        var empty = await world.Service.SaveAsync(Owner, MissionId, default);

        Assert.Equal(MissionFailure.Invalid, empty.Failure);
        Assert.Empty(world.Saved);
    }
}
