using System.Globalization;
using Jarvis.Application.Automations;
using Jarvis.Application.Timeline;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Automations;

namespace Jarvis.Application.Routines;

/// <summary>One repeated behaviour the miner found and the draft automation that would act on it.</summary>
public sealed record RoutineSuggestion(
    string Fingerprint,
    string Title,
    string Evidence,
    double Confidence,
    AutomationRuleDefinition Definition);

/// <summary>
/// Finds repeated behaviour in the life timeline: things done at about the same time on the same weekdays, and
/// things that reliably follow another event. Pure and deterministic so it is easy to test; the service decides
/// what to store. Every suggestion carries an automation that already passes <see cref="AutomationRuleValidator"/>.
/// </summary>
public static class RoutineMinerEngine
{
    public const int WindowWeeks = 8;
    public const int TimeWindowMinutes = 45;
    public const int MinWeeksPerWeekday = 3;
    public const double MinWeekdayHitRate = 0.6;
    public const int FollowUpHours = 2;
    public const int MinFollowUpOccurrences = 4;
    public const double MinFollowUpRate = 0.7;
    public const int MaxSuggestions = 6;
    public const int FollowUpCooldownMinutes = 240;

    private static readonly HashSet<string> TimeKinds =
        new([TimelineKinds.Journal, TimelineKinds.Expense, TimelineKinds.Habit, TimelineKinds.Task],
            StringComparer.Ordinal);

    // Timeline kinds that also exist as an automation event, so they can start an automation.
    private static readonly Dictionary<string, string> EventKindFor = new(StringComparer.Ordinal)
    {
        [TimelineKinds.Journal] = AutomationEventKinds.JournalSaved,
        [TimelineKinds.Expense] = AutomationEventKinds.ExpenseLogged,
        [TimelineKinds.Task] = AutomationEventKinds.TaskCompleted
    };

    public static IReadOnlyList<RoutineSuggestion> Mine(IReadOnlyList<TimelineEvent> events, TimeZoneInfo zone,
        DateTimeOffset now)
    {
        var since = now.AddDays(-7 * WindowWeeks);
        var recent = events.Where(e => e.At >= since && e.At <= now).OrderBy(e => e.At).ToList();
        var found = new List<RoutineSuggestion>();
        found.AddRange(MineTimeHabits(recent, zone, now));
        found.AddRange(MineFollowUps(recent, now));
        return found.OrderByDescending(s => s.Confidence).ThenBy(s => s.Fingerprint, StringComparer.Ordinal)
            .Take(MaxSuggestions).ToList();
    }

    private static string Normalize(string? text) =>
        string.Join(' ', (text ?? "").ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Journal and expense entries have free-form titles, so only their kind identifies the routine.</summary>
    private static string KeyOf(TimelineEvent e) =>
        e.Kind is TimelineKinds.Journal or TimelineKinds.Expense ? e.Kind : $"{e.Kind}:{Normalize(e.Title)}";

    private static string Label(string kind, string title) => kind switch
    {
        TimelineKinds.Journal => "write in your journal",
        TimelineKinds.Expense => "log an expense",
        TimelineKinds.Habit => $"do \"{title}\"",
        TimelineKinds.Task => $"finish \"{title}\"",
        _ => title
    };

    private static IEnumerable<RoutineSuggestion> MineTimeHabits(List<TimelineEvent> events, TimeZoneInfo zone,
        DateTimeOffset now)
    {
        var today = TimelineRules.LocalDate(now, zone);
        var firstDay = today.AddDays(-7 * WindowWeeks + 1);
        var dayCounts = new int[7];
        for (var day = firstDay; day <= today; day = day.AddDays(1)) dayCounts[WeekdayIndex(day.DayOfWeek)]++;

        foreach (var group in events.Where(e => TimeKinds.Contains(e.Kind)).GroupBy(KeyOf))
        {
            var sample = group.First();
            var points = group.Select(e =>
            {
                var local = TimeZoneInfo.ConvertTime(e.At, zone);
                return (Date: DateOnly.FromDateTime(local.DateTime),
                    Minute: local.Hour * 60 + local.Minute);
            }).OrderBy(p => p.Minute).ToList();
            if (points.Count < MinWeeksPerWeekday) continue;

            // The densest ±45 minute stretch of the day.
            var best = (Start: 0, Count: 0);
            for (var i = 0; i < points.Count; i++)
            {
                var count = points.Count(p => p.Minute >= points[i].Minute &&
                                              p.Minute <= points[i].Minute + 2 * TimeWindowMinutes);
                if (count > best.Count) best = (points[i].Minute, count);
            }

            var inWindow = points.Where(p => p.Minute >= best.Start &&
                                             p.Minute <= best.Start + 2 * TimeWindowMinutes).ToList();
            var hitsByWeekday = new HashSet<DateOnly>[7];
            for (var i = 0; i < 7; i++) hitsByWeekday[i] = [];
            foreach (var p in inWindow) hitsByWeekday[WeekdayIndex(p.Date.DayOfWeek)].Add(p.Date);

            var mask = 0;
            var hits = 0;
            var occurrences = 0;
            for (var i = 0; i < 7; i++)
            {
                var n = hitsByWeekday[i].Count;
                if (n < MinWeeksPerWeekday || dayCounts[i] == 0 || (double)n / dayCounts[i] < MinWeekdayHitRate)
                    continue;
                mask |= 1 << i;
                hits += n;
                occurrences += dayCounts[i];
            }

            if (mask == 0) continue;

            var median = inWindow.Select(p => p.Minute).OrderBy(m => m).ElementAt(inWindow.Count / 2);
            var rounded = (int)Math.Round(median / 5.0) * 5;
            if (rounded >= 24 * 60) rounded = 24 * 60 - 5;
            var time = new TimeOnly(rounded / 60, rounded % 60);
            var title = TimelineRules.Shorten(sample.Title, 60);
            var weekdaysLabel = DescribeWeekdays(mask);
            var action = sample.Kind == TimelineKinds.Task
                ? (AutomationActionDefinition)new TaskActionDefinition(
                    TimelineRules.Shorten($"Do: {title}", 120),
                    $"This is a recurring routine of the user: \"{title}\". Do it or prepare it for them.")
                : new NotificationActionDefinition(
                    sample.Kind == TimelineKinds.Habit ? title : "Time to " + Label(sample.Kind, title),
                    sample.Kind == TimelineKinds.Habit
                        ? "You usually do this around now."
                        : "You usually do this around now. Open Jarvis when you are ready.");
            var definition = new AutomationRuleDefinition(AutomationSchema.CurrentVersion,
                new ScheduleTriggerDefinition(time, zone.Id, mask == 0b1111111 ? null : mask), null, [action], null);
            AutomationRuleValidator.Validate(definition);

            var rate = (double)hits / occurrences;
            yield return new RoutineSuggestion(
                $"time:{group.Key}",
                $"Remind me to {Label(sample.Kind, title)} at {time.ToString("HH:mm", CultureInfo.InvariantCulture)}",
                $"You {Label(sample.Kind, title)} around {time.ToString("HH:mm", CultureInfo.InvariantCulture)} on " +
                $"{hits} of the last {occurrences} {(weekdaysLabel.Length == 0 ? "" : weekdaysLabel + " ")}days.",
                Math.Round(Math.Min(1.0, rate * Math.Min(1.0, hits / 12.0 + 0.5)), 2),
                definition);
        }
    }

    private static IEnumerable<RoutineSuggestion> MineFollowUps(List<TimelineEvent> events, DateTimeOffset now)
    {
        var horizon = now.AddHours(-FollowUpHours);
        foreach (var kind in EventKindFor.Keys)
        {
            var starts = events.Where(e => e.Kind == kind && e.At <= horizon).ToList();
            if (starts.Count < MinFollowUpOccurrences) continue;

            // For each start event, which later moments fell within the follow-up window?
            var followedBy = new Dictionary<string, (string Kind, string Title, int Count)>(StringComparer.Ordinal);
            foreach (var start in starts)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var next in events.Where(e => e.At > start.At && e.At <= start.At.AddHours(FollowUpHours) &&
                                                        e.Kind != kind && TimeKinds.Contains(e.Kind)))
                {
                    var key = KeyOf(next);
                    if (!seen.Add(key)) continue;
                    followedBy[key] = followedBy.TryGetValue(key, out var existing)
                        ? (existing.Kind, existing.Title, existing.Count + 1)
                        : (next.Kind, next.Title, 1);
                }
            }

            foreach (var (key, (nextKind, nextTitle, count)) in followedBy)
            {
                var rate = (double)count / starts.Count;
                if (count < MinFollowUpOccurrences || rate < MinFollowUpRate) continue;
                var title = TimelineRules.Shorten(nextTitle, 60);
                var definition = new AutomationRuleDefinition(AutomationSchema.CurrentVersion,
                    new EventTriggerDefinition(EventKindFor[kind], null, null), null,
                    [new NotificationActionDefinition("Time to " + Label(nextKind, title),
                        "You usually do this soon after this.")],
                    new AutomationLimitsDefinition(null, null, FollowUpCooldownMinutes));
                AutomationRuleValidator.Validate(definition);
                yield return new RoutineSuggestion(
                    $"follow:{kind}>{key}",
                    $"Nudge me to {Label(nextKind, title)} after I {AfterLabel(kind)}",
                    $"After you {AfterLabel(kind)} you {Label(nextKind, title)} within {FollowUpHours} hours: " +
                    $"{count} of {starts.Count} times.",
                    Math.Round(Math.Min(1.0, rate * Math.Min(1.0, count / 10.0 + 0.5)), 2),
                    definition);
            }
        }
    }

    private static string AfterLabel(string kind) => kind switch
    {
        TimelineKinds.Journal => "write in your journal",
        TimelineKinds.Expense => "log an expense",
        TimelineKinds.Task => "finish a task",
        _ => kind
    };

    /// <summary>Monday = 0 … Sunday = 6, matching the bit order of <see cref="ReminderWeekdays"/>.</summary>
    private static int WeekdayIndex(DayOfWeek day) => ((int)day + 6) % 7;

    private static string DescribeWeekdays(int mask)
    {
        if (mask == 0b1111111) return "";
        if (mask == ReminderWeekdays.Weekdays) return "weekday";
        var names = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        return string.Join("/", Enumerable.Range(0, 7).Where(i => (mask & (1 << i)) != 0).Select(i => names[i]));
    }
}
