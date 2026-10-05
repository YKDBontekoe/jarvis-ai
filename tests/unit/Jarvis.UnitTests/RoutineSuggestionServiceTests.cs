using System.Text.Json;
using Jarvis.Application.Automations;
using Jarvis.Application.Routines;
using Jarvis.Application.Timeline;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Routines;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class RoutineSuggestionServiceTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000bbbb");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000cccc");
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public FakeTimeProvider Clock { get; } = new(Now);
        public InMemoryRoutineRepository Repository { get; } = new();
        public RecordingNotifications Notifications { get; } = new();
        public InMemorySettingsStore Settings { get; } = new();
        public RecordingAutomations Automations { get; } = new();
        public List<TimelineEvent> Events { get; } = [];
        public RoutineSuggestionService Service { get; }

        public Harness()
        {
            for (var i = 1; i <= 40; i++)
                Events.Add(new TimelineEvent($"j{i}", TimelineKinds.Journal, "My day", null,
                    Now.Date.AddDays(-i).AddHours(22).ToUniversalDateTime(), DateOnly.FromDateTime(Now.Date.AddDays(-i))));
            Service = new RoutineSuggestionService(Repository, [new FixedSource(Events)], Automations,
                new NoBriefings(), Notifications, Settings, Clock);
        }
    }

    [Fact]
    public async Task Refresh_stores_suggestions_and_notifies_once_a_week()
    {
        var h = new Harness();

        var first = await h.Service.RefreshAsync(Owner, force: false, CancellationToken.None);

        var view = Assert.Single(first);
        Assert.Contains("journal", view.Title);
        var note = Assert.Single(h.Notifications.Created);
        Assert.Equal(RoutineRules.NotificationType, note.Type);
        Assert.Equal(view.Id, note.SourceId);

        // A second refresh within a day is throttled, and a forced one never notifies again within a week.
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.Service.RefreshAsync(Owner, force: false, CancellationToken.None);
        await h.Service.RefreshAsync(Owner, force: true, CancellationToken.None);
        Assert.Single(h.Notifications.Created);
        Assert.Single(await h.Repository.ListAsync(Owner, CancellationToken.None));
    }

    [Fact]
    public async Task Accept_creates_a_draft_automation_once_and_remembers_it()
    {
        var h = new Harness();
        var view = Assert.Single(await h.Service.RefreshAsync(Owner, true, CancellationToken.None));

        var result = await h.Service.AcceptAsync(view.Id, Owner, CancellationToken.None);
        var again = await h.Service.AcceptAsync(view.Id, Owner, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(result.AutomationId, again!.AutomationId);
        var created = Assert.Single(h.Automations.Created);
        Assert.Equal(Owner, created.OwnerId);
        AutomationRuleValidator.Validate(created.Request.ParseDefinition());
        Assert.Empty(await h.Service.ListAsync(Owner, CancellationToken.None));
    }

    [Fact]
    public async Task A_dismissed_pattern_is_never_offered_again()
    {
        var h = new Harness();
        var view = Assert.Single(await h.Service.RefreshAsync(Owner, true, CancellationToken.None));

        Assert.True(await h.Service.DismissAsync(view.Id, Owner, CancellationToken.None));
        h.Clock.Advance(TimeSpan.FromDays(3));
        var after = await h.Service.RefreshAsync(Owner, true, CancellationToken.None);

        Assert.Empty(after);
        Assert.Null(await h.Service.AcceptAsync(view.Id, Owner, CancellationToken.None));
        Assert.Empty(h.Automations.Created);
    }

    [Fact]
    public async Task A_pattern_that_stops_showing_up_is_dropped()
    {
        var h = new Harness();
        Assert.Single(await h.Service.RefreshAsync(Owner, true, CancellationToken.None));

        h.Events.Clear();
        h.Clock.Advance(TimeSpan.FromDays(1));

        Assert.Empty(await h.Service.RefreshAsync(Owner, true, CancellationToken.None));
        Assert.Empty(await h.Repository.ListAsync(Owner, CancellationToken.None));
    }

    [Fact]
    public async Task Suggestions_belong_to_one_owner()
    {
        var h = new Harness();
        var view = Assert.Single(await h.Service.RefreshAsync(Owner, true, CancellationToken.None));

        Assert.Null(await h.Service.AcceptAsync(view.Id, Other, CancellationToken.None));
        Assert.False(await h.Service.DismissAsync(view.Id, Other, CancellationToken.None));
        Assert.Empty(await h.Repository.ListAsync(Other, CancellationToken.None));
        Assert.Empty(h.Automations.Created);
    }

    [Fact]
    public async Task A_failing_source_does_not_stop_the_others()
    {
        var h = new Harness();
        var service = new RoutineSuggestionService(h.Repository, [new ThrowingSource(), new FixedSource(h.Events)],
            h.Automations, new NoBriefings(), h.Notifications, h.Settings, h.Clock);

        Assert.Single(await service.RefreshAsync(Owner, true, CancellationToken.None));
    }

    [Fact]
    public async Task List_refreshes_when_the_last_run_is_over_a_day_old()
    {
        var h = new Harness();
        Assert.Single(await h.Service.ListAsync(Owner, CancellationToken.None));

        h.Events.Clear();
        h.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Single(await h.Service.ListAsync(Owner, CancellationToken.None));

        h.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Empty(await h.Service.ListAsync(Owner, CancellationToken.None));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan by) => current += by;
    }

    private sealed class InMemoryRoutineRepository : IRoutineSuggestionRepository
    {
        private readonly List<RoutineSuggestionEntry> _rows = [];

        public Task<IReadOnlyList<RoutineSuggestionEntry>> ListAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RoutineSuggestionEntry>>(_rows.Where(x => x.OwnerId == ownerId).ToArray());

        public Task<RoutineSuggestionEntry?> GetAsync(Guid id, Guid ownerId, CancellationToken ct) =>
            Task.FromResult(_rows.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task AddRangeAsync(IReadOnlyList<RoutineSuggestionEntry> entries, CancellationToken ct)
        {
            _rows.AddRange(entries);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(RoutineSuggestionEntry entry, CancellationToken ct)
        {
            var index = _rows.FindIndex(x => x.Id == entry.Id && x.OwnerId == entry.OwnerId);
            if (index < 0) return Task.FromResult(false);
            _rows[index] = entry;
            return Task.FromResult(true);
        }

        public Task<int> DeleteManyAsync(Guid ownerId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult(_rows.RemoveAll(x => x.OwnerId == ownerId && ids.Contains(x.Id)));
    }

    private sealed class FixedSource(List<TimelineEvent> events) : ITimelineSource
    {
        public IReadOnlySet<string> Kinds { get; } = new HashSet<string>(TimelineKinds.All);

        public Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TimelineEvent>>(events.ToArray());
    }

    private sealed class ThrowingSource : ITimelineSource
    {
        public IReadOnlySet<string> Kinds { get; } = new HashSet<string>(TimelineKinds.All);

        public Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class RecordingAutomations : IAutomationRuleService
    {
        public List<(Guid OwnerId, SaveAutomationRuleRequest Request)> Created { get; } = [];

        public Task<AutomationRuleRecord> CreateAsync(Guid ownerId, SaveAutomationRuleRequest request,
            CancellationToken ct)
        {
            Created.Add((ownerId, request));
            return Task.FromResult(new AutomationRuleRecord(Guid.NewGuid(), ownerId, request.Name, 1,
                request.Definition.GetRawText(), "draft", "wf", null, null, null, null, Now, Now));
        }

        public Task<AutomationRuleRecord?> UpdateAsync(Guid id, Guid ownerId, SaveAutomationRuleRequest request,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AutomationRuleRecord>> ListAsync(Guid ownerId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<AutomationRuleRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<AutomationValidationResult> ValidateAsync(AutomationRuleDefinition definition) =>
            throw new NotSupportedException();
        public Task<AutomationRuleRecord?> EnableAsync(Guid id, Guid ownerId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<AutomationRuleRecord?> DisableAsync(Guid id, Guid ownerId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<AutomationRunRecord?> TestRunAsync(Guid id, Guid ownerId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<AutomationRunRecord?> ManualRunAsync(Guid id, Guid ownerId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class NoBriefings : IDailyBriefingRepository
    {
        public Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult<DailyBriefingPreferenceRecord?>(null);
        public Task<(DailyBriefingPreferenceRecord Preference, string PreviousWorkflowId)> SaveAsync(Guid ownerId,
            SaveDailyBriefingRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DailyBriefingPreferenceRecord>> ListPendingForSchedulingAsync(
            CancellationToken ct) => throw new NotSupportedException();
        public Task<int> RequeueStaleEnabledAsync(DateTimeOffset utcNow, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task MarkScheduleDispatchedAsync(Guid ownerId, string workflowId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> DeliverAsync(DailyBriefingActivityInput input, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}

internal static class RoutineTestTime
{
    public static DateTimeOffset ToUniversalDateTime(this DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
}
