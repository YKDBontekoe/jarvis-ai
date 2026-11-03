using System.Text.Json;
using Jarvis.Application.Automations;
using Jarvis.Application.Events;
using Jarvis.Application.Library;
using Jarvis.Application.Planner;
using Jarvis.Application.Proactivity;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ProactivePreparationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private static ProactiveCase Case(string kind, IReadOnlyList<ProactiveSource>? sources = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), "fixture", kind, "Work", "Keep work moving", "open", true, null,
        sources?.FirstOrDefault()?.Ref, sources?.Select(s => s.Ref).ToArray() ?? [], null,
        new(QuietStart: 0, QuietEnd: 0), [], [], null, null, "Review", sources ?? [], [], [], [],
        Now.AddDays(-2), Now, null, Now);
    private static ProactivePreparationService Service(IOwnerSettingDocuments? documents = null,
        IDayPlannerService? planner = null, IOwnerEventRepository? events = null,
        IAutomationRuleRepository? automations = null, IWebPageFetcher? pages = null, IProactiveClaimReviewer? reviewer = null) => new(
        documents ?? Fake<IOwnerSettingDocuments>.Create(), planner ?? Fake<IDayPlannerService>.Create(),
        events ?? Fake<IOwnerEventRepository>.Create(), automations ?? Fake<IAutomationRuleRepository>.Create(),
        pages ?? Fake<IWebPageFetcher>.Create(), new Clock(), claimReviewer: reviewer);

    [Fact]
    public async Task Research_reviews_explicit_claims_caches_unchanged_evidence_and_preserves_the_original_statement()
    {
        const string original = "The refund period is thirty days.";
        const string current = "The refund period is fourteen days.";
        var source = new ProactiveSource("library:" + Guid.NewGuid(), "Refund terms", "saved-v1", "saved", original, Url: "https://example.org/policy");
        var value = Case("research", [source]) with { Assumptions = [new("refund", original, source.Ref)],
            Reports = [new(Now.AddDays(-1), "Original report", new Dictionary<string, string> { [source.Ref] = "old" })] };
        var calls = 0;
        var reviewer = Fake<IProactiveClaimReviewer>.Create(("ReviewAsync", args => {
            calls++;
            return ((IReadOnlyList<ProactiveClaimEvidence>)args[1]!).Select(claim => new ProactiveClaimReview(claim.Id,
                claim.Statement.Contains("fourteen") ? "supported" : "contradicted", "The source explicitly states fourteen days.", current,
                claim.SourceRef, claim.SourceVersion, ProactiveRules.Hash(claim.Statement))).ToArray();
        }));
        var service = Service(pages: Fake<IWebPageFetcher>.Create(("FetchAsync", _ => new FetchedPage(source.Url!, "text/plain", current))), reviewer: reviewer);
        var first = await service.PrepareAsync(value, [source], Ct);
        Assert.Contains("contradicted", first.Report);
        Assert.Contains(source.Url!, first.Report);
        Assert.Contains("Compared with preparation", first.Report);
        Assert.Equal(original, value.Assumptions[0].Statement);
        var checkedValue = value with { Context = first.Sources, PreparedReport = first.Report, ClaimReviews = first.ClaimReviews };
        var unchanged = await service.PrepareAsync(checkedValue, [source], Ct);
        Assert.Equal(first.Report, unchanged.Report);
        Assert.Equal(1, calls);
        var changedClaim = checkedValue with { Assumptions = [value.Assumptions[0] with { Statement = current }] };
        var reviewed = await service.PrepareAsync(changedClaim, [source], Ct);
        Assert.Equal("supported", Assert.Single(reviewed.ClaimReviews!).Status);
        Assert.Equal(2, calls);
        var offline = Service(pages: Fake<IWebPageFetcher>.Create(("FetchAsync", _ => throw new HttpRequestException())), reviewer: reviewer);
        var failed = await offline.PrepareAsync(checkedValue, [source], Ct);
        Assert.Equal("unavailable", Assert.Single(failed.ClaimReviews!).Status);
        Assert.Contains("unknown", failed.Report);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Replanning_preserves_pinned_and_completed_blocks_and_keeps_overflow_visible()
    {
        var pinned = new DayPlanItem(Guid.NewGuid(), "Pinned", 30, false, Now.AddHours(1), Now, Pinned: true);
        var done = new DayPlanItem(Guid.NewGuid(), "Done", 30, true, Now, Now);
        var flexible = new DayPlanItem(Guid.NewGuid(), "Draft", 60, false, Now.AddHours(-1), Now);
        var overflow = new DayPlanItem(Guid.NewGuid(), "Long research", 120, false, null, Now);
        var state = new DayPlanState(new(2026, 11, 2), [pinned, done, flexible, overflow], new(9, 0), new(15, 0), null, "UTC");
        var timeline = new DayTimelineDto(state.Date, "UTC", state.DayStart, state.DayEnd, true, false,
            [new("event", "meeting", "Meeting", Now.AddMinutes(30), Now.AddHours(1), false)], [], [], 120, null);
        var result = await Service(
            Fake<IOwnerSettingDocuments>.Create(("ReadDocumentAsync", _ => ProactiveRules.Serialize(state))),
            Fake<IDayPlannerService>.Create(("GetTodayAsync", _ => timeline)))
            .PrepareAsync(Case("day_plan"), [], Ct);
        Assert.NotNull(result.Mutation);
        var next = JsonSerializer.Deserialize<DayPlanState>(result.Mutation.AfterJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(pinned, next.Items[0]);
        Assert.Equal(done, next.Items[1]);
        Assert.Equal(Now.AddMinutes(95), next.Items[2].StartAt); // Existing planner keeps a five-minute buffer.
        Assert.Null(next.Items[3].StartAt);
        Assert.Contains("Long research", result.Report);
    }

    [Fact]
    public async Task Calendar_failure_never_produces_an_automatic_plan()
    {
        var state = new DayPlanState(new(2026, 11, 2), [], new(9, 0), new(17, 0), null);
        var timeline = new DayTimelineDto(state.Date, "UTC", state.DayStart, state.DayEnd, true, true, [], [], [], 0, null);
        var service = Service(Fake<IOwnerSettingDocuments>.Create(("ReadDocumentAsync", _ => ProactiveRules.Serialize(state))),
            Fake<IDayPlannerService>.Create(("GetTodayAsync", _ => timeline)));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.PrepareAsync(Case("day_plan"), [], Ct));
    }

    [Fact]
    public async Task Research_tracks_source_versions_cites_changes_and_marks_retrieval_failure_unknown()
    {
        var source = new ProactiveSource("library:" + Guid.NewGuid(), "Policy", "saved-v1", "saved", "Old excerpt", Url: "https://example.org/policy");
        var body = "New policy evidence.\nCancellation takes thirty days.";
        var service = Service(pages: Fake<IWebPageFetcher>.Create(("FetchAsync", _ => new FetchedPage(source.Url!, "text/plain", body))));
        var value = Case("research", [source]);
        var first = await service.PrepareAsync(value, [source], Ct);
        Assert.Contains(source.Url!, first.Report);
        Assert.Contains("thirty days", first.Report);
        Assert.Equal("saved-v1", first.Sources[0].OriginVersion);
        var checkedCase = value with { Context = first.Sources, PreparedReport = first.Report };
        var unchanged = await service.PrepareAsync(checkedCase, [source], Ct);
        Assert.Equal(first.Report, unchanged.Report);
        Assert.Equal(first.Sources[0].Version, unchanged.Sources[0].Version);
        var offline = Service(pages: Fake<IWebPageFetcher>.Create(("FetchAsync", _ => throw new HttpRequestException())));
        var failed = await offline.PrepareAsync(checkedCase, [source], Ct);
        Assert.Equal("unavailable", Assert.Single(failed.Sources).State);
        Assert.Contains("unknown", failed.Report);
        Assert.DoesNotContain("No selected public sources changed", failed.Report);
    }

    [Fact]
    public async Task Shared_goal_capacity_is_allocated_once_in_the_owners_priority_order()
    {
        var value = Case("goal_capacity") with { AvailableMinutes = 90,
            Steps = [new("health", "Health", [], Minutes: 60), new("learn", "Learning", [], Minutes: 60)] };
        var result = await Service().PrepareAsync(value, [], Ct);
        Assert.Contains("Health: 60 of 60", result.Report);
        Assert.Contains("Learning: 30 of 60", result.Report);
        Assert.Null(result.Mutation);
    }

    [Fact]
    public async Task Meeting_preparation_carries_only_unresolved_accepted_items_and_shared_agenda()
    {
        var source = new ProactiveSource("meeting:" + Guid.NewGuid(), "Weekly review", "v2", "scheduled",
            "Agenda: Budget\nOpen accepted: Choose a supplier\nResolved accepted: Measure the room");
        var result = await Service().PrepareAsync(Case("meeting", [source]), [source], Ct);
        Assert.Contains("Choose a supplier", result.Report);
        Assert.DoesNotContain("Measure the room", result.Report);
    }

    [Fact]
    public async Task Seven_day_trial_observes_actual_events_deduplicates_rechecks_and_never_runs_actions()
    {
        var source = new ProactiveSource("automation:" + Guid.NewGuid(), "Routine", "v1", "draft", "Draft routine");
        var value = Case("routine_trial", [source]);
        var definition = new AutomationRuleDefinition(1, new EventTriggerDefinition("task_completed", "report", null), null,
            [new NotificationActionDefinition("Ready", "Report ready")], null);
        var rule = new AutomationRuleRecord(Guid.Parse(source.Ref.Split(':')[1]), value.OwnerId, "Routine", 1,
            AutomationDefinitionJson.Serialize(definition), "draft", "unused", null, null, null, null, Now.AddDays(-2), Now);
        IReadOnlyList<OwnerEventRecord> observed = [
            new(Guid.NewGuid(), value.OwnerId, "task.completed", "report ready", null, null, null, EventOrigin.Owner, null, Now.AddHours(-2)),
            new(Guid.NewGuid(), value.OwnerId, "task.completed", "other task", null, null, null, EventOrigin.Owner, null, Now.AddHours(-1))];
        var service = Service(events: Fake<IOwnerEventRepository>.Create(("ListAsync", _ => observed)),
            automations: Fake<IAutomationRuleRepository>.Create(("GetAsync", _ => rule)));
        var result = await service.PrepareAsync(value, [source], Ct);
        Assert.Equal(2, result.Observations!.Count);
        Assert.Equal(1, result.Observations.Sum(o => o.Actions));
        var repeated = await service.PrepareAsync(value with { TrialObservations = result.Observations }, [source], Ct);
        Assert.Equal(result.Observations.Count, repeated.Observations!.Count);
        Assert.Null(result.Mutation);
        var changed = source with { Version = "v2" };
        await Assert.ThrowsAsync<ArgumentException>(() => service.PrepareAsync(value, [changed], Ct));
    }
}
