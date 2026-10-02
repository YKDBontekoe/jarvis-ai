using System.Globalization;
using System.Text;
using Jarvis.Domain.Habits;

namespace Jarvis.Application.Habits;

public static class HabitCadences
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? cadence) =>
        cadence is Daily or Weekly;
}

public static class HabitSources
{
    public const string App = "app";
    public const string Chat = "chat";
}

public static class HabitRules
{
    public const int MaxNameLength = 60;
    public const int MaxIconLength = 16;
    public const int MaxHabits = 30;

    /// <summary>How far back a check-in may be added or removed, so "I ran yesterday" still counts.</summary>
    public const int MaxBackfillDays = 7;

    /// <summary>Days of check-in history returned with each habit for the calendar view.</summary>
    public const int HistoryDays = 84;

    public static string? ValidateName(string? name)
    {
        var clean = Clean(name);
        if (clean is null) return "Give the habit a name.";
        if (clean.Length > MaxNameLength) return $"Habit names can contain at most {MaxNameLength} characters.";
        return NameKey(clean).Length == 0 ? "Give the habit a name with letters or digits." : null;
    }

    public static string? ValidateTarget(string cadence, int? target) =>
        cadence == HabitCadences.Weekly && target is { } value && value is < 1 or > 7
            ? "Pick between 1 and 7 times a week."
            : null;

    /// <summary>Daily habits always count one day at a time; weekly habits default to three times a week.</summary>
    public static int NormalizeTarget(string cadence, int? target) =>
        cadence == HabitCadences.Daily ? 1 : Math.Clamp(target ?? 3, 1, 7);

    /// <summary>Trims and collapses runs of whitespace; null when nothing is left.</summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string? CleanIcon(string? icon)
    {
        var clean = Clean(icon);
        return clean is null ? null : clean.Length > MaxIconLength ? clean[..MaxIconLength] : clean;
    }

    /// <summary>Lowercase words without accents or punctuation, so "Sporten!" and "sporten" are the same habit.</summary>
    public static string NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>Streak and progress figures for one habit as of <see cref="Today"/> in the owner's time zone.</summary>
public sealed record HabitStats(
    DateOnly Today,
    int CurrentStreak,
    int BestStreak,
    bool DoneToday,
    int ThisWeekCount,
    int TotalCheckIns,
    IReadOnlyList<DateOnly> RecentDates)
{
    /// <summary>"days" for a daily habit, "weeks" for a weekly one.</summary>
    public string StreakUnit { get; init; } = "days";
}

public sealed record HabitSummary(Habit Habit, HabitStats Stats)
{
    /// <summary>
    /// Still worth doing today: a daily habit not yet checked in, or a weekly habit below its weekly target that was
    /// not already done today.
    /// </summary>
    public bool IsOpenToday => !Habit.IsArchived && !Stats.DoneToday &&
                               (Habit.Cadence == HabitCadences.Daily || Stats.ThisWeekCount < Habit.TargetPerWeek);
}

/// <summary>Pure streak arithmetic. Weeks run Monday to Sunday.</summary>
public static class HabitStreaks
{
    public static HabitStats Compute(Habit habit, IEnumerable<DateOnly> checkInDates, DateOnly today)
    {
        var dates = checkInDates.Where(date => date <= today).ToHashSet();
        var weekStart = WeekStart(today);
        var thisWeek = dates.Count(date => date >= weekStart);
        var recentFrom = today.AddDays(-(HabitRules.HistoryDays - 1));
        var recent = dates.Where(date => date >= recentFrom).Order().ToArray();
        var (current, best, unit) = habit.Cadence == HabitCadences.Weekly
            ? (CurrentWeeks(dates, today, habit.TargetPerWeek), BestWeeks(dates, habit.TargetPerWeek), "weeks")
            : (CurrentDays(dates, today), BestDays(dates), "days");
        return new HabitStats(today, current, Math.Max(best, current), dates.Contains(today), thisWeek, dates.Count,
            recent) { StreakUnit = unit };
    }

    public static DateOnly WeekStart(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Consecutive days up to today; a streak stays alive through today until the day is over.</summary>
    internal static int CurrentDays(IReadOnlySet<DateOnly> dates, DateOnly today)
    {
        var day = dates.Contains(today) ? today : today.AddDays(-1);
        var count = 0;
        while (dates.Contains(day))
        {
            count++;
            day = day.AddDays(-1);
        }
        return count;
    }

    internal static int BestDays(IReadOnlySet<DateOnly> dates)
    {
        var best = 0;
        foreach (var date in dates)
        {
            if (dates.Contains(date.AddDays(-1))) continue;
            var length = 1;
            while (dates.Contains(date.AddDays(length))) length++;
            best = Math.Max(best, length);
        }
        return best;
    }

    /// <summary>Consecutive weeks that met the target; the current week counts once it has met it.</summary>
    internal static int CurrentWeeks(IReadOnlySet<DateOnly> dates, DateOnly today, int target)
    {
        var perWeek = CountPerWeek(dates);
        var week = WeekStart(today);
        if (perWeek.GetValueOrDefault(week) < target) week = week.AddDays(-7);
        var count = 0;
        while (perWeek.GetValueOrDefault(week) >= target)
        {
            count++;
            week = week.AddDays(-7);
        }
        return count;
    }

    internal static int BestWeeks(IReadOnlySet<DateOnly> dates, int target)
    {
        var met = CountPerWeek(dates).Where(pair => pair.Value >= target).Select(pair => pair.Key).ToHashSet();
        var best = 0;
        foreach (var week in met)
        {
            if (met.Contains(week.AddDays(-7))) continue;
            var length = 1;
            while (met.Contains(week.AddDays(7 * length))) length++;
            best = Math.Max(best, length);
        }
        return best;
    }

    private static Dictionary<DateOnly, int> CountPerWeek(IEnumerable<DateOnly> dates) =>
        dates.GroupBy(WeekStart).ToDictionary(group => group.Key, group => group.Count());
}

public enum HabitFailure
{
    None,
    NotFound,
    Invalid,
    Conflict
}

/// <summary>Outcome of a habit change: the value, or why it failed and which field caused it.</summary>
public sealed record HabitOperation<T>(T? Value, HabitFailure Failure = HabitFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == HabitFailure.None;

    public static HabitOperation<T> Ok(T value) => new(value);
    public static HabitOperation<T> NotFound() => new(default, HabitFailure.NotFound);

    public static HabitOperation<T> Invalid(string field, string message) =>
        new(default, HabitFailure.Invalid, field, message);

    public static HabitOperation<T> Conflict(string field, string message) =>
        new(default, HabitFailure.Conflict, field, message);
}

public sealed record HabitDraft(string? Name, string? Icon, string? Cadence, int? TargetPerWeek);

/// <summary>Result of checking in (or undoing) several habits by name, as the chat does.</summary>
public sealed record HabitCheckInResult(
    DateOnly Date,
    IReadOnlyList<HabitSummary> Changed,
    IReadOnlyList<HabitSummary> Unchanged,
    IReadOnlyList<string> NotFound,
    IReadOnlyList<(string Text, IReadOnlyList<string> Candidates)> Ambiguous);

/// <summary>
/// The evening question. <see cref="CheckInTime"/> is local "HH:mm". <see cref="TimeZoneId"/> is the phone's zone,
/// sent by the app; when missing the daily-briefing zone, then UTC, is used.
/// </summary>
public sealed record HabitSettings(bool EveningCheckIn = true, string CheckInTime = "20:30", string? TimeZoneId = null)
{
    public static HabitSettings Default { get; } = new();

    public TimeOnly LocalTime => TimeOnly.TryParseExact(CheckInTime, "HH:mm", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var time) ? time : new TimeOnly(20, 30);

    /// <summary>Throws <see cref="ArgumentException"/> for a time that is not "HH:mm" or an unknown time zone.</summary>
    public HabitSettings Normalize()
    {
        if (!TimeOnly.TryParseExact(CheckInTime?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var time))
            throw new ArgumentException("Use a time like 20:30.", nameof(CheckInTime));
        var zone = string.IsNullOrWhiteSpace(TimeZoneId) ? null : TimeZoneId.Trim();
        if (zone is not null && !Workflows.LocalClock.TryFind(zone, out _))
            throw new ArgumentException("Unknown time zone.", nameof(TimeZoneId));
        return new HabitSettings(EveningCheckIn, time.ToString("HH:mm", CultureInfo.InvariantCulture), zone);
    }
}

/// <summary>Remembers the last evening the question went out, so a restarted workflow never asks twice.</summary>
public sealed record HabitCheckInState(DateOnly? LastAskedDate = null);

public static class HabitSettingsSections
{
    public const string Settings = "habits";
    public const string CheckInState = "habits-checkin-state";
}

public sealed record HabitCheckInWorkflowInput(Guid OwnerId);

/// <summary>When the next evening question is due; <see cref="Active"/> is false when there is nothing to ask about.</summary>
public sealed record HabitCheckInSchedule(bool Active, DateTimeOffset FireAt, DateOnly LocalDate);

public sealed record HabitCheckInDelivery(Guid OwnerId, DateOnly LocalDate);

public static class HabitCheckInWorkflowIds
{
    public static string For(Guid ownerId) => $"habit-checkin-{ownerId:N}";
}

public interface IHabitCheckInScheduler
{
    Task ScheduleHabitCheckInAsync(Guid ownerId, CancellationToken cancellationToken);
    Task CancelHabitCheckInAsync(Guid ownerId, CancellationToken cancellationToken);
}

public interface IHabitRepository
{
    Task<IReadOnlyList<Habit>> ListAsync(Guid ownerId, bool includeArchived, CancellationToken cancellationToken);
    Task<Habit?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddAsync(Habit habit, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(Habit habit, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HabitCheckIn>> ListCheckInsAsync(Guid ownerId, IReadOnlyCollection<Guid> habitIds,
        CancellationToken cancellationToken);

    /// <summary>Adds the check-in; false when that habit already has one on that day.</summary>
    Task<bool> AddCheckInAsync(HabitCheckIn checkIn, CancellationToken cancellationToken);

    Task<bool> DeleteCheckInAsync(Guid ownerId, Guid habitId, DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListOwnersWithActiveHabitsAsync(CancellationToken cancellationToken);
}

public interface IHabitService
{
    Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HabitSummary>> ListAsync(Guid ownerId, bool includeArchived, CancellationToken cancellationToken);
    Task<HabitSummary?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<HabitOperation<HabitSummary>> CreateAsync(Guid ownerId, HabitDraft draft, string? timeZoneId,
        CancellationToken cancellationToken);
    Task<HabitOperation<HabitSummary>> UpdateAsync(Guid id, Guid ownerId, HabitDraft draft,
        CancellationToken cancellationToken);
    Task<HabitOperation<HabitSummary>> SetArchivedAsync(Guid id, Guid ownerId, bool archived,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Marks the habit done on <paramref name="date"/> (today when null); doing it twice is harmless.</summary>
    Task<HabitOperation<HabitSummary>> SetDoneAsync(Guid id, Guid ownerId, DateOnly? date, bool done, string source,
        CancellationToken cancellationToken);

    /// <summary>Checks habits in (or out) by name, as the chat does with "ik heb gesport".</summary>
    Task<HabitOperation<HabitCheckInResult>> SetDoneByNameAsync(Guid ownerId, IEnumerable<string?> names,
        DateOnly? date, bool done, CancellationToken cancellationToken);

    Task<HabitSettings> GetSettingsAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<HabitSettings> SaveSettingsAsync(Guid ownerId, HabitSettings settings, CancellationToken cancellationToken);

    /// <summary>Next evening question for the worker; inactive when it is turned off or there are no habits.</summary>
    Task<HabitCheckInSchedule> ResolveCheckInAsync(Guid ownerId, DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the evening question for <paramref name="localDate"/> when habits are still open. Returns false when the
    /// workflow should stop (turned off, or no active habits left).
    /// </summary>
    Task<bool> DeliverCheckInAsync(Guid ownerId, DateOnly localDate, CancellationToken cancellationToken);
}
