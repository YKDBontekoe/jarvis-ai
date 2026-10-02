using Jarvis.Agents.Habits;
using Jarvis.Application.Audit;
using Jarvis.Application.Channels;
using Jarvis.Application.Conversations;
using Jarvis.Application.Habits;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Habits;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class HabitTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000dddd");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000eeee");

    // Thursday 1 October 2026, 18:00 in Amsterdam (UTC+2).
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public void Daily_streak_counts_back_from_today_and_stays_alive_until_the_day_is_over()
    {
        var habit = Habit(HabitCadences.Daily);
        var dates = new[] { Today.AddDays(-1), Today.AddDays(-2), Today.AddDays(-3), Today.AddDays(-6),
            Today.AddDays(-7), Today.AddDays(-8), Today.AddDays(-9) };

        var open = HabitStreaks.Compute(habit, dates, Today);
        var done = HabitStreaks.Compute(habit, dates.Append(Today), Today);
        var broken = HabitStreaks.Compute(habit, dates.Skip(1), Today);

        Assert.Equal(3, open.CurrentStreak);
        Assert.False(open.DoneToday);
        Assert.Equal(4, open.BestStreak);
        Assert.Equal(4, done.CurrentStreak);
        Assert.True(done.DoneToday);
        Assert.Equal(0, broken.CurrentStreak);
        Assert.Equal("days", open.StreakUnit);
    }

    [Fact]
    public void Weekly_streak_counts_weeks_that_met_the_target()
    {
        var habit = Habit(HabitCadences.Weekly, target: 2);
        // Weeks start on Monday 28 September. Last two weeks met the target, the week before did not.
        var dates = new[]
        {
            new DateOnly(2026, 9, 28),
            new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 24),
            new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20),
            new DateOnly(2026, 9, 9)
        };

        var stats = HabitStreaks.Compute(habit, dates, Today);
        var metThisWeek = HabitStreaks.Compute(habit, dates.Append(Today), Today);

        Assert.Equal(new DateOnly(2026, 9, 28), HabitStreaks.WeekStart(Today));
        Assert.Equal(1, stats.ThisWeekCount);
        Assert.Equal(2, stats.CurrentStreak);
        Assert.Equal(3, metThisWeek.CurrentStreak);
        Assert.Equal("weeks", stats.StreakUnit);
        Assert.True(new HabitSummary(habit, stats).IsOpenToday);
        Assert.False(new HabitSummary(habit, metThisWeek).IsOpenToday);
    }

    [Fact]
    public async Task Creating_a_habit_validates_and_starts_the_evening_question()
    {
        var (service, repository, scheduler, _, settings) = Create();

        var created = await service.CreateAsync(Owner, new HabitDraft("  Sporten ", "🏃", "weekly", 3),
            "Europe/Amsterdam", default);
        var duplicate = await service.CreateAsync(Owner, new HabitDraft("sporten!", null, null, null), null, default);
        var badTarget = await service.CreateAsync(Owner, new HabitDraft("Run", null, "weekly", 9), null, default);
        var badCadence = await service.CreateAsync(Owner, new HabitDraft("Run", null, "monthly", null), null, default);

        Assert.True(created.Succeeded);
        Assert.Equal("Sporten", created.Value!.Habit.Name);
        Assert.Equal(3, created.Value.Habit.TargetPerWeek);
        Assert.Equal(HabitFailure.Conflict, duplicate.Failure);
        Assert.Equal("targetPerWeek", badTarget.Field);
        Assert.Equal("cadence", badCadence.Field);
        Assert.Single(repository.Habits);
        Assert.Equal([Owner], scheduler.Scheduled);
        var saved = await settings.GetAsync<HabitSettings>(Owner, HabitSettingsSections.Settings, default);
        Assert.Equal("Europe/Amsterdam", saved!.TimeZoneId);
    }

    [Fact]
    public async Task Checking_in_is_idempotent_owner_scoped_and_limited_to_the_past_week()
    {
        var (service, repository, _, _, _) = Create();
        var habit = (await service.CreateAsync(Owner, new HabitDraft("Read", null, null, null), null, default))
            .Value!.Habit;

        var first = await service.SetDoneAsync(habit.Id, Owner, null, true, HabitSources.App, default);
        var second = await service.SetDoneAsync(habit.Id, Owner, null, true, HabitSources.App, default);
        var yesterday = await service.SetDoneAsync(habit.Id, Owner, Today.AddDays(-1), true, HabitSources.App,
            default);
        var future = await service.SetDoneAsync(habit.Id, Owner, Today.AddDays(1), true, HabitSources.App, default);
        var tooOld = await service.SetDoneAsync(habit.Id, Owner, Today.AddDays(-8), true, HabitSources.App, default);
        var otherOwner = await service.SetDoneAsync(habit.Id, Other, null, true, HabitSources.App, default);

        Assert.True(first.Value!.Stats.DoneToday);
        Assert.Equal(1, second.Value!.Stats.TotalCheckIns);
        Assert.Equal(2, yesterday.Value!.Stats.CurrentStreak);
        Assert.Equal("date", future.Field);
        Assert.Equal("date", tooOld.Field);
        Assert.Equal(HabitFailure.NotFound, otherOwner.Failure);
        Assert.Equal(2, repository.CheckIns.Count);

        var undone = await service.SetDoneAsync(habit.Id, Owner, null, false, HabitSources.App, default);
        Assert.False(undone.Value!.Stats.DoneToday);
        Assert.Equal(1, undone.Value.Stats.CurrentStreak);
    }

    [Fact]
    public async Task Chat_checks_habits_in_by_name_and_reports_misses()
    {
        var (service, _, _, _, _) = Create();
        await service.CreateAsync(Owner, new HabitDraft("Sporten", "🏃", null, null), null, default);
        await service.CreateAsync(Owner, new HabitDraft("Read 20 pages", null, null, null), null, default);
        await service.CreateAsync(Owner, new HabitDraft("Read the news", null, null, null), null, default);
        var audit = new RecordingAudit();
        var tools = new HabitAgentTools(service, audit, new FixedUser(), NullLogger.Instance);

        var reply = await tools.CheckInHabitsAsync(["sporten", "read", "guitar"]);
        var again = await tools.CheckInHabitsAsync(["Sporten"]);

        Assert.Contains("\"Sporten\" (daily): done today, streak 1 days", reply);
        Assert.Contains("No habit matches \"guitar\"", reply);
        Assert.Contains("\"read\" matches more than one habit", reply);
        Assert.Contains("already checked in", again);
        var metadata = Assert.Single(audit.Metadata);
        Assert.DoesNotContain("Sporten", metadata);
        Assert.Equal(["habit.checked_in"], audit.Actions);
    }

    [Fact]
    public void Next_check_in_is_tonight_then_tomorrow_once_asked()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var time = new TimeOnly(20, 30);

        var tonight = HabitService.NextCheckIn(Now, time, zone, null);
        var asked = HabitService.NextCheckIn(Now, time, zone, Today);
        var justMissed = HabitService.NextCheckIn(Now.AddHours(3), time, zone, null);
        var longMissed = HabitService.NextCheckIn(Now.AddHours(5), time, zone, null);

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 18, 30, 0, TimeSpan.Zero), tonight.FireAt);
        Assert.Equal(Today, tonight.LocalDate);
        Assert.Equal(Today.AddDays(1), asked.LocalDate);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 18, 30, 0, TimeSpan.Zero), asked.FireAt);
        Assert.Equal(Now.AddHours(3), justMissed.FireAt);
        Assert.Equal(Today.AddDays(1), longMissed.LocalDate);
    }

    [Fact]
    public async Task Evening_question_names_open_habits_once_and_stops_when_turned_off()
    {
        var (service, _, scheduler, notifications, _) = Create();
        var run = (await service.CreateAsync(Owner, new HabitDraft("Run", "🏃", null, null), null, default))
            .Value!.Habit;
        var read = (await service.CreateAsync(Owner, new HabitDraft("Read", null, null, null), null, default))
            .Value!.Habit;
        await service.SetDoneAsync(read.Id, Owner, null, true, HabitSources.App, default);

        Assert.True(await service.DeliverCheckInAsync(Owner, Today, default));
        Assert.True(await service.DeliverCheckInAsync(Owner, Today, default));

        var sent = Assert.Single(notifications.Sent);
        Assert.Equal("habit.checkin", sent.Type);
        Assert.Contains("🏃 Run", sent.Body);
        Assert.DoesNotContain("Read", sent.Body);
        Assert.Equal(ChannelNotificationCategories.CheckIns, ChannelNotificationCategories.For(sent.Type));

        await service.SaveSettingsAsync(Owner, new HabitSettings(false), default);
        Assert.Equal([Owner], scheduler.Cancelled);
        Assert.False(await service.DeliverCheckInAsync(Owner, Today.AddDays(1), default));
        Assert.False((await service.ResolveCheckInAsync(Owner, Now, default)).Active);
        _ = run;
    }

    [Fact]
    public void Settings_reject_bad_times_and_zones()
    {
        Assert.Equal("07:05", new HabitSettings(true, " 07:05 ").Normalize().CheckInTime);
        Assert.Throws<ArgumentException>(() => new HabitSettings(true, "25:00").Normalize());
        Assert.Throws<ArgumentException>(() => new HabitSettings(true, "20:00", "Mars/Olympus").Normalize());
    }

    private static Habit Habit(string cadence, int target = 1) =>
        new(Guid.NewGuid(), Owner, "Habit", null, cadence, target, null, Now, Now);

    private static (HabitService Service, InMemoryHabits Repository, RecordingScheduler Scheduler,
        RecordingNotifications Notifications, InMemorySettingsStore Settings) Create()
    {
        var repository = new InMemoryHabits();
        var scheduler = new RecordingScheduler();
        var notifications = new RecordingNotifications();
        var settings = new InMemorySettingsStore();
        var briefings = Fake<IDailyBriefingRepository>.Create(("GetAsync", _ =>
            new DailyBriefingPreferenceRecord(Owner, true, new TimeOnly(8, 0), "Europe/Amsterdam", "wf", null, null)));
        return (new HabitService(repository, settings, briefings, notifications, scheduler, new FixedClock(Now)),
            repository, scheduler, notifications, settings);
    }

    private sealed class InMemoryHabits : IHabitRepository
    {
        public List<Habit> Habits { get; } = [];
        public List<HabitCheckIn> CheckIns { get; } = [];

        public Task<IReadOnlyList<Habit>> ListAsync(Guid ownerId, bool includeArchived,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Habit>>(Habits
                .Where(x => x.OwnerId == ownerId && (includeArchived || !x.IsArchived)).ToArray());

        public Task<Habit?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Habits.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task AddAsync(Habit habit, CancellationToken cancellationToken)
        {
            Habits.Add(habit);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(Habit habit, CancellationToken cancellationToken)
        {
            var index = Habits.FindIndex(x => x.Id == habit.Id && x.OwnerId == habit.OwnerId);
            if (index < 0) return Task.FromResult(false);
            Habits[index] = habit;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            CheckIns.RemoveAll(x => x.HabitId == id && x.OwnerId == ownerId);
            return Task.FromResult(Habits.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);
        }

        public Task<IReadOnlyList<HabitCheckIn>> ListCheckInsAsync(Guid ownerId, IReadOnlyCollection<Guid> habitIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HabitCheckIn>>(CheckIns
                .Where(x => x.OwnerId == ownerId && habitIds.Contains(x.HabitId)).ToArray());

        public Task<bool> AddCheckInAsync(HabitCheckIn checkIn, CancellationToken cancellationToken)
        {
            if (CheckIns.Any(x => x.HabitId == checkIn.HabitId && x.Date == checkIn.Date)) return Task.FromResult(false);
            CheckIns.Add(checkIn);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteCheckInAsync(Guid ownerId, Guid habitId, DateOnly date,
            CancellationToken cancellationToken) =>
            Task.FromResult(CheckIns.RemoveAll(x => x.OwnerId == ownerId && x.HabitId == habitId && x.Date == date) > 0);

        public Task<IReadOnlyList<Guid>> ListOwnersWithActiveHabitsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Habits.Where(x => !x.IsArchived).Select(x => x.OwnerId).Distinct()
                .ToArray());
    }

    private sealed class RecordingScheduler : IHabitCheckInScheduler
    {
        public List<Guid> Scheduled { get; } = [];
        public List<Guid> Cancelled { get; } = [];

        public Task ScheduleHabitCheckInAsync(Guid ownerId, CancellationToken cancellationToken)
        {
            if (!Scheduled.Contains(ownerId)) Scheduled.Add(ownerId);
            return Task.CompletedTask;
        }

        public Task CancelHabitCheckInAsync(Guid ownerId, CancellationToken cancellationToken)
        {
            Cancelled.Add(ownerId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNotifications : INotificationRepository
    {
        public List<NotificationRecord> Sent { get; } = [];

        public Task<IReadOnlyList<NotificationRecord>> ListNotificationsAsync(Guid ownerId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotificationRecord>>(Sent);

        public Task<NotificationRecord?> GetNotificationAsync(Guid id, Guid ownerId,
            CancellationToken cancellationToken) => Task.FromResult(Sent.FirstOrDefault(x => x.Id == id));

        public Task<bool> MarkReadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<NotificationRecord> CreateAsync(Guid ownerId, string type, string title, string body,
            Guid? sourceId, CancellationToken cancellationToken)
        {
            var record = new NotificationRecord(Guid.NewGuid(), type, title, body, sourceId, Now, null);
            Sent.Add(record);
            return Task.FromResult(record);
        }
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingAudit : IAuditEventStore
    {
        public List<string> Actions { get; } = [];
        public List<string> Metadata { get; } = [];

        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass,
            bool success, Guid? approvalId, string? metadataJson, CancellationToken cancellationToken,
            Guid? agentRunId = null)
        {
            Actions.Add(action);
            Metadata.Add(metadataJson ?? "");
            return Task.FromResult(new AuditEventRecord(Guid.NewGuid(), agentRunId, tool, action, riskClass,
                approvalId, DateTimeOffset.UtcNow, success, metadataJson));
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditEventRecord>>([]);
    }
}
