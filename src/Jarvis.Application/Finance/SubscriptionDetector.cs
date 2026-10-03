using Jarvis.Application.Expenses;
using Jarvis.Domain.Expenses;

namespace Jarvis.Application.Finance;

/// <summary>
/// Finds charges that repeat on a schedule at a similar amount: streaming, phone, gym, insurance. Weekly, monthly,
/// quarterly and yearly rhythms are recognised. Groceries, eating out, transport and shopping are skipped because
/// the same shop is visited often with varying amounts.
/// </summary>
public static class SubscriptionDetector
{
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        ExpenseCategories.Groceries, ExpenseCategories.Dining, ExpenseCategories.Transport, ExpenseCategories.Shopping
    };

    private const double AmountTolerance = 0.25;
    private const double MinOnRhythm = 0.7;

    public static IReadOnlyList<DetectedSubscription> Detect(IReadOnlyList<Expense> expenses, DateOnly today)
    {
        var found = new List<DetectedSubscription>();
        var groups = expenses
            .Where(x => x.Merchant is not null && !Skipped.Contains(x.Category))
            .GroupBy(x => (Key: ExpenseRules.Key(x.Merchant), x.Currency))
            .Where(g => g.Key.Key.Length > 0 && g.Count() >= 2);

        foreach (var group in groups)
        {
            // One charge per day: a duplicate import or a double tap must not look like a rhythm.
            var charges = group.GroupBy(x => x.SpentOn).Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .OrderBy(x => x.SpentOn).ToArray();
            if (charges.Length < 2) continue;

            var intervals = charges.Zip(charges.Skip(1), (a, b) => b.SpentOn.DayNumber - a.SpentOn.DayNumber)
                .ToArray();
            var cadence = CadenceFor(intervals);
            if (cadence is null) continue;
            var minimum = cadence == SubscriptionCadences.Yearly ? 2 : 3;
            if (charges.Length < minimum) continue;

            var last = charges[^1];
            var amounts = charges.Select(x => x.Amount).Order().ToArray();
            var median = amounts[amounts.Length / 2];
            if (median <= 0 || Math.Abs((double)(last.Amount - median) / (double)median) > AmountTolerance) continue;

            var next = SubscriptionCadences.Next(last.SpentOn, cadence);
            // Long past its next date means it stopped: cancelled, or never really recurring.
            if (next.AddDays(PeriodDays(cadence)) < today) continue;

            var previous = charges.Length >= 2 ? charges[^2].Amount : (decimal?)null;
            found.Add(new DetectedSubscription(group.Key.Key, last.Merchant!, last.Amount, last.Currency, cadence,
                last.SpentOn, next, charges.Length,
                previous is { } before && IsChange(before, last.Amount) ? before : null));
        }
        return found.OrderBy(x => x.NextDueOn).ToArray();
    }

    internal static string? CadenceFor(IReadOnlyList<int> intervals)
    {
        if (intervals.Count == 0) return null;
        foreach (var (cadence, min, max) in new[]
                 {
                     (SubscriptionCadences.Weekly, 6, 8), (SubscriptionCadences.Monthly, 27, 33),
                     (SubscriptionCadences.Quarterly, 85, 96), (SubscriptionCadences.Yearly, 355, 375)
                 })
        {
            var on = intervals.Count(x => x >= min && x <= max);
            if ((double)on / intervals.Count >= MinOnRhythm) return cadence;
        }
        return null;
    }

    public static bool IsChange(decimal before, decimal after) =>
        before > 0 && Math.Abs(after - before) / before >= 0.02m;

    private static int PeriodDays(string cadence) => cadence switch
    {
        SubscriptionCadences.Weekly => 14,
        SubscriptionCadences.Quarterly => 45,
        SubscriptionCadences.Yearly => 60,
        _ => 20
    };
}
