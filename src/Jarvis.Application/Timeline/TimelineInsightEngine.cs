using System.Globalization;

namespace Jarvis.Application.Timeline;

/// <summary>
/// Finds patterns in a few weeks of daily numbers. It is plain arithmetic, so the same days always give the same
/// findings; a model only phrases them later, if at all. A pattern needs enough days and a clear strength.
/// </summary>
public static class TimelineInsightEngine
{
    public const int MinPairedDays = 7;
    public const double MinCorrelation = 0.4;
    public const int TrendWindowDays = 14;
    public const double TrendDelta = 0.5;

    private sealed record Metric(string Id, string Label, Func<DayMetrics, double?> Value, bool HigherIsBetter);

    private static readonly Metric Mood = new("mood", "mood", d => d.Mood, true);
    private static readonly Metric Energy = new("energy", "energy", d => d.Energy, true);
    private static readonly Metric Stress = new("stress", "stress", d => d.Stress, false);
    private static readonly Metric Habits = new("habits", "habit follow-through", d => d.HabitRatio, true);
    private static readonly Metric Spend = new("spend", "spending", d => d.Spend > 0 ? (double)d.Spend : 0, false);
    private static readonly Metric Tasks = new("tasks", "finished tasks", d => d.TasksCompleted, true);

    private static readonly (Metric A, Metric B)[] Pairs =
    [
        (Habits, Mood), (Habits, Energy), (Habits, Stress), (Spend, Stress), (Spend, Mood), (Tasks, Stress),
        (Tasks, Mood), (Energy, Mood), (Stress, Mood)
    ];

    public static IReadOnlyList<TimelineInsight> Analyze(IReadOnlyList<DayMetrics> days)
    {
        var insights = new List<TimelineInsight>();
        foreach (var (a, b) in Pairs)
        {
            if (Correlate(days, a, b) is { } insight) insights.Add(insight);
        }
        insights.AddRange(Trends(days));
        if (WeekdayPattern(days, Spend, "spending", true) is { } spendDay) insights.Add(spendDay);
        if (WeekdayPattern(days, Mood, "mood", false) is { } moodDay) insights.Add(moodDay);
        if (BestDay(days) is { } best) insights.Add(best);
        return insights.OrderByDescending(x => x.Strength).Take(8).ToArray();
    }

    /// <summary>Pearson correlation of the days where both values exist; null when there are too few of them.</summary>
    public static double? Pearson(IReadOnlyList<(double X, double Y)> pairs)
    {
        if (pairs.Count < 2) return null;
        var meanX = pairs.Average(p => p.X);
        var meanY = pairs.Average(p => p.Y);
        var covariance = pairs.Sum(p => (p.X - meanX) * (p.Y - meanY));
        var varianceX = pairs.Sum(p => (p.X - meanX) * (p.X - meanX));
        var varianceY = pairs.Sum(p => (p.Y - meanY) * (p.Y - meanY));
        if (varianceX <= 1e-9 || varianceY <= 1e-9) return null;
        return covariance / Math.Sqrt(varianceX * varianceY);
    }

    private static TimelineInsight? Correlate(IReadOnlyList<DayMetrics> days, Metric a, Metric b)
    {
        var pairs = days.Select(d => (X: a.Value(d), Y: b.Value(d)))
            .Where(p => p.X.HasValue && p.Y.HasValue)
            .Select(p => (X: p.X!.Value, Y: p.Y!.Value))
            .ToArray();
        if (pairs.Length < MinPairedDays) return null;
        if (Pearson(pairs) is not { } r || Math.Abs(r) < MinCorrelation) return null;

        // Compare days above and below the usual level of the first metric: easier to read than "r = 0.52".
        var usual = pairs.Average(p => p.X);
        var high = pairs.Where(p => p.X > usual).Select(p => p.Y).ToArray();
        var low = pairs.Where(p => p.X <= usual).Select(p => p.Y).ToArray();
        if (high.Length == 0 || low.Length == 0) return null;

        var direction = r > 0 ? "higher" : "lower";
        var headline = $"{Capitalize(b.Label)} is {direction} on days with more {a.Label}";
        var detail = string.Format(CultureInfo.InvariantCulture,
            "Across {0} days, {1} averaged {2:0.0} when {3} was above its usual level and {4:0.0} when it was not.",
            pairs.Length, b.Label, high.Average(), a.Label, low.Average());
        return new TimelineInsight($"corr:{a.Id}:{b.Id}", headline, detail, Math.Abs(r), [a.Id, b.Id]);
    }

    private static IEnumerable<TimelineInsight> Trends(IReadOnlyList<DayMetrics> days)
    {
        if (days.Count == 0) yield break;
        var last = days.Max(d => d.Date);
        var recentStart = last.AddDays(-(TrendWindowDays - 1));
        var priorStart = recentStart.AddDays(-TrendWindowDays);
        foreach (var metric in new[] { Mood, Energy, Stress })
        {
            var recent = days.Where(d => d.Date >= recentStart).Select(metric.Value).Where(v => v.HasValue)
                .Select(v => v!.Value).ToArray();
            var prior = days.Where(d => d.Date >= priorStart && d.Date < recentStart).Select(metric.Value)
                .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            if (recent.Length < 4 || prior.Length < 4) continue;
            var delta = recent.Average() - prior.Average();
            if (Math.Abs(delta) < TrendDelta) continue;
            var better = (delta > 0) == metric.HigherIsBetter;
            var verb = delta > 0 ? "up" : "down";
            yield return new TimelineInsight($"trend:{metric.Id}",
                $"{Capitalize(metric.Label)} is trending {verb}",
                string.Format(CultureInfo.InvariantCulture,
                    "The last {0} days averaged {1:0.0} against {2:0.0} in the {0} days before.{3}",
                    TrendWindowDays, recent.Average(), prior.Average(),
                    better ? " That is a good direction." : " That may be worth a look."),
                Math.Min(1, Math.Abs(delta) / 2), [metric.Id]);
        }
    }

    private static TimelineInsight? WeekdayPattern(IReadOnlyList<DayMetrics> days, Metric metric, string label,
        bool highest)
    {
        var values = days.Select(d => (d.Date.DayOfWeek, Value: metric.Value(d)))
            .Where(x => x.Value.HasValue).Select(x => (x.DayOfWeek, Value: x.Value!.Value)).ToArray();
        if (values.Length < 14) return null;
        var overall = values.Average(x => x.Value);
        if (overall <= 0) return null;
        var perDay = values.GroupBy(x => x.DayOfWeek).Where(g => g.Count() >= 2)
            .Select(g => (Day: g.Key, Average: g.Average(x => x.Value))).ToArray();
        if (perDay.Length < 4) return null;
        var pick = highest ? perDay.MaxBy(x => x.Average) : perDay.MinBy(x => x.Average);
        var ratio = pick.Average / overall;
        if (highest ? ratio < 1.5 : ratio > 0.85) return null;
        return new TimelineInsight($"weekday:{metric.Id}",
            highest ? $"Most {label} happens on {pick.Day}s" : $"{Capitalize(label)} dips on {pick.Day}s",
            string.Format(CultureInfo.InvariantCulture,
                "{0}s average {1:0.0} against {2:0.0} across all days.", pick.Day, pick.Average, overall),
            Math.Min(1, Math.Abs(ratio - 1)), [metric.Id]);
    }

    private static TimelineInsight? BestDay(IReadOnlyList<DayMetrics> days)
    {
        var rated = days.Where(d => d.Rating.HasValue).ToArray();
        if (rated.Length < 5) return null;
        var best = rated.MaxBy(d => d.Rating)!;
        return new TimelineInsight("best-day", $"Your best day was {best.Date:dddd d MMMM}",
            string.Format(CultureInfo.InvariantCulture, "You rated it {0:0.#}/10.", best.Rating), 0.2, ["rating"]);
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
