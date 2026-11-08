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
    public async Task Chat_archives_and_restores_a_habit_by_name_without_losing_history()
    {
        var (service, _, _, _, _) = Create();
        var created = await service.CreateAsync(Owner, new HabitDraft("Meditate", null, null, null), null, default);
        await service.SetDoneAsync(created.Value!.Habit.Id, Owner, null, true, HabitSources.App, default);
        var audit = new RecordingAudit();
        var tools = new HabitAgentTools(service, audit, new FixedUser(), NullLogger.Instance);

        var archived = await tools.ArchiveHabitAsync("meditat");
        var again = await tools.ArchiveHabitAsync("Meditate");
        var restored = await tools.ArchiveHabitAsync("Meditate", archive: false);

        Assert.Contains("Archived \"Meditate\"", archived, StringComparison.Ordinal);
        Assert.Contains("no active habits to archive", again, StringComparison.Ordinal);
        Assert.Contains("is tracked again", restored, StringComparison.Ordinal);
        var summary = Assert.Single(await service.ListAsync(Owner, false, default));
        Assert.Equal(1, summary.Stats.TotalCheckIns);
        Assert.Equal(["habit.archived", "habit.restored"], audit.Actions);
        Assert.All(audit.Metadata, item => Assert.DoesNotContain("Meditate", item));
    }

    [Fact]
    public async Task Archiving_asks_which_habit_when_the_name_is_ambiguous_and_ignores_other_owners()
    {
        var (service, _, _, _, _) = Create();
        await service.CreateAsync(Owner, new HabitDraft("Read 20 pages", null, null, null), null, default);
        await service.CreateAsync(Owner, new HabitDraft("Read the news", null, null, null), null, default);
        await service.CreateAsync(Other, new HabitDraft("Piano", null, null, null), null, default);
        var tools = new HabitAgentTools(service, new RecordingAudit(), new FixedUser(), NullLogger.Instance);

        Assert.Contains("matches more than one habit", await tools.ArchiveHabitAsync("read"), StringComparison.Ordinal);
        Assert.Contains("No active habit matches", await tools.ArchiveHabitAsync("Piano"), StringComparison.Ordinal);
        Assert.Equal(2, (await service.ListAsync(Owner, false, default)).Count);
        Assert.Single(await service.ListAsync(Other, false, default));
    }

    [Fact]
    public async Task Chat_deletes_a_habit_only_by_its_exact_name_and_never_another_owners()
    {
        var (service, _, _, _, _) = Create();
        await service.CreateAsync(Owner, new HabitDraft("Read 20 pages", null, null, null), null, default);
        await service.CreateAsync(Owner, new HabitDraft("Read the news", null, null, null), null, default);
        await service.CreateAsync(Other, new HabitDraft("Piano", null, null, null), null, default);
        var audit = new RecordingAudit();
        var tools = new HabitAgentTools(service, audit, new FixedUser(), NullLogger.Instance);

        Assert.StartsWith("No habit has that exact name", await tools.DeleteHabitAsync("read"), StringComparison.Ordinal);
        Assert.StartsWith("No habit has that exact name", await tools.DeleteHabitAsync("Piano"), StringComparison.Ordinal);
        Assert.Equal("The habit and its check-ins are deleted.", await tools.DeleteHabitAsync("read the NEWS"));

        Assert.Equal(["Read 20 pages"], (await service.ListAsync(Owner, true, default)).Select(x => x.Habit.Name));
        Assert.Single(await service.ListAsync(Other, true, default));
        Assert.Equal(["habit.deleted"], audit.Actions);
        Assert.All(audit.Metadata, item => Assert.DoesNotContain("news", item, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_pause_bridges_the_paused_days_in_a_daily_streak_but_not_the_days_missed_before_it()
    {
        // Checked in 5, 4 and 3 days ago, paused from 2 days ago through tomorrow, nothing since.
        var dates = new[] { Today.AddDays(-5), Today.AddDays(-4), Today.AddDays(-3) };
        var paused = Habit(HabitCadences.Daily) with { PausedFrom = Today.AddDays(-2), PausedUntil = Today.AddDays(1) };
        var notPaused = Habit(HabitCadences.Daily);
        var pausedLate = Habit(HabitCadences.Daily) with { PausedFrom = Today, PausedUntil = Today.AddDays(5) };

        Assert.Equal(3, HabitStreaks.Compute(paused, dates, Today).CurrentStreak);
        Assert.Equal(0, HabitStreaks.Compute(notPaused, dates, Today).CurrentStreak);
        // Pausing today does not rescue days that were already missed.
        Assert.Equal(0, HabitStreaks.Compute(pausedLate, dates, Today).CurrentStreak);
        Assert.True(paused.IsPausedOn(Today));
        Assert.False(paused.IsPausedOn(Today.AddDays(2)));
        Assert.False(notPaused.IsPausedOn(Today));
    }

    [Fact]
    public void A_week_paused_end_to_end_neither_counts_nor_breaks_a_weekly_streak()
    {
        // Target 2: weeks of 14 and 21 September met it; the week of 21 September is followed by a fully paused week.
        var dates = new[] { new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 22) };
        var habit = Habit(HabitCadences.Weekly, target: 2) with
        {
            PausedFrom = new DateOnly(2026, 9, 28), PausedUntil = new DateOnly(2026, 10, 4)
        };

        // A week later: the paused week sits between the current week (still open) and the two weeks that met the target.
        var later = new DateOnly(2026, 10, 8);
        Assert.Equal(2, HabitStreaks.Compute(habit, dates, later).CurrentStreak);
        Assert.Equal(0, HabitStreaks.Compute(Habit(HabitCadences.Weekly, target: 2), dates, later).CurrentStreak);
    }

    [Fact]
    public async Task A_paused_habit_is_not_open_today_and_resuming_brings_it_back_with_limits_and_owner_scope()
    {
        var (service, _, _, _, _) = Create();
        var created = await service.CreateAsync(Owner, new HabitDraft("Meditate", null, null, null), null, default);
        var id = created.Value!.Habit.Id;
        Assert.True(created.Value.IsOpenToday);

        var paused = await service.SetPausedAsync(id, Owner, Today.AddDays(7), default);
        var resumed = await service.SetPausedAsync(id, Owner, null, default);

        Assert.False(paused.Value!.IsOpenToday);
        Assert.Equal(Today, paused.Value.Habit.PausedFrom);
        Assert.True(resumed.Value!.IsOpenToday);
        Assert.Null(resumed.Value.Habit.PausedUntil);
        Assert.Equal("until", (await service.SetPausedAsync(id, Owner, Today.AddDays(-1), default)).Field);
        Assert.Equal("until", (await service.SetPausedAsync(id, Owner, Today.AddDays(HabitService.MaxPauseDays + 1), default)).Field);
        Assert.Equal(HabitFailure.NotFound, (await service.SetPausedAsync(id, Other, Today.AddDays(2), default)).Failure);
    }

    [Fact]
    public async Task Chat_pauses_and_resumes_a_habit_by_name_and_reports_bad_input()
    {
        var (service, _, _, _, _) = Create();
        await service.CreateAsync(Owner, new HabitDraft("Meditate", null, null, null), null, default);
        var audit = new RecordingAudit();
        var tools = new HabitAgentTools(service, audit, new FixedUser(), NullLogger.Instance);

        var paused = await tools.PauseHabitAsync("medit", "2026-10-10");
        var open = (await service.ListAsync(Owner, false, default)).Single();
        var resumed = await tools.PauseHabitAsync("Meditate");

        Assert.Contains("Paused \"Meditate\" until 2026-10-10", paused, StringComparison.Ordinal);
        Assert.False(open.IsOpenToday);
        Assert.Contains("active again", resumed, StringComparison.Ordinal);
        Assert.StartsWith("I could not read that date", await tools.PauseHabitAsync("Meditate", "soon"), StringComparison.Ordinal);
        Assert.StartsWith("No active habit matches", await tools.PauseHabitAsync("Piano", "2026-10-10"), StringComparison.Ordinal);
        Assert.StartsWith("I could not change that habit", await tools.PauseHabitAsync("Meditate", "2030-01-01"), StringComparison.Ordinal);
        Assert.Equal(["habit.paused", "habit.resumed"], audit.Actions);
        Assert.Contains("PauseHabit", Jarvis.Agents.ToolGroups.All.Single(g => g.Id == "habits").Tools);
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
