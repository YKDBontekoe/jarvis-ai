using System.Globalization;
using Jarvis.Domain.Game;

namespace Jarvis.Application.Game;

/// <summary>
/// Where Jarvis's bets come from, and how they settle. Jarvis only bets on outcomes the owner would rather avoid, so
/// beating Jarvis is always good for them. Everything is arithmetic on the owner's own history; no model is involved.
/// </summary>
public static class BetEngine
{
    /// <summary>A pattern needs at least this many past chances before Jarvis bets on it.</summary>
    public const int MinSamples = 4;

    /// <summary>How many past weeks are looked at.</summary>
    public const int LookbackWeeks = 8;

    public const double MinMissRate = 0.5;

    public static IReadOnlyList<BetCandidate> Propose(IReadOnlyList<HabitHistory> habits,
        IReadOnlyList<BudgetSnapshot> budgets, DateOnly today)
    {
        var found = new List<BetCandidate>();
        foreach (var habit in habits)
        {
            var candidate = habit.Cadence == "weekly" ? WeeklyHabit(habit, today) : DailyHabit(habit, today);
            if (candidate is not null) found.Add(candidate);
        }

        foreach (var budget in budgets)
            if (Budget(budget, today) is { } candidate) found.Add(candidate);

        return found.OrderByDescending(x => x.Confidence).ThenBy(x => x.EndsOn).ToArray();
    }

    /// <summary>The coming day (tomorrow to six days out) the owner most often skips this habit.</summary>
    public static BetCandidate? DailyHabit(HabitHistory habit, DateOnly today)
    {
        BetCandidate? best = null;
        var bestRate = 0.0;
        for (var ahead = 1; ahead <= 6; ahead++)
        {
            var day = today.AddDays(ahead);
            if (habit.Paused(day)) continue;
            var samples = 0;
            var missed = 0;
            for (var week = 1; week <= LookbackWeeks; week++)
            {
                var past = day.AddDays(-7 * week);
                if (past >= today || past < habit.CreatedOn || habit.Paused(past)) continue;
                samples++;
                if (!habit.Dates.Contains(past)) missed++;
            }

            if (samples < MinSamples) continue;
            var rate = (double)missed / samples;
            if (rate < MinMissRate || rate <= bestRate) continue;
            bestRate = rate;
            var weekday = day.ToString("dddd", CultureInfo.InvariantCulture);
            best = new BetCandidate(BetKinds.HabitDay, SubjectKey(habit.Id), habit.Id,
                $"You’ll skip {habit.Name} on {weekday}",
                $"You skipped it on {missed} of your last {samples} {weekday}s.",
                Confidence(rate), day, day);
        }

        return best;
    }

    /// <summary>A weekly target the owner usually misses, bet on while there is still time to make it.</summary>
    public static BetCandidate? WeeklyHabit(HabitHistory habit, DateOnly today)
    {
        var weekStart = GameRules.WeekStart(today);
        var weekEnd = weekStart.AddDays(6);
        // Leave at least three days to prove Jarvis wrong.
        if (weekEnd.DayNumber - today.DayNumber < 2) return null;
        if (CountIn(habit.Dates, weekStart, today) >= habit.TargetPerWeek) return null;
        if (Enumerable.Range(0, 7).Any(d => habit.Paused(weekStart.AddDays(d)))) return null;

        var samples = 0;
        var missed = 0;
        for (var week = 1; week <= LookbackWeeks; week++)
        {
            var start = weekStart.AddDays(-7 * week);
            if (start < habit.CreatedOn) continue;
            if (Enumerable.Range(0, 7).Any(d => habit.Paused(start.AddDays(d)))) continue;
            samples++;
            if (CountIn(habit.Dates, start, start.AddDays(6)) < habit.TargetPerWeek) missed++;
        }

        if (samples < MinSamples) return null;
        var rate = (double)missed / samples;
        if (rate < MinMissRate) return null;
        var times = habit.TargetPerWeek == 1 ? "once" : $"{habit.TargetPerWeek} times";
        return new BetCandidate(BetKinds.HabitWeek, SubjectKey(habit.Id), habit.Id,
            $"You won’t do {habit.Name} {times} this week",
            $"You missed that in {missed} of your last {samples} weeks.",
            Confidence(rate), today, weekEnd);
    }

    /// <summary>A budget that is still under its limit but heading over it, bet on with enough month left to turn.</summary>
    public static BetCandidate? Budget(BudgetSnapshot budget, DateOnly today)
    {
        if (budget.Limit <= 0 || budget.Spent >= budget.Limit || budget.Projected is not { } projected ||
            projected <= budget.Limit)
            return null;
        var monthEnd = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        if (monthEnd.DayNumber - today.DayNumber < 5) return null;

        var over = (double)(projected / budget.Limit) - 1;
        var category = budget.Category.Trim();
        return new BetCandidate(BetKinds.Budget, BudgetKey(category, today), null,
            $"You’ll go over your {category} budget this month",
            $"{Money(budget.Spent, budget.Currency)} of {Money(budget.Limit, budget.Currency)} spent, " +
            $"heading for {Money(projected, budget.Currency)}.",
            Math.Clamp(0.5 + over, 0.55, 0.9), today, monthEnd);
    }

    /// <summary>
    /// Settles a habit bet. Returns the new status, or null while it is still open. A day that turned into a pause, or
    /// a habit that is gone, voids the bet.
    /// </summary>
    public static string? SettleHabit(GameBet bet, HabitHistory? habit, int target, DateOnly today)
    {
        if (habit is null) return BetStatuses.Void;
        if (bet.Kind == BetKinds.HabitDay)
        {
            if (habit.Dates.Contains(bet.EndsOn)) return BetStatuses.Won;
            if (habit.Paused(bet.EndsOn)) return BetStatuses.Void;
            return today > bet.EndsOn ? BetStatuses.Lost : null;
        }

        var weekStart = GameRules.WeekStart(bet.EndsOn);
        if (CountIn(habit.Dates, weekStart, bet.EndsOn) >= target) return BetStatuses.Won;
        return today > bet.EndsOn ? BetStatuses.Lost : null;
    }

    /// <summary>Settles a budget bet: going over loses at once, reaching the month's end under the limit wins.</summary>
    public static string? SettleBudget(GameBet bet, BudgetSnapshot? budget, DateOnly today)
    {
        if (budget is null) return BetStatuses.Void;
        if (budget.Spent > budget.Limit) return BetStatuses.Lost;
        return today > bet.EndsOn ? BetStatuses.Won : null;
    }

    public static string SubjectKey(Guid habitId) => "habit:" + habitId.ToString("N");

    public static string BudgetKey(string category, DateOnly month) =>
        $"budget:{category.Trim().ToLowerInvariant()}:{month:yyyy-MM}";

    /// <summary>The budget category a budget bet is about, from its subject key.</summary>
    public static string? BudgetCategory(string subjectKey)
    {
        var parts = subjectKey.Split(':');
        return parts.Length == 3 && parts[0] == "budget" ? parts[1] : null;
    }

    private static double Confidence(double missRate) => Math.Round(Math.Clamp(missRate, 0.5, 0.95), 2);

    private static int CountIn(IReadOnlySet<DateOnly> dates, DateOnly from, DateOnly to) =>
        dates.Count(d => d >= from && d <= to);

    public static string Money(decimal amount, string currency) =>
        (currency.ToUpperInvariant() switch
        {
            "EUR" => "€",
            "USD" => "$",
            "GBP" => "£",
            _ => currency.ToUpperInvariant() + " "
        }) + amount.ToString("0", CultureInfo.InvariantCulture);
}
