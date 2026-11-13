namespace Jarvis.Application.Game;

/// <summary>
/// The race against your ghost: this week so far against the same weekdays four weeks earlier. Only the owner's own
/// numbers are compared; a measurement shows when at least one side has something to count.
/// </summary>
public static class GhostEngine
{
    public sealed record Spend(DateOnly On, decimal Amount, string Currency);

    public sealed record Mood(DateOnly On, int Value);

    public static GhostReport Race(DateOnly today, IReadOnlyCollection<DateOnly> habitCheckIns,
        IReadOnlyCollection<Spend> spending, IReadOnlyCollection<Mood> moods,
        IReadOnlyCollection<DateOnly> journalDays, IReadOnlyCollection<DateOnly> promisesKept)
    {
        var from = GameRules.WeekStart(today);
        var ghostFrom = from.AddDays(-GameRules.GhostLagDays);
        var ghostTo = today.AddDays(-GameRules.GhostLagDays);
        bool Now(DateOnly d) => d >= from && d <= today;
        bool Then(DateOnly d) => d >= ghostFrom && d <= ghostTo;

        var metrics = new List<GhostMetric>();
        void Count(string key, string label, string unit, IReadOnlyCollection<DateOnly> days, bool distinct)
        {
            var source = distinct ? days.Distinct().ToArray() : days.ToArray();
            var now = source.Count(Now);
            var ghost = source.Count(Then);
            if (now > 0 || ghost > 0) metrics.Add(Metric(key, label, now, ghost, unit, false));
        }

        Count("habits", "Habit check-ins", "check-ins", habitCheckIns, false);
        Count("journal", "Days journalled", "days", journalDays, true);
        Count("promises", "Promises kept", "kept", promisesKept, false);

        // Only one currency is compared, the one used most.
        var currency = spending.Where(s => Now(s.On) || Then(s.On)).GroupBy(s => s.Currency.ToUpperInvariant())
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).FirstOrDefault()?.Key;
        if (currency is not null)
        {
            var mine = spending.Where(s => s.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase)).ToArray();
            metrics.Add(Metric("spending", "Spent", (double)mine.Where(s => Now(s.On)).Sum(s => s.Amount),
                (double)mine.Where(s => Then(s.On)).Sum(s => s.Amount), currency, true));
        }

        var moodNow = moods.Where(m => Now(m.On)).Select(m => (double)m.Value).ToArray();
        var moodThen = moods.Where(m => Then(m.On)).Select(m => (double)m.Value).ToArray();
        if (moodNow.Length > 0 && moodThen.Length > 0)
            metrics.Add(Metric("mood", "Average mood", Math.Round(moodNow.Average(), 1),
                Math.Round(moodThen.Average(), 1), "of 5", false));

        return new GhostReport(from, today, ghostFrom, ghostTo, metrics,
            metrics.Count(m => m.Verdict == GhostVerdicts.Ahead), metrics.Count(m => m.Verdict == GhostVerdicts.Behind));
    }

    private static GhostMetric Metric(string key, string label, double now, double ghost, string unit,
        bool lowerIsBetter)
    {
        var verdict = Math.Abs(now - ghost) < 0.05 ? GhostVerdicts.Even
            : (now > ghost) != lowerIsBetter ? GhostVerdicts.Ahead : GhostVerdicts.Behind;
        return new GhostMetric(key, label, Math.Round(now, 2), Math.Round(ghost, 2), unit, lowerIsBetter, verdict);
    }
}
