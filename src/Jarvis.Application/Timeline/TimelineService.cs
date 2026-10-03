using Jarvis.Application.Expenses;
using Jarvis.Application.Habits;
using Jarvis.Application.Journal;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Timeline;

public sealed class TimelineService(
    IEnumerable<ITimelineSource> sources,
    IJournalRepository journal,
    IExpenseRepository expenses,
    IHabitRepository habits,
    IJarvisTaskRepository tasks,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : ITimelineService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zone = await ZoneAsync(ownerId, cancellationToken);
        return TimelineRules.LocalDate(clock.GetUtcNow(), zone);
    }

    public async Task<TimelineResult> QueryAsync(Guid ownerId, TimelineQuery query,
        CancellationToken cancellationToken)
    {
        var (from, to) = Clamp(query.From, query.To);
        var limit = Math.Clamp(query.Limit, 1, TimelineRules.MaxLimit);
        var zone = await ZoneAsync(ownerId, cancellationToken);
        var (events, statuses) = await CollectAsync(new TimelineWindow(ownerId, from, to, zone, TimelineRules.MaxLimit),
            query.Kinds, cancellationToken);

        var text = TimelineRules.Shorten(query.Text, 100);
        var filtered = events
            .Where(x => query.Kinds is not { Count: > 0 } || query.Kinds.Contains(x.Kind))
            .Where(x => text.Length == 0 || Contains(x.Title, text) || (x.Detail is not null && Contains(x.Detail, text)))
            .OrderByDescending(x => x.At).ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        var days = filtered.Take(limit).GroupBy(x => x.Date).OrderByDescending(g => g.Key)
            .Select(g => new TimelineDay(g.Key, g.ToArray())).ToArray();
        return new TimelineResult(from, to, days, filtered.Length, filtered.Length > limit, statuses);
    }

    public async Task<IReadOnlyList<TimelineYear>> OnThisDayAsync(Guid ownerId, DateOnly today, int yearsBack,
        CancellationToken cancellationToken)
    {
        yearsBack = Math.Clamp(yearsBack, 1, 10);
        var zone = await ZoneAsync(ownerId, cancellationToken);
        var earliest = today.AddYears(-yearsBack);
        var (events, _) = await CollectAsync(new TimelineWindow(ownerId, earliest, today.AddDays(-1), zone, 5_000),
            null, cancellationToken);

        var result = new List<TimelineYear>();
        for (var back = 1; back <= yearsBack; back++)
        {
            var date = Anniversary(today, back);
            var matches = events.Where(x => x.Date == date).OrderBy(x => x.At).ToArray();
            if (matches.Length > 0) result.Add(new TimelineYear(date.Year, date, matches));
        }
        return result;
    }

    public async Task<TimelineInsights> InsightsAsync(Guid ownerId, DateOnly today, int days,
        CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 14, TimelineRules.MaxInsightDays);
        var from = today.AddDays(-(days - 1));
        var zone = await ZoneAsync(ownerId, cancellationToken);

        var entries = await journal.ListAsync(ownerId, from, today, 1_000, cancellationToken);
        var spending = await expenses.ListAsync(ownerId, from, today, cancellationToken);
        var allHabits = await habits.ListAsync(ownerId, false, cancellationToken);
        var checkIns = allHabits.Count == 0
            ? []
            : await habits.ListCheckInsAsync(ownerId, allHabits.Select(x => x.Id).ToArray(), cancellationToken);
        var finished = (await tasks.ListAsync(ownerId, cancellationToken))
            .Where(x => x is { Status: "completed", CompletedAt: not null }).ToArray();

        // Money in several currencies cannot be added up, so use the currency used most.
        var currency = spending.GroupBy(x => x.Currency).OrderByDescending(g => g.Count()).Select(g => g.Key)
            .FirstOrDefault();
        var dailyHabits = allHabits.Count(x => x.Cadence == HabitCadences.Daily);

        var metrics = new List<DayMetrics>();
        for (var date = from; date <= today; date = date.AddDays(1))
        {
            var dayEntries = entries.Where(x => x.EntryDate == date).ToArray();
            var done = checkIns.Count(x => x.Date == date);
            metrics.Add(new DayMetrics(
                date,
                Average(dayEntries.Select(x => x.Mood)),
                Average(dayEntries.Select(x => x.Energy)),
                Average(dayEntries.Select(x => x.Stress)),
                Average(dayEntries.Select(x => x.Rating)),
                spending.Where(x => x.SpentOn == date && x.Currency == currency).Sum(x => x.Amount),
                dailyHabits == 0 ? null : Math.Min(1.0, (double)done / dailyHabits),
                finished.Count(x => TimelineRules.LocalDate(x.CompletedAt!.Value, zone) == date)));
        }

        var withData = metrics.Count(x => x.Mood.HasValue || x.Energy.HasValue || x.Stress.HasValue ||
                                          x.Rating.HasValue || x.Spend > 0 || x.HabitRatio is > 0 ||
                                          x.TasksCompleted > 0);
        return new TimelineInsights(from, today, withData, TimelineInsightEngine.Analyze(metrics), metrics);
    }

    private async Task<(IReadOnlyList<TimelineEvent> Events, IReadOnlyList<TimelineSourceStatus> Statuses)>
        CollectAsync(TimelineWindow window, IReadOnlySet<string>? kinds, CancellationToken cancellationToken)
    {
        var events = new List<TimelineEvent>();
        var statuses = new List<TimelineSourceStatus>();
        // The sources share one database context, so they run one after the other.
        foreach (var source in sources)
        {
            if (kinds is { Count: > 0 } && !source.Kinds.Overlaps(kinds)) continue;
            try
            {
                events.AddRange(await source.ListAsync(window, cancellationToken));
                statuses.AddRange(source.Kinds.Select(kind => new TimelineSourceStatus(kind, true)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One broken area must not blank the whole timeline.
                statuses.AddRange(source.Kinds.Select(kind => new TimelineSourceStatus(kind, false)));
            }
        }
        return (events, statuses);
    }

    private async Task<TimeZoneInfo> ZoneAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        return LocalClock.TryFind(zoneId, out var zone) ? zone : TimeZoneInfo.Utc;
    }

    /// <summary>The same calendar day <paramref name="yearsBack"/> years earlier; 29 February becomes the 28th.</summary>
    public static DateOnly Anniversary(DateOnly today, int yearsBack) => today.AddYears(-yearsBack);

    public static (DateOnly From, DateOnly To) Clamp(DateOnly from, DateOnly to)
    {
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber >= TimelineRules.MaxWindowDays)
            from = to.AddDays(-(TimelineRules.MaxWindowDays - 1));
        return (from, to);
    }

    private static double? Average(IEnumerable<int?> values)
    {
        var present = values.Where(x => x.HasValue).Select(x => (double)x!.Value).ToArray();
        return present.Length == 0 ? null : present.Average();
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
