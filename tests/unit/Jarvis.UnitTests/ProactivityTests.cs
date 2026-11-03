using Jarvis.Application.Proactivity;
using Jarvis.Application.Settings;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ProactivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Keep track of the return", true)]
    [InlineData("Ik wacht op hun antwoord", false)]
    [InlineData("Compare the offers later", false)]
    public void Capturing_intentions_never_enables_a_job_or_creates_a_promise(string text, bool explicitRequest)
    {
        var message = Guid.NewGuid();
        var draft = OpenLoopDetector.Detect(text, message, null)!;
        Assert.Equal(explicitRequest, draft.Explicit);
        Assert.False(draft.Enabled);
        Assert.Equal($"message:{message}", Assert.Single(draft.SourceRefs!));
        Assert.Null(OpenLoopDetector.Detect("What is the weather?", message, null));
    }

    [Fact]
    public async Task Expired_work_stops_without_reading_sources()
    {
        var f = new Fixture();
        var value = await f.Service.CreateAsync(f.Owner, new("research", "Observe", "Observe sources", Enabled: true, ExpiresAt: Now.AddMinutes(-1)), Ct);
        var reads = f.Sources.Reads;
        var stopped = (await f.Service.CheckAsync(f.Owner, value.Id, Ct))!;
        Assert.False(stopped.Enabled);
        Assert.True(stopped.Terminal);
        Assert.Equal(reads, f.Sources.Reads);
    }

    [Fact]
    public async Task Explicit_and_inferred_intentions_have_different_lifecycles_and_terminal_work_is_not_resurrected()
    {
        var f = new Fixture();
        var suggested = await f.Service.CreateAsync(f.Owner, new("open_loop", "Compare", "Compare offers", Explicit: false), Ct);
        Assert.Equal("suggested", suggested.Status);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.CommandAsync(f.Owner, suggested.Id, "enable", suggested.Revision, Ct));
        var accepted = (await f.Service.CommandAsync(f.Owner, suggested.Id, "accept", suggested.Revision, Ct))!;
        Assert.Equal("open", accepted.Status);
        Assert.False(accepted.Enabled);
        var dismissed = (await f.Service.CommandAsync(f.Owner, accepted.Id, "dismiss", accepted.Revision, Ct))!;
        var repeated = await f.Service.CreateAsync(f.Owner, new("open_loop", "Compare again", "Compare offers"), Ct);
        Assert.Equal(dismissed.Id, repeated.Id);
        Assert.Equal("dismissed", repeated.Status);
    }

    [Fact]
    public async Task Cases_are_owner_scoped_and_stale_commands_conflict()
    {
        var f = new Fixture();
        var value = await f.Create();
        Assert.Null(await f.Service.GetAsync(Guid.NewGuid(), value.Id, Ct));
        await f.Service.CommandAsync(f.Owner, value.Id, "pause", value.Revision, Ct);
        await Assert.ThrowsAsync<ProactiveConflictException>(() => f.Service.CommandAsync(f.Owner, value.Id, "resolve", value.Revision, Ct));
    }

    [Fact]
    public async Task Repeated_checks_are_idempotent_and_budgeted_and_do_not_infer_completion()
    {
        var f = new Fixture();
        var value = await f.Create();
        var first = (await f.Service.CheckAsync(f.Owner, value.Id, Ct))!;
        Assert.Equal(1, first.ChecksToday);
        var again = (await f.Service.CheckAsync(f.Owner, value.Id, Ct))!;
        Assert.Equal(first.Revision, again.Revision);
        Assert.Equal("open", again.Status);
        var paused = (await f.Service.CommandAsync(f.Owner, value.Id, "pause", again.Revision, Ct))!;
        Assert.False(paused.Enabled);
        Assert.Null(paused.NextCheckAt);
    }

    [Fact]
    public async Task Disabled_autonomy_and_quiet_hours_do_no_source_reads()
    {
        var f = new Fixture();
        var value = await f.Create();
        f.Settings.Value = new AutonomySettings(Enabled: false);
        var reads = f.Sources.Reads;
        await f.Service.CheckAsync(f.Owner, value.Id, Ct);
        Assert.Equal(reads, f.Sources.Reads);
        Assert.True(ProactiveRules.Quiet(new(TimeZoneId: "Europe/Amsterdam"), new(2026, 11, 2, 23, 0, 0, TimeSpan.Zero)));
        Assert.False(ProactiveRules.Quiet(new(TimeZoneId: "Europe/Amsterdam"), Now));
    }

    [Fact]
    public async Task Explicit_feedback_learns_only_after_three_answers_and_can_be_undone()
    {
        var f = new Fixture();
        var value = await f.Create();
        for (var i = 0; i < 2; i++) value = (await f.Service.FeedbackAsync(f.Owner, value.Id, "too_early", value.Revision, Ct))!;
        Assert.Equal(60, value.Policy.CadenceMinutes);
        value = (await f.Service.FeedbackAsync(f.Owner, value.Id, "too_early", value.Revision, Ct))!;
        Assert.Equal(120, value.Policy.CadenceMinutes);
        var receipt = Assert.Single(value.Receipts, r => r.Kind == "preference");
        value = (await f.Service.UndoAsync(f.Owner, value.Id, receipt.Id, value.Revision, Ct))!;
        Assert.Equal(60, value.Policy.CadenceMinutes);
        Assert.True(value.Receipts.Single(r => r.Id == receipt.Id).Undone);
    }

    [Fact]
    public async Task Undo_refuses_to_overwrite_later_owner_preferences()
    {
        var f = new Fixture();
        var value = await f.Create();
        for (var i = 0; i < 3; i++) value = (await f.Service.FeedbackAsync(f.Owner, value.Id, "not_useful", value.Revision, Ct))!;
        var receipt = value.Receipts.Single(r => r.Kind == "preference");
        value = (await f.Service.UpdateAsync(f.Owner, value.Id, new("open_loop", value.Title, value.Objective,
            Policy: value.Policy with { CadenceMinutes = 240 }), value.Revision, Ct))!;
        await Assert.ThrowsAsync<ProactiveConflictException>(() => f.Service.UndoAsync(f.Owner, value.Id, receipt.Id, value.Revision, Ct));
    }

    [Fact]
    public async Task Source_removal_and_profile_revocation_are_rechecked()
    {
        var f = new Fixture();
        var source = new ProactiveSource($"memory:{Guid.NewGuid()}", "Preference", "v1", "current", "Private text");
        f.Sources.Values = [source];
        var value = await f.Service.CreateAsync(f.Owner, new("context", "Context", "Resume", [source.Ref]), Ct);
        f.Sources.Values = [];
        Assert.Empty((await f.Service.GetAsync(f.Owner, value.Id, Ct))!.Context);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.CreateAsync(f.Owner, new("context", "Missing", "Other", [source.Ref]), Ct));
    }

    [Fact]
    public async Task Removing_a_source_from_the_case_hides_derived_reports_and_claims_from_that_source()
    {
        var f = new Fixture();
        var source = new ProactiveSource("library:" + Guid.NewGuid(), "Old source", "v1", "saved", "Private excerpt");
        f.Sources.Values = [source];
        var value = await f.Service.CreateAsync(f.Owner, new("research", "Research", "Watch the evidence", [source.Ref]), Ct);
        value = value with { Revision = value.Revision + 1, PreparedReport = "Private excerpt", Reports = [new(Now, "Private excerpt", new Dictionary<string, string> { [source.Ref] = "v1" })],
            ClaimReviews = [new("claim", "supported", "Private excerpt", "Private excerpt", source.Ref, "v1", "claim-hash")] };
        Assert.True(await f.Repository.CompareExchangeAsync(value, value.Revision - 1, Ct));
        await f.Service.UpdateAsync(f.Owner, value.Id, new("research", "Research", "Watch the evidence", []), value.Revision, Ct);
        var current = (await f.Service.GetAsync(f.Owner, value.Id, Ct))!;
        Assert.Null(current.PreparedReport);
        Assert.Empty(current.Reports!);
        Assert.Empty(current.ClaimReviews!);
        Assert.Empty(current.Context);
    }

    [Fact]
    public async Task Editing_a_claim_invalidates_its_previous_classification_without_erasing_authorized_history()
    {
        var f = new Fixture();
        var source = new ProactiveSource("library:" + Guid.NewGuid(), "Policy", "saved", "saved", "Original source", Url: "https://example.org/policy");
        f.Sources.Values = [source];
        var claim = new ProactiveAssumption("claim", "Refunds last thirty days.", source.Ref);
        var value = await f.Service.CreateAsync(f.Owner, new("research", "Policy", "Monitor policy", [source.Ref], Assumptions: [claim]), Ct);
        value = value with { Revision = value.Revision + 1, Context = [source with { State = "checked", Version = "fetched", OriginVersion = "saved" }],
            PreparedReport = "Old classification", ClaimReviews = [new("claim", "supported", "Old classification", "Thirty days", source.Ref, "fetched", ProactiveRules.Hash(claim.Statement))],
            Reports = [new(Now, "Old classification", new Dictionary<string, string> { [source.Ref] = "fetched" })] };
        Assert.True(await f.Repository.CompareExchangeAsync(value, value.Revision - 1, Ct));
        Assert.Single((await f.Service.GetAsync(f.Owner, value.Id, Ct))!.ClaimReviews!);
        await f.Service.UpdateAsync(f.Owner, value.Id, new("research", "Policy", "Monitor policy", [source.Ref],
            Assumptions: [claim with { Statement = "Refunds last fourteen days." }]), value.Revision, Ct);
        var current = (await f.Service.GetAsync(f.Owner, value.Id, Ct))!;
        Assert.Null(current.PreparedReport);
        Assert.Empty(current.ClaimReviews!);
        Assert.Single(current.Reports!);
    }

    [Fact]
    public async Task Reply_received_never_completes_a_promise()
    {
        var f = new Fixture();
        var value = await f.Service.CreateAsync(f.Owner, new("follow_through", "Quote", "Await quote"), Ct);
        var result = ProactiveRules.Evaluate(value, [new($"inbox_thread:{Guid.NewGuid()}", "Reply", "v2", "reply_received", "Received", Now)], Now);
        Assert.Contains("still open", result.NextAction);
        Assert.Equal("open", value.Status);
    }

    [Fact]
    public async Task Dependencies_unlock_only_after_completion_and_cycles_are_rejected()
    {
        var f = new Fixture();
        var source = new ProactiveSource($"task:{Guid.NewGuid()}", "Research", "v1", "completed", "Done");
        var value = await f.Create();
        value = value with { Steps = [new("research", "Research", [], source.Ref), new("draft", "Draft", ["research"])] };
        var result = ProactiveRules.Evaluate(value, [source], Now);
        Assert.Equal("Draft", result.NextAction);
        Assert.True(result.Steps[0].Done);
        Assert.Throws<ArgumentException>(() => ProactiveRules.Validate(new("project", "Plan", "Ship",
            Steps: [new("a", "A", ["b"]), new("b", "B", ["a"])])));
    }

    [Fact]
    public async Task Deadline_risk_shows_arithmetic_and_missing_estimates_remain_unknown()
    {
        var f = new Fixture();
        var value = (await f.Create()) with { Deadline = Now.AddDays(1), AvailableMinutes = 30,
            Steps = [new("a", "Research", [], Minutes: 90)] };
        var result = ProactiveRules.Evaluate(value, [], Now);
        Assert.Contains("60 minutes short", Assert.Single(result.Findings).Reason);
        Assert.Contains("30-minute first pass", Assert.Single(result.Findings).Reason);
        result = ProactiveRules.Evaluate(value with { Steps = [new("a", "Research", [])] }, [], Now);
        Assert.Contains("unknown", Assert.Single(result.Findings).Title);
    }

    [Fact]
    public async Task Missing_input_becomes_one_specific_request_without_presenting_old_preparation_as_current()
    {
        var f = new Fixture();
        var value = await f.Create();
        var missing = true;
        var prepare = Fake<IProactivePreparationService>.Create(("PrepareAsync", _ => missing
            ? throw new ArgumentException("Choose a saved public web source.") : new ProactivePreparation([], "Fresh preparation")));
        var service = new ProactiveService(f.Repository, f.Sources, f.Settings, new Clock(), prepare);
        var result = (await service.CheckAsync(f.Owner, value.Id, Ct))!;
        Assert.Equal("needs_input", result.Status);
        Assert.Equal("Needed: Choose a saved public web source.", result.NextAction);
        Assert.Null(result.PreparedReport);
        Assert.Null(result.NextCheckAt);
        Assert.Equal("permission", ProactiveRules.Failure(new HttpRequestException("Denied", null, System.Net.HttpStatusCode.Forbidden)));
        Assert.Equal("transient", ProactiveRules.Failure(new HttpRequestException("Unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable)));
        missing = false;
        var recovered = (await service.CheckAsync(f.Owner, value.Id, Ct))!;
        Assert.Equal("open", recovered.Status);
        Assert.Equal("Fresh preparation", recovered.PreparedReport);
        Assert.Null(recovered.FailureKind);
    }

    [Fact]
    public async Task Uncertain_memory_comparison_never_changes_protected_sources()
    {
        var f = new Fixture();
        var value = (await f.Create()) with { Kind = "memory_conflict" };
        var memory = new ProactiveSource($"memory:{Guid.NewGuid()}", "Preference", "v1", "current", "Old", Protected: true);
        var result = ProactiveRules.Evaluate(value, [memory, memory with { Ref = $"memory:{Guid.NewGuid()}", Detail = "New" }], Now);
        Assert.Contains(result.Findings, x => x.Key == "memory:compare");
        Assert.Equal("Old", memory.Detail);
    }

    private sealed class Fixture
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public Store Repository { get; } = new();
        public Reader Sources { get; } = new();
        public Settings Settings { get; } = new();
        public ProactiveService Service => new(Repository, Sources, Settings, new Clock());
        public Task<ProactiveCase> Create() => Service.CreateAsync(Owner, new("open_loop", "Work", "Finish this", Enabled: true), Ct);
    }

    [Fact]
    public async Task Source_change_during_preparation_discards_the_result_before_commit()
    {
        var f = new Fixture();
        var source = new ProactiveSource($"task:{Guid.NewGuid()}", "Research", "v1", "open", "Old evidence");
        f.Sources.Values = [source];
        var value = await f.Service.CreateAsync(f.Owner, new("context", "Resume", "Resume work", [source.Ref], Enabled: true), Ct);
        var prepare = Fake<IProactivePreparationService>.Create(("PrepareAsync", _ => {
            f.Sources.Values = [source with { Version = "v2", Detail = "Owner changed the evidence" }];
            return new ProactivePreparation([source], "Obsolete result");
        }));
        var service = new ProactiveService(f.Repository, f.Sources, f.Settings, new Clock(), prepare);
        var result = (await service.CheckAsync(f.Owner, value.Id, Ct))!;
        Assert.Null(result.PreparedReport);
        Assert.Contains(result.Receipts, receipt => receipt.Kind == "stale");
        Assert.Equal(Now.AddMinutes(1), result.NextCheckAt);
    }

    [Fact]
    public async Task Owner_pause_during_preparation_prevents_the_internal_mutation()
    {
        var f = new Fixture();
        var value = await f.Create();
        async Task<ProactivePreparation> Prepare()
        {
            var claimed = (await f.Repository.GetAsync(f.Owner, value.Id, Ct))!;
            await f.Service.CommandAsync(f.Owner, value.Id, "pause", claimed.Revision, Ct);
            return new([], "Replan", Mutation: new("planner.day", "{}", "{\"changed\":true}"));
        }
        var prepare = Fake<IProactivePreparationService>.Create(("PrepareAsync", _ => Prepare()));
        var service = new ProactiveService(f.Repository, f.Sources, f.Settings, new Clock(), prepare);
        var result = (await service.CheckAsync(f.Owner, value.Id, Ct))!;
        Assert.Equal("paused", result.Status);
        Assert.Equal(0, f.Repository.Mutations);
    }

    [Fact]
    public async Task Memory_resolution_requires_reviewed_versions_and_preserves_pinned_memories()
    {
        var f = new Fixture();
        var first = new ProactiveSource($"memory:{Guid.NewGuid()}", "Fact", "v1", "current", "Office is in Utrecht.", IsPinned: true);
        var second = new ProactiveSource($"memory:{Guid.NewGuid()}", "Fact", "v1", "current", "Office is in Rotterdam.");
        f.Sources.Values = [first, second];
        var value = await f.Service.CreateAsync(f.Owner, new("memory_conflict", "Office city", "Review the office city", [first.Ref, second.Ref]), Ct);
        var calls = 0;
        var review = Fake<Jarvis.Application.Memory.IMemoryReviewService>.Create(("CorrectAsync", _ => { calls++; return null; }));
        var resolver = new ProactiveMemoryResolution(f.Repository, f.Sources, review, new Clock());
        var versions = new Dictionary<string, string> { [first.Ref] = "v1", [second.Ref] = "v1" };
        await Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveAsync(f.Owner, value.Id,
            new(value.Revision, "keep_second", SourceVersions: versions), Ct));
        Assert.Equal(0, calls);
        f.Sources.Values = [first with { Version = "v2" }, second];
        await Assert.ThrowsAsync<ProactiveConflictException>(() => resolver.ResolveAsync(f.Owner, value.Id,
            new(value.Revision, "keep_first", SourceVersions: versions), Ct));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Keeping_both_memories_records_the_owners_context_on_each_version()
    {
        var f = new Fixture();
        var first = new ProactiveSource($"memory:{Guid.NewGuid()}", "Fact", "v1", "current", "Prefers short answers.");
        var second = new ProactiveSource($"memory:{Guid.NewGuid()}", "Fact", "v1", "current", "Prefers detailed answers.");
        f.Sources.Values = [first, second];
        var value = await f.Service.CreateAsync(f.Owner, new("memory_conflict", "Answer length", "Review answer length", [first.Ref, second.Ref]), Ct);
        var corrected = new List<string>();
        var review = Fake<Jarvis.Application.Memory.IMemoryReviewService>.Create(("CorrectAsync", args =>
        {
            var text = (string)args[2]!;
            corrected.Add(text);
            return new Jarvis.Application.Memory.MemoryCorrectionResult(Jarvis.Application.Memory.MemoryCorrectionOutcome.Corrected,
                new Jarvis.Domain.Memory.MemoryRecord(Guid.NewGuid(), f.Owner, "fact", text, .7f, .95f, "user", null, Now, Now, null, false));
        }));
        var resolver = new ProactiveMemoryResolution(f.Repository, f.Sources, review, new Clock());
        var result = (await resolver.ResolveAsync(f.Owner, value.Id, new(value.Revision, "both", "daily updates", "technical explanations",
            new Dictionary<string, string> { [first.Ref] = "v1", [second.Ref] = "v1" }), Ct))!;
        Assert.Equal("resolved", result.Status);
        Assert.Contains("Applies when: daily updates", corrected[0]);
        Assert.Contains("Applies when: technical explanations", corrected[1]);
        Assert.Contains(result.Receipts, r => r.Kind == "memory_resolution");
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private sealed class Reader : IProactiveSourceReader
    {
        public IReadOnlyList<ProactiveSource> Values { get; set; } = [];
        public int Reads { get; private set; }
        public Task<IReadOnlyList<ProactiveSource>> AvailableAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(Values);
        public Task<IReadOnlyList<ProactiveSource>> ReadAsync(Guid ownerId, Guid? profileId, IReadOnlyList<string> refs, CancellationToken ct)
        { Reads++; return Task.FromResult<IReadOnlyList<ProactiveSource>>(Values.Where(v => refs.Contains(v.Ref)).ToArray()); }
    }

    private sealed class Store : IProactiveRepository
    {
        public int Mutations { get; private set; }
        private readonly Dictionary<Guid, ProactiveCase> values = [];
        public Task<ProactiveCase> CreateAsync(ProactiveCase value, CancellationToken ct)
        {
            var existing = values.Values.FirstOrDefault(v => v.OwnerId == value.OwnerId && v.Fingerprint == value.Fingerprint);
            if (existing is not null) return Task.FromResult(existing);
            values[value.Id] = value; return Task.FromResult(value);
        }
        public Task<ProactiveCase?> GetAsync(Guid ownerId, Guid id, CancellationToken ct) =>
            Task.FromResult(values.TryGetValue(id, out var value) && value.OwnerId == ownerId ? value : null);
        public Task<IReadOnlyList<ProactiveCase>> ListAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProactiveCase>>(values.Values.Where(v => v.OwnerId == ownerId).ToArray());
        public Task<IReadOnlyList<ProactiveCase>> DueAsync(DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProactiveCase>>(values.Values.Where(v => v.NextCheckAt <= now).ToArray());
        public Task<bool> CompareExchangeAsync(ProactiveCase value, long revision, CancellationToken ct)
        {
            if (!values.TryGetValue(value.Id, out var current) || current.OwnerId != value.OwnerId || current.Revision != revision) return Task.FromResult(false);
            values[value.Id] = value; return Task.FromResult(true);
        }
        public Task<bool> ApplyAsync(ProactiveCase value, long revision, ProactiveSettingMutation mutation, CancellationToken ct)
        { Mutations++; return CompareExchangeAsync(value, revision, ct); }
    }

    private sealed class Settings : IOwnerSettingsStore
    {
        public AutonomySettings Value { get; set; } = AutonomySettings.Default;
        public Task<T?> GetAsync<T>(Guid ownerId, string section, CancellationToken ct) where T : class => Task.FromResult(Value as T);
        public Task SaveAsync<T>(Guid ownerId, string section, T value, CancellationToken ct) where T : class => Task.CompletedTask;
        public Task<IReadOnlyList<Guid>> ListOwnersAsync(string section, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
