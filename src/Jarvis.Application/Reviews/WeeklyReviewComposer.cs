using System.Globalization;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Reviews;

/// <summary>Monday-to-Sunday weeks in the owner's time zone. Used from activities and services, not workflows.</summary>
public static class WeeklyReviewClock
{
    /// <summary>How long after a missed Sunday delivery the review is still sent, for example after downtime.</summary>
    public static readonly TimeSpan CatchUpWindow = TimeSpan.FromHours(36);

    public static DateOnly WeekStartOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static DateOnly CurrentWeekStart(DateTimeOffset utcNow, TimeZoneInfo zone) =>
        WeekStartOf(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime));

    public static DateTimeOffset FireAt(DateOnly weekStart, TimeOnly localTime, TimeZoneInfo zone) =>
        LocalClock.Resolve(weekStart.AddDays(6).ToDateTime(localTime), zone);

    public static (DateTimeOffset Start, DateTimeOffset End) Window(DateOnly weekStart, TimeZoneInfo zone) =>
        (LocalClock.Resolve(weekStart.ToDateTime(TimeOnly.MinValue), zone),
            LocalClock.Resolve(weekStart.AddDays(7).ToDateTime(TimeOnly.MinValue), zone));

    /// <summary>
    /// The next week to deliver and when. A Sunday that was missed by less than <see cref="CatchUpWindow"/> is sent
    /// right away; otherwise the review waits for the coming Sunday.
    /// </summary>
    public static (DateTimeOffset FireAt, DateOnly WeekStart) ResolveNext(DateTimeOffset utcNow, TimeOnly localTime,
        TimeZoneInfo zone, DateOnly? lastNotifiedWeek)
    {
        var now = utcNow.ToUniversalTime();
        var current = CurrentWeekStart(now, zone);
        var previous = current.AddDays(-7);
        var previousFire = FireAt(previous, localTime, zone);
        if (!(lastNotifiedWeek >= previous) && now >= previousFire && now - previousFire <= CatchUpWindow)
            return (now, previous);
        if (lastNotifiedWeek >= current)
        {
            var next = current.AddDays(7);
            return (FireAt(next, localTime, zone), next);
        }

        var fire = FireAt(current, localTime, zone);
        return (fire > now ? fire : now, current);
    }
}

/// <summary>One journal entry reduced to what the weekly review counts.</summary>
public sealed record JournalSample(DateOnly Date, int? Mood, int? Energy, int? Stress, int? Rating,
    IReadOnlyList<string> Tags);

public static class WeeklyReviewComposer
{
    public const int MaxNotificationLength = 600;

    public static WeeklyReviewStats BuildStats(IReadOnlyList<JournalSample> entries, double? previousMood,
        int tasksCompleted, int remindersHandled, int remindersUpcoming, int newMemories,
        int decisionsResolved = 0, double? brierScore = null, double? previousBrierScore = null)
    {
        var days = entries
            .GroupBy(entry => entry.Date)
            .OrderBy(group => group.Key)
            .Select(group => new WeeklyReviewDay(group.Key,
                RoundedAverage(group.Select(x => x.Mood)), RoundedAverage(group.Select(x => x.Energy)),
                RoundedAverage(group.Select(x => x.Stress)), RoundedAverage(group.Select(x => x.Rating))))
            .ToArray();
        var best = days.Where(day => day.Rating is not null)
            .OrderByDescending(day => day.Rating).ThenByDescending(day => day.Mood ?? 0).ThenBy(day => day.Date)
            .FirstOrDefault();
        var tags = entries.SelectMany(entry => entry.Tags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .GroupBy(tag => tag.Trim().ToLowerInvariant())
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(5).Select(group => group.Key).ToArray();
        return new WeeklyReviewStats(entries.Count,
            Average(entries.Select(x => x.Mood)), Average(entries.Select(x => x.Energy)),
            Average(entries.Select(x => x.Stress)), Average(entries.Select(x => x.Rating)),
            previousMood, best?.Date, best?.Rating, tasksCompleted, remindersHandled, remindersUpcoming, newMemories,
            tags, days, decisionsResolved, brierScore, previousBrierScore);
    }

    /// <summary>Per-week journal averages, oldest first, with empty weeks kept so the chart keeps its rhythm.</summary>
    public static IReadOnlyList<WeeklyTrendPoint> Trend(IEnumerable<JournalSample> entries, DateOnly lastWeekStart,
        int weeks)
    {
        var byWeek = entries.GroupBy(entry => WeeklyReviewClock.WeekStartOf(entry.Date))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var points = new List<WeeklyTrendPoint>(weeks);
        for (var index = weeks - 1; index >= 0; index--)
        {
            var start = lastWeekStart.AddDays(-7 * index);
            var items = byWeek.GetValueOrDefault(start) ?? [];
            points.Add(new WeeklyTrendPoint(start, items.Length, Average(items.Select(x => x.Mood)),
                Average(items.Select(x => x.Energy)), Average(items.Select(x => x.Stress)),
                Average(items.Select(x => x.Rating))));
        }

        return points;
    }

    /// <summary>The story used when no model is available or it fails: plain facts, no guesses.</summary>
    public static string Compose(WeeklyReviewFacts facts)
    {
        var stats = facts.Stats;
        if (stats.JournalEntries == 0 && stats.TasksCompleted == 0 && stats.RemindersHandled == 0 &&
            stats.NewMemories == 0 && stats.DecisionsResolved == 0)
        {
            var quiet = "A quiet week: no journal entries, finished tasks, or reminders.";
            if (stats.RemindersUpcoming > 0)
                quiet += $" Next week has {Count(stats.RemindersUpcoming, "reminder", "reminders")} lined up.";
            return quiet + " A few lines in your journal next week will make this review richer.";
        }

        var parts = new List<string>();
        if (stats.JournalEntries > 0)
        {
            var days = stats.Days.Count;
            var opening = $"You journaled on {Count(days, "day", "days")} this week";
            var measures = new List<string>();
            if (stats.Mood is { } mood) measures.Add($"mood averaged {Format(mood)}/5{Change(mood, stats.PreviousMood)}");
            if (stats.Energy is { } energy) measures.Add($"energy {Format(energy)}/5");
            if (stats.Stress is { } stress) measures.Add($"stress {Format(stress)}/5");
            parts.Add(measures.Count == 0 ? opening + "." : opening + ": " + string.Join(", ", measures) + ".");
            if (stats.BestDay is { } best && stats.BestDayRating is { } rating)
                parts.Add($"Your best day was {best.DayOfWeek.ToString()} ({rating}/10).");
        }
        else
        {
            parts.Add("You did not write in your journal this week.");
        }

        var done = new List<string>();
        if (stats.TasksCompleted > 0) done.Add(Count(stats.TasksCompleted, "task", "tasks") + " finished");
        if (stats.RemindersHandled > 0) done.Add(Count(stats.RemindersHandled, "reminder", "reminders") + " handled");
        if (stats.NewMemories > 0) done.Add(Count(stats.NewMemories, "new memory", "new memories"));
        if (done.Count > 0) parts.Add(Capitalize(JoinList(done)) + ".");
        if (DescribeDecisions(stats) is { } decisions) parts.Add(decisions);
        if (stats.RemindersUpcoming > 0)
            parts.Add($"Next week has {Count(stats.RemindersUpcoming, "reminder", "reminders")} lined up.");
        return string.Join(' ', parts);
    }

    /// <summary>One sentence on the decisions settled this week and how the predictions scored, or null.</summary>
    public static string? DescribeDecisions(WeeklyReviewStats stats)
    {
        if (stats.DecisionsResolved <= 0) return null;
        var text = $"You settled {Count(stats.DecisionsResolved, "decision", "decisions")}";
        if (stats.BrierScore is not { } score) return text + ".";
        text += $"; your predictions scored {score.ToString("0.00", CultureInfo.InvariantCulture)} " +
                "(0 is perfect, always saying 50% scores 0.25)";
        if (stats.PreviousBrierScore is { } before)
        {
            var delta = Math.Round(before - score, 2);
            text += delta > 0.02 ? ", better than before"
                : delta < -0.02 ? ", worse than before" : ", in line with before";
        }

        return text + ".";
    }

    public static string NotificationBody(string story)
    {
        var text = story.Trim();
        return text.Length > MaxNotificationLength ? text[..(MaxNotificationLength - 1)] + "…" : text;
    }

    public static string CleanNarration(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = value.IndexOf('\n');
            var closing = value.LastIndexOf("```", StringComparison.Ordinal);
            value = firstNewLine > 0 && closing > firstNewLine ? value[(firstNewLine + 1)..closing].Trim() : string.Empty;
        }

        return value.Length > 1_500 ? value[..1_499] + "…" : value;
    }

    private static double? Average(IEnumerable<int?> values)
    {
        var present = values.Where(value => value is not null).Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null : Math.Round(present.Average(), 2);
    }

    private static int? RoundedAverage(IEnumerable<int?> values) =>
        Average(values) is { } average ? (int)Math.Round(average, MidpointRounding.AwayFromZero) : null;

    private static string Change(double mood, double? previous)
    {
        if (previous is not { } before) return string.Empty;
        var delta = Math.Round(mood - before, 1);
        if (Math.Abs(delta) < 0.1) return ", steady on last week";
        return delta > 0 ? $", up {Format(delta)} on last week" : $", down {Format(-delta)} on last week";
    }

    private static string Format(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private static string JoinList(IReadOnlyList<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1]
    };

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
