using Jarvis.Agents.Planner;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Application.Planner;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class DayPlannerTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000dd01");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000dd02");
    private const string Amsterdam = "Europe/Amsterdam";

    // 2026-10-02 09:00 in Amsterdam (UTC+2).
    private static readonly DateTimeOffset Morning = new(2026, 10, 2, 7, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Local(int hour, int minute = 0) =>
        new DateTimeOffset(2026, 10, 2, hour, minute, 0, TimeSpan.FromHours(2)).ToUniversalTime();

    [Fact]
    public void Free_slots_skip_busy_time_and_short_gaps()
    {
        var window = new TimeRange(Local(9), Local(12));
        var slots = DayPlanScheduler.FreeSlots(window,
        [
            new TimeRange(Local(9, 30), Local(10)),
            new TimeRange(Local(9, 45), Local(10, 30)),
            new TimeRange(Local(10, 40), Local(11))
        ]);

        Assert.Equal(
        [
            new TimeRange(Local(9), Local(9, 30)),
            new TimeRange(Local(11), Local(12))
        ], slots);
    }

    [Fact]
    public void Placing_keeps_order_uses_buffers_and_skips_what_does_not_fit()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var tooLong = Guid.NewGuid();
        var placed = DayPlanScheduler.Place(new TimeRange(Local(9, 2), Local(12)),
            [new TimeRange(Local(10), Local(11))],
            [(first, TimeSpan.FromMinutes(45)), (second, TimeSpan.FromMinutes(30)), (tooLong, TimeSpan.FromHours(2))]);

        // 09:02 rounds up to 09:05; the event ends at 11:00 and gets 5 minutes of room.
        Assert.Equal(Local(9, 5), placed[first]);
        Assert.Equal(Local(11, 5), placed[second]);
        Assert.False(placed.ContainsKey(tooLong));
    }

    [Fact]
    public async Task Plan_my_day_fits_todos_around_calendar_events_and_reports_leftovers()
    {
        var (service, _, calendar, _) = Create(Morning);
        calendar.Events.Add(new CalendarEventRecord("Standup", Local(9, 30), Local(10)));
        calendar.Events.Add(new CalendarEventRecord("Lunch", Local(12), Local(13)));
        calendar.Events.Add(new CalendarEventRecord("Holiday", new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero)));
        var write = await service.AddItemAsync(Owner, new AddDayPlanItemRequest("Write report", 90), Amsterdam, default);
        var mail = await service.AddItemAsync(Owner, new AddDayPlanItemRequest("Inbox zero", 20), Amsterdam, default);
        var huge = await service.AddItemAsync(Owner, new AddDayPlanItemRequest("Rebuild shed", 480), Amsterdam, default);

        var result = await service.PlanAsync(Owner, Amsterdam, default);

        Assert.Equal(2, result.Scheduled);
        var items = result.Today.Items.ToDictionary(item => item.Id);
        Assert.Equal(Local(10, 5), items[write.Id].StartAt);
        Assert.Equal(Local(9), items[mail.Id].StartAt);
        Assert.Null(items[huge.Id].StartAt);
        Assert.Equal([huge.Id], result.DidNotFit.Select(item => item.Id));
        // The all-day event is on the timeline but does not block the morning.
        Assert.Contains(result.Today.Entries, entry => entry.Title == "Holiday");
        Assert.Contains(result.Today.Entries, entry => entry.Kind == DayTimelineKinds.Focus && entry.Title == "Write report");
        Assert.True(result.Today.CalendarConnected);
        Assert.NotNull(result.Today.PlannedAt);
    }

    [Fact]
    public async Task Done_items_keep_their_block_and_open_ones_move_when_replanned_later()
    {
        var (service, _, _, clock) = Create(Morning);
        var done = await service.AddItemAsync(Owner, new AddDayPlanItemRequest("Done thing", 30), Amsterdam, default);
        var missed = await service.AddItemAsync(Owner, new AddDayPlanItemRequest("Missed thing", 30), Amsterdam, default);
        await service.PlanAsync(Owner, Amsterdam, default);
        await service.UpdateItemAsync(Owner, done.Id, new UpdateDayPlanItemRequest(Done: true), Amsterdam, default);

        clock.Now = Local(14, 3);
        var result = await service.PlanAsync(Owner, Amsterdam, default);

        var items = result.Today.Items.ToDictionary(item => item.Id);
        Assert.Equal(Local(9), items[done.Id].StartAt);
        Assert.Equal(Local(14, 5), items[missed.Id].StartAt);
    }

    [Fact]
    public async Task A_new_day_carries_open_todos_over_without_their_blocks()
    {
        var (service, _, _, clock) = Create(Morning);
        var open = await service.AddItemAsync(Owner, new AddDayPlanItemRequest("Call plumber"), Amsterdam, default);
        var done = await service.AddItemAsync(Owner, new AddDayPlanItemRequest("Pay rent"), Amsterdam, default);
        await service.PlanAsync(Owner, Amsterdam, default);
        await service.UpdateItemAsync(Owner, done.Id, new UpdateDayPlanItemRequest(Done: true), Amsterdam, default);

        clock.Now = Morning.AddDays(1);
        var tomorrow = await service.GetTodayAsync(Owner, Amsterdam, default);

        Assert.Equal(new DateOnly(2026, 10, 3), tomorrow.Date);
        var item = Assert.Single(tomorrow.Items);
        Assert.Equal(open.Id, item.Id);
        Assert.Null(item.StartAt);
        Assert.Null(tomorrow.PlannedAt);
    }

    [Fact]
    public async Task Timeline_shows_only_todays_reminders_and_plans_stay_per_owner()
    {
        var (service, reminders, _, _) = Create(Morning);
        reminders.Items.Add(Reminder("Take pills", Local(20)));
        reminders.Items.Add(Reminder("Tomorrow thing", Local(20).AddDays(1)));
        reminders.Items.Add(Reminder("Cancelled", Local(15)) with { Status = "cancelled" });
        await service.AddItemAsync(Owner, new AddDayPlanItemRequest("Mine"), Amsterdam, default);

        var today = await service.GetTodayAsync(Owner, Amsterdam, default);
        var other = await service.GetTodayAsync(Other, Amsterdam, default);

        var reminder = Assert.Single(today.Entries, entry => entry.Kind == DayTimelineKinds.Reminder);
        Assert.Equal("Take pills", reminder.Title);
        Assert.Single(today.Items);
        Assert.Empty(other.Items);
        Assert.False(today.CalendarConnected);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_a_clear_message()
    {
        var (service, _, _, _) = Create(Morning);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddItemAsync(Owner, new AddDayPlanItemRequest("   "), Amsterdam, default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddItemAsync(Owner, new AddDayPlanItemRequest("Nap", 2), Amsterdam, default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveHoursAsync(Owner, new SaveDayHoursRequest(new TimeOnly(17, 0), new TimeOnly(9, 0)),
                Amsterdam, default));
    }

    [Fact]
    public async Task A_calendar_that_fails_to_load_is_reported_instead_of_breaking_the_day()
    {
        var (service, _, calendar, _) = Create(Morning);
        calendar.Fail = true;

        var today = await service.GetTodayAsync(Owner, Amsterdam, default);

        Assert.True(today.CalendarConnected);
        Assert.True(today.CalendarUnavailable);
    }

    [Fact]
    public async Task Agent_tools_add_plan_and_describe_the_day()
    {
        var (service, _, calendar, _) = Create(Morning);
        calendar.Events.Add(new CalendarEventRecord("Dentist", Local(9), Local(10)));
        // The app sends the phone's zone; chat tools then plan in that same zone.
        await service.GetTodayAsync(Owner, Amsterdam, default);
        var tools = new PlannerAgentTools(service, new FixedUser());

        var added = await tools.AddToDayPlanAsync("Groceries", 40);
        var planned = await tools.PlanMyDayAsync();
        var day = await tools.GetTodayPlanAsync();

        Assert.Contains("Groceries, 40 min", added);
        Assert.Contains("Planned 1 to-do.", planned);
        Assert.Contains("10:05-10:45 focus: Groceries", planned);
        Assert.Contains("09:00-10:00 event: Dentist", day);
        Assert.Contains("untrusted", day);
        Assert.Contains(Amsterdam, day);
        Assert.Contains("could not add", await tools.AddToDayPlanAsync("x", 1000));
        Assert.Equal("That is not a valid day plan item ID.", await tools.CompleteDayPlanItemAsync("nope"));
    }

    [Fact]
    public void Planner_guidance_routes_calendar_writes_through_approval()
    {
        Assert.Contains("InvokeMcpTool", PlannerContextContributor.Guidance);
        Assert.Contains("approval", PlannerContextContributor.Guidance);
    }

    private static (DayPlannerService Service, FakeReminders Reminders, FakeCalendar Calendar, MutableClock Clock)
        Create(DateTimeOffset now)
    {
        var clock = new MutableClock(now);
        var reminders = new FakeReminders();
        var calendar = new FakeCalendar();
        var credentials = new InMemoryCredentialStore();
        var service = new DayPlannerService(new InMemorySettingsStore(), calendar, new CalendarAwareCredentials(
            credentials, calendar), reminders, new NoBriefing(), clock);
        return (service, reminders, calendar, clock);
    }

    private static ReminderRecord Reminder(string title, DateTimeOffset dueAt) =>
        new(Guid.NewGuid(), Owner, title, dueAt, "wf", "pending", Morning, null);

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeCalendar : ICalendarFeed
    {
        public List<CalendarEventRecord> Events { get; } = [];
        public bool Fail { get; set; }

        public Task<IReadOnlyList<CalendarEventRecord>> ListUpcomingAsync(Guid ownerId, DateTimeOffset from,
            DateTimeOffset until, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("calendar down");
            return Task.FromResult<IReadOnlyList<CalendarEventRecord>>(Events
                .Where(item => item.StartAt >= from && item.StartAt < until).ToArray());
        }
    }

    /// <summary>Reports the calendar as connected for the owner once a test adds events or a failure.</summary>
    private sealed class CalendarAwareCredentials(InMemoryCredentialStore inner, FakeCalendar calendar)
        : IIntegrationCredentialStore
    {
        public Task<IReadOnlyList<IntegrationCredentialStatus>> ListAsync(Guid ownerId,
            CancellationToken cancellationToken) => inner.ListAsync(ownerId, cancellationToken);

        public Task<IntegrationCredentialStatus?> GetStatusAsync(Guid ownerId, string provider,
            CancellationToken cancellationToken) =>
            ownerId == Owner && (calendar.Events.Count > 0 || calendar.Fail)
                ? Task.FromResult<IntegrationCredentialStatus?>(new IntegrationCredentialStatus(provider, ["ics_url"],
                    DateTimeOffset.UtcNow))
                : inner.GetStatusAsync(ownerId, provider, cancellationToken);

        public Task SaveAsync(Guid ownerId, string provider, IReadOnlyDictionary<string, string> secrets,
            CancellationToken cancellationToken) => inner.SaveAsync(ownerId, provider, secrets, cancellationToken);

        public Task SaveSecretAsync(Guid ownerId, string provider, string secretName, string value,
            CancellationToken cancellationToken) =>
            inner.SaveSecretAsync(ownerId, provider, secretName, value, cancellationToken);

        public Task<bool> DeleteSecretAsync(Guid ownerId, string provider, string secretName,
            CancellationToken cancellationToken) =>
            inner.DeleteSecretAsync(ownerId, provider, secretName, cancellationToken);

        public Task<IReadOnlyDictionary<string, string>?> GetSecretsAsync(Guid ownerId, string provider,
            CancellationToken cancellationToken) => inner.GetSecretsAsync(ownerId, provider, cancellationToken);

        public Task<bool> DeleteAsync(Guid ownerId, string provider, CancellationToken cancellationToken) =>
            inner.DeleteAsync(ownerId, provider, cancellationToken);
    }

    private sealed class FakeReminders : IReminderService
    {
        public List<ReminderRecord> Items { get; } = [];

        public Task<IReadOnlyList<ReminderRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReminderRecord>>(Items.Where(item => item.OwnerId == ownerId).ToArray());

        public Task<ReminderRecord> CreateAsync(Guid ownerId, CreateReminderRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReminderRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ReminderRecord?> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ReminderRecord?> SnoozeAsync(Guid id, Guid ownerId, DateTimeOffset dueAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReminderRecord?> MarkDoneAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoBriefing : IDailyBriefingRepository
    {
        public Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<DailyBriefingPreferenceRecord?>(null);
        public Task<(DailyBriefingPreferenceRecord Preference, string PreviousWorkflowId)> SaveAsync(Guid ownerId,
            SaveDailyBriefingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DailyBriefingPreferenceRecord>> ListPendingForSchedulingAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RequeueStaleEnabledAsync(DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task MarkScheduleDispatchedAsync(Guid ownerId, string workflowId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> DeliverAsync(DailyBriefingActivityInput input, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
