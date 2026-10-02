using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Habits;

namespace Jarvis.Application.Habits;

public sealed class HabitService(
    IHabitRepository habits,
    IOwnerSettingsStore settingsStore,
    IDailyBriefingRepository briefings,
    INotificationRepository notifications,
    IHabitCheckInScheduler? scheduler = null,
    TimeProvider? timeProvider = null) : IHabitService
{
    /// <summary>After a restart the evening question may still go out this long after its time.</summary>
    private static readonly TimeSpan CatchUpWindow = TimeSpan.FromHours(2);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zone = await ResolveZoneAsync(ownerId, await GetSettingsAsync(ownerId, cancellationToken),
            cancellationToken);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }

    public async Task<IReadOnlyList<HabitSummary>> ListAsync(Guid ownerId, bool includeArchived,
        CancellationToken cancellationToken)
    {
        var all = await habits.ListAsync(ownerId, includeArchived, cancellationToken);
        return await SummarizeAsync(ownerId, all, await TodayAsync(ownerId, cancellationToken), cancellationToken);
    }

    public async Task<HabitSummary?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var habit = await habits.GetAsync(id, ownerId, cancellationToken);
        return habit is null ? null : await SummarizeAsync(habit, cancellationToken);
    }

    public async Task<HabitOperation<HabitSummary>> CreateAsync(Guid ownerId, HabitDraft draft, string? timeZoneId,
        CancellationToken cancellationToken)
    {
        if (HabitRules.ValidateName(draft.Name) is { } nameError)
            return HabitOperation<HabitSummary>.Invalid("name", nameError);
        var cadence = draft.Cadence?.Trim().ToLowerInvariant() ?? HabitCadences.Daily;
        if (!HabitCadences.IsValid(cadence))
            return HabitOperation<HabitSummary>.Invalid("cadence", "Choose daily or weekly.");
        if (HabitRules.ValidateTarget(cadence, draft.TargetPerWeek) is { } targetError)
            return HabitOperation<HabitSummary>.Invalid("targetPerWeek", targetError);

        var name = HabitRules.Clean(draft.Name)!;
        var active = await habits.ListAsync(ownerId, false, cancellationToken);
        if (active.Count >= HabitRules.MaxHabits)
            return HabitOperation<HabitSummary>.Invalid("name",
                $"You can track at most {HabitRules.MaxHabits} habits. Archive one first.");
        if (SameName(active, name, null) is not null)
            return HabitOperation<HabitSummary>.Conflict("name", "You already have a habit with that name.");

        var now = clock.GetUtcNow();
        var habit = new Habit(Guid.CreateVersion7(), ownerId, name, HabitRules.CleanIcon(draft.Icon), cadence,
            HabitRules.NormalizeTarget(cadence, draft.TargetPerWeek), null, now, now);
        await habits.AddAsync(habit, cancellationToken);

        var settings = await GetSettingsAsync(ownerId, cancellationToken);
        var zone = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
        if (zone is not null && zone != settings.TimeZoneId && LocalClock.TryFind(zone, out _))
            settings = await SaveSettingsAsync(ownerId, settings with { TimeZoneId = zone }, cancellationToken);
        else if (settings.EveningCheckIn)
            await TryScheduleAsync(ownerId, true, cancellationToken);
        return HabitOperation<HabitSummary>.Ok(await SummarizeAsync(habit, cancellationToken));
    }

    public async Task<HabitOperation<HabitSummary>> UpdateAsync(Guid id, Guid ownerId, HabitDraft draft,
        CancellationToken cancellationToken)
    {
        var habit = await habits.GetAsync(id, ownerId, cancellationToken);
        if (habit is null) return HabitOperation<HabitSummary>.NotFound();
        var name = draft.Name is null ? habit.Name : HabitRules.Clean(draft.Name);
        if (HabitRules.ValidateName(name) is { } nameError)
            return HabitOperation<HabitSummary>.Invalid("name", nameError);
        var cadence = draft.Cadence?.Trim().ToLowerInvariant() ?? habit.Cadence;
        if (!HabitCadences.IsValid(cadence))
            return HabitOperation<HabitSummary>.Invalid("cadence", "Choose daily or weekly.");
        if (HabitRules.ValidateTarget(cadence, draft.TargetPerWeek) is { } targetError)
            return HabitOperation<HabitSummary>.Invalid("targetPerWeek", targetError);
        var others = await habits.ListAsync(ownerId, false, cancellationToken);
        if (!habit.IsArchived && SameName(others, name!, id) is not null)
            return HabitOperation<HabitSummary>.Conflict("name", "You already have a habit with that name.");

        var target = draft.TargetPerWeek ?? (cadence == habit.Cadence ? habit.TargetPerWeek : null);
        var updated = habit with
        {
            Name = name!,
            Icon = draft.Icon is null ? habit.Icon : HabitRules.CleanIcon(draft.Icon),
            Cadence = cadence,
            TargetPerWeek = HabitRules.NormalizeTarget(cadence, target),
            UpdatedAt = clock.GetUtcNow()
        };
        return await habits.UpdateAsync(updated, cancellationToken)
            ? HabitOperation<HabitSummary>.Ok(await SummarizeAsync(updated, cancellationToken))
            : HabitOperation<HabitSummary>.NotFound();
    }

    public async Task<HabitOperation<HabitSummary>> SetArchivedAsync(Guid id, Guid ownerId, bool archived,
        CancellationToken cancellationToken)
    {
        var habit = await habits.GetAsync(id, ownerId, cancellationToken);
        if (habit is null) return HabitOperation<HabitSummary>.NotFound();
        if (habit.IsArchived == archived) return HabitOperation<HabitSummary>.Ok(await SummarizeAsync(habit, cancellationToken));
        if (!archived)
        {
            var active = await habits.ListAsync(ownerId, false, cancellationToken);
            if (active.Count >= HabitRules.MaxHabits)
                return HabitOperation<HabitSummary>.Invalid("archived",
                    $"You can track at most {HabitRules.MaxHabits} habits. Archive another one first.");
            if (SameName(active, habit.Name, id) is not null)
                return HabitOperation<HabitSummary>.Conflict("name",
                    "You already have an active habit with that name.");
        }

        var now = clock.GetUtcNow();
        var updated = habit with { ArchivedAt = archived ? now : null, UpdatedAt = now };
        if (!await habits.UpdateAsync(updated, cancellationToken)) return HabitOperation<HabitSummary>.NotFound();
        if (!archived && (await GetSettingsAsync(ownerId, cancellationToken)).EveningCheckIn)
            await TryScheduleAsync(ownerId, true, cancellationToken);
        return HabitOperation<HabitSummary>.Ok(await SummarizeAsync(updated, cancellationToken));
    }

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        habits.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<HabitOperation<HabitSummary>> SetDoneAsync(Guid id, Guid ownerId, DateOnly? date, bool done,
        string source, CancellationToken cancellationToken)
    {
        var habit = await habits.GetAsync(id, ownerId, cancellationToken);
        if (habit is null) return HabitOperation<HabitSummary>.NotFound();
        if (habit.IsArchived)
            return HabitOperation<HabitSummary>.Invalid("habit", "Restore the habit before checking it in.");
        var today = await TodayAsync(ownerId, cancellationToken);
        var day = date ?? today;
        if (ValidateDate(day, today) is { } dateError) return HabitOperation<HabitSummary>.Invalid("date", dateError);
        await ApplyAsync(habit, day, done, source, cancellationToken);
        return HabitOperation<HabitSummary>.Ok(await SummarizeAsync(habit, today, cancellationToken));
    }

    public async Task<HabitOperation<HabitCheckInResult>> SetDoneByNameAsync(Guid ownerId,
        IEnumerable<string?> names, DateOnly? date, bool done, CancellationToken cancellationToken)
    {
        var today = await TodayAsync(ownerId, cancellationToken);
        var day = date ?? today;
        if (ValidateDate(day, today) is { } dateError)
            return HabitOperation<HabitCheckInResult>.Invalid("date", dateError);

        var active = await habits.ListAsync(ownerId, false, cancellationToken);
        var before = (await SummarizeAsync(ownerId, active, today, cancellationToken)).ToDictionary(x => x.Habit.Id);
        var doneOnDay = await DoneOnAsync(ownerId, active, day, cancellationToken);
        var targets = new Dictionary<Guid, Habit>();
        var unchanged = new Dictionary<Guid, Habit>();
        var notFound = new List<string>();
        var ambiguous = new List<(string, IReadOnlyList<string>)>();
        foreach (var name in names.Select(HabitRules.Clean).OfType<string>().Distinct())
        {
            var match = Match(active, name);
            if (match.Count == 0) notFound.Add(name);
            else if (match.Count > 1) ambiguous.Add((name, match.Select(x => x.Name).ToArray()));
            else if (doneOnDay.Contains(match[0].Id) == done) unchanged[match[0].Id] = match[0];
            else targets[match[0].Id] = match[0];
        }

        foreach (var habit in targets.Values)
            await ApplyAsync(habit, day, done, HabitSources.Chat, cancellationToken);
        var changed = targets.Count == 0
            ? []
            : await SummarizeAsync(ownerId, targets.Values.ToArray(), today, cancellationToken);
        return HabitOperation<HabitCheckInResult>.Ok(new HabitCheckInResult(day, changed,
            unchanged.Keys.Select(id => before[id]).ToArray(), notFound, ambiguous));
    }

    public async Task<HabitSettings> GetSettingsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settingsStore.GetAsync<HabitSettings>(ownerId, HabitSettingsSections.Settings, cancellationToken)
        ?? HabitSettings.Default;

    public async Task<HabitSettings> SaveSettingsAsync(Guid ownerId, HabitSettings settings,
        CancellationToken cancellationToken)
    {
        var normalized = settings.Normalize();
        await settingsStore.SaveAsync(ownerId, HabitSettingsSections.Settings, normalized, cancellationToken);
        // The workflow reads the time and zone on every run, so a restart is only needed to switch it on or off.
        await TryScheduleAsync(ownerId, normalized.EveningCheckIn, cancellationToken);
        return normalized;
    }

    public async Task<HabitCheckInSchedule> ResolveCheckInAsync(Guid ownerId, DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(ownerId, cancellationToken);
        if (!settings.EveningCheckIn || (await habits.ListAsync(ownerId, false, cancellationToken)).Count == 0)
            return new HabitCheckInSchedule(false, utcNow, DateOnly.FromDateTime(utcNow.UtcDateTime));
        var zone = await ResolveZoneAsync(ownerId, settings, cancellationToken);
        var state = await settingsStore.GetAsync<HabitCheckInState>(ownerId, HabitSettingsSections.CheckInState,
            cancellationToken) ?? new HabitCheckInState();
        return NextCheckIn(utcNow, settings.LocalTime, zone, state.LastAskedDate);
    }

    public async Task<bool> DeliverCheckInAsync(Guid ownerId, DateOnly localDate, CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(ownerId, cancellationToken);
        if (!settings.EveningCheckIn) return false;
        var active = await habits.ListAsync(ownerId, false, cancellationToken);
        if (active.Count == 0) return false;
        var state = await settingsStore.GetAsync<HabitCheckInState>(ownerId, HabitSettingsSections.CheckInState,
            cancellationToken) ?? new HabitCheckInState();
        if (state.LastAskedDate >= localDate) return true;
        var today = await TodayAsync(ownerId, cancellationToken);
        if (today != localDate) return true;

        var open = (await SummarizeAsync(ownerId, active, today, cancellationToken))
            .Where(x => x.IsOpenToday).ToArray();
        if (open.Length > 0)
            await notifications.CreateAsync(ownerId, HabitNotifications.CheckIn, "How did today go?",
                HabitNotifications.Body(open), null, cancellationToken);
        await settingsStore.SaveAsync(ownerId, HabitSettingsSections.CheckInState,
            new HabitCheckInState(localDate), cancellationToken);
        return true;
    }

    /// <summary>
    /// Today's question time if it is still ahead (or just passed, after a restart) and not asked yet; otherwise
    /// tomorrow's.
    /// </summary>
    public static HabitCheckInSchedule NextCheckIn(DateTimeOffset utcNow, TimeOnly localTime, TimeZoneInfo zone,
        DateOnly? lastAsked)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
        if (lastAsked is null || lastAsked < localDate)
        {
            var today = LocalClock.Resolve(localDate.ToDateTime(localTime), zone);
            if (today > utcNow) return new HabitCheckInSchedule(true, today, localDate);
            if (utcNow - today <= CatchUpWindow) return new HabitCheckInSchedule(true, utcNow, localDate);
        }
        var tomorrow = localDate.AddDays(1);
        return new HabitCheckInSchedule(true, LocalClock.Resolve(tomorrow.ToDateTime(localTime), zone), tomorrow);
    }

    /// <summary>
    /// Habits whose name equals <paramref name="text"/> (ignoring case and punctuation); otherwise habits where one
    /// contains the other as whole words, so "read" finds "Read 20 pages".
    /// </summary>
    public static IReadOnlyList<Habit> Match(IReadOnlyList<Habit> all, string text)
    {
        var key = HabitRules.NameKey(text);
        if (key.Length == 0) return [];
        var exact = all.Where(x => HabitRules.NameKey(x.Name) == key).ToArray();
        if (exact.Length > 0) return [exact[0]];
        var padded = $" {key} ";
        return all.Where(x =>
        {
            var habitKey = HabitRules.NameKey(x.Name);
            return habitKey.Length > 0 && ($" {habitKey} ".Contains(padded, StringComparison.Ordinal) ||
                                           padded.Contains($" {habitKey} ", StringComparison.Ordinal));
        }).ToArray();
    }

    private static string? ValidateDate(DateOnly day, DateOnly today)
    {
        if (day > today) return "You can't check in a day that hasn't happened yet.";
        return day < today.AddDays(-HabitRules.MaxBackfillDays)
            ? $"You can only change the last {HabitRules.MaxBackfillDays} days."
            : null;
    }

    private async Task ApplyAsync(Habit habit, DateOnly day, bool done, string source,
        CancellationToken cancellationToken)
    {
        if (done)
            await habits.AddCheckInAsync(new HabitCheckIn(Guid.CreateVersion7(), habit.Id, habit.OwnerId, day, source,
                clock.GetUtcNow()), cancellationToken);
        else
            await habits.DeleteCheckInAsync(habit.OwnerId, habit.Id, day, cancellationToken);
    }

    private async Task<HashSet<Guid>> DoneOnAsync(Guid ownerId, IReadOnlyList<Habit> all, DateOnly day,
        CancellationToken cancellationToken) =>
        (await habits.ListCheckInsAsync(ownerId, all.Select(x => x.Id).ToArray(), cancellationToken))
        .Where(x => x.Date == day).Select(x => x.HabitId).ToHashSet();

    private async Task<HabitSummary> SummarizeAsync(Habit habit, CancellationToken cancellationToken) =>
        await SummarizeAsync(habit, await TodayAsync(habit.OwnerId, cancellationToken), cancellationToken);

    private async Task<HabitSummary> SummarizeAsync(Habit habit, DateOnly today, CancellationToken cancellationToken) =>
        (await SummarizeAsync(habit.OwnerId, [habit], today, cancellationToken))[0];

    private async Task<IReadOnlyList<HabitSummary>> SummarizeAsync(Guid ownerId, IReadOnlyList<Habit> all,
        DateOnly today, CancellationToken cancellationToken)
    {
        if (all.Count == 0) return [];
        var checkIns = (await habits.ListCheckInsAsync(ownerId, all.Select(x => x.Id).ToArray(), cancellationToken))
            .ToLookup(x => x.HabitId, x => x.Date);
        return all.Select(habit => new HabitSummary(habit, HabitStreaks.Compute(habit, checkIns[habit.Id], today)))
            .ToArray();
    }

    private async Task<TimeZoneInfo> ResolveZoneAsync(Guid ownerId, HabitSettings settings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.TimeZoneId) && LocalClock.TryFind(settings.TimeZoneId, out var own))
            return own;
        var briefingZone = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        return LocalClock.TryFind(briefingZone, out var zone) ? zone : TimeZoneInfo.Utc;
    }

    // Scheduling is best effort: the worker's reconciler starts or repairs the workflow within minutes.
    private async Task TryScheduleAsync(Guid ownerId, bool enabled, CancellationToken cancellationToken)
    {
        if (scheduler is null) return;
        try
        {
            if (enabled) await scheduler.ScheduleHabitCheckInAsync(ownerId, cancellationToken);
            else await scheduler.CancelHabitCheckInAsync(ownerId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private static Habit? SameName(IEnumerable<Habit> all, string name, Guid? except)
    {
        var key = HabitRules.NameKey(name);
        return all.FirstOrDefault(x => x.Id != except && HabitRules.NameKey(x.Name) == key);
    }
}

public static class HabitNotifications
{
    public const string CheckIn = "habit.checkin";

    /// <summary>Names the open habits; the notification body never leaves the owner's own devices and channels.</summary>
    public static string Body(IReadOnlyList<HabitSummary> open)
    {
        var names = open.Take(5).Select(x => string.IsNullOrWhiteSpace(x.Habit.Icon)
            ? x.Habit.Name
            : $"{x.Habit.Icon} {x.Habit.Name}").ToArray();
        var list = names.Length == 1 ? names[0] : string.Join(", ", names[..^1]) + " and " + names[^1];
        if (open.Count > names.Length) list += $" and {open.Count - names.Length} more";
        var streaks = open.Where(x => x.Stats.CurrentStreak >= 3 && x.Habit.Cadence == HabitCadences.Daily)
            .OrderByDescending(x => x.Stats.CurrentStreak).FirstOrDefault();
        var nudge = streaks is null
            ? ""
            : $" Your {streaks.Stats.CurrentStreak}-day streak on {streaks.Habit.Name} is still alive.";
        return $"Still open today: {list}.{nudge} Tap to check them off, or just tell me what you did.";
    }
}
