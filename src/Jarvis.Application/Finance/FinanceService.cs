using System.Globalization;
using System.Text;
using Jarvis.Application.Expenses;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Expenses;
using Jarvis.Domain.Finance;

namespace Jarvis.Application.Finance;

public sealed class FinanceService(IFinanceRepository repository, IExpenseService expenseService,
    IExpenseRepository expenses, IReminderService reminders, IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : IFinanceService
{
    private static readonly TimeOnly ReminderTime = new(9, 0);
    private const int UpcomingDays = 14;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zone = await ZoneAsync(ownerId, cancellationToken);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }

    // ---- Budgets ----

    public async Task<IReadOnlyList<BudgetStatus>> BudgetStatusAsync(Guid ownerId, int year, int month,
        DateOnly today, CancellationToken cancellationToken)
    {
        var budgets = await repository.ListBudgetsAsync(ownerId, cancellationToken);
        if (budgets.Count == 0) return [];
        var (from, to) = ExpenseService.MonthRange(year, month);
        var spending = await expenses.ListAsync(ownerId, from, to, cancellationToken);
        return budgets.Select(b => StatusOf(b, spending, year, month, today))
            .OrderByDescending(x => x.Percent).ToArray();
    }

    public static BudgetStatus StatusOf(Budget budget, IReadOnlyList<Expense> monthExpenses, int year, int month,
        DateOnly today)
    {
        var spent = monthExpenses.Where(x => x.Currency == budget.Currency &&
                                             (budget.Category == BudgetCategories.Total || x.Category == budget.Category))
            .Sum(x => x.Amount);
        var percent = budget.Limit <= 0 ? 0 : (int)Math.Floor(spent * 100m / budget.Limit);
        decimal? projected = null;
        if (today.Year == year && today.Month == month && today.Day >= 5)
            projected = Math.Round(spent / today.Day * DateTime.DaysInMonth(year, month), 2);
        var state = percent >= FinanceRules.OverPercent ? BudgetStates.Over
            : projected > budget.Limit ? BudgetStates.ProjectedOver
            : percent >= FinanceRules.WarnPercent ? BudgetStates.Warning
            : BudgetStates.Ok;
        return new BudgetStatus(budget.Id, budget.Category, budget.Limit, budget.Currency, spent,
            budget.Limit - spent, percent, projected, state);
    }

    public async Task<FinanceOperation<Budget>> SetBudgetAsync(Guid ownerId, string? category, decimal? limit,
        string? currency, CancellationToken cancellationToken)
    {
        var key = BudgetCategories.Normalize(category);
        if (key is null)
            return FinanceOperation<Budget>.Invalid("category",
                "Use a category such as groceries or dining, or total for everything.");
        if (limit is not { } amount || amount <= 0 || amount > FinanceRules.MaxBudget)
            return FinanceOperation<Budget>.Invalid("limit", "Give a monthly limit above zero.");
        if (currency is not null && ExpenseRules.NormalizeCurrency(currency) is null)
            return FinanceOperation<Budget>.Invalid("currency", "Use a three-letter currency code such as EUR.");

        var existing = await repository.FindBudgetAsync(ownerId, key, cancellationToken);
        var now = clock.GetUtcNow();
        var resolvedCurrency = ExpenseRules.NormalizeCurrency(currency) ?? existing?.Currency ??
            (await expenses.ListRecentAsync(ownerId, 1, cancellationToken)).FirstOrDefault()?.Currency ??
            ExpenseRules.DefaultCurrency;
        if (existing is null)
        {
            if ((await repository.ListBudgetsAsync(ownerId, cancellationToken)).Count >= FinanceRules.MaxBudgets)
                return FinanceOperation<Budget>.Invalid("category", $"Use at most {FinanceRules.MaxBudgets} budgets.");
            var created = new Budget(Guid.CreateVersion7(), ownerId, key, ExpenseRules.Round(amount),
                resolvedCurrency, null, 0, now, now);
            await repository.AddBudgetAsync(created, cancellationToken);
            return FinanceOperation<Budget>.Ok(created);
        }

        // A new limit starts the warnings over.
        var updated = existing with
        {
            Limit = ExpenseRules.Round(amount), Currency = resolvedCurrency, AlertedMonth = null, AlertedLevel = 0,
            UpdatedAt = now
        };
        await repository.UpdateBudgetAsync(updated, cancellationToken);
        return FinanceOperation<Budget>.Ok(updated);
    }

    public Task<bool> DeleteBudgetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteBudgetAsync(id, ownerId, cancellationToken);

    // ---- Subscriptions ----

    public async Task<IReadOnlyList<Subscription>> RefreshSubscriptionsAsync(Guid ownerId, DateOnly today,
        CancellationToken cancellationToken)
    {
        var history = await expenses.ListAsync(ownerId, today.AddMonths(-FinanceRules.DetectionMonths), today,
            cancellationToken);
        var detected = SubscriptionDetector.Detect(history, today);
        var stored = (await repository.ListSubscriptionsAsync(ownerId, cancellationToken)).ToList();
        var now = clock.GetUtcNow();
        var seen = new HashSet<Guid>();

        foreach (var item in detected)
        {
            var existing = stored.FirstOrDefault(x => x.MerchantKey == item.MerchantKey && x.Currency == item.Currency);
            if (existing is null)
            {
                var created = new Subscription(Guid.CreateVersion7(), ownerId, item.MerchantKey, item.Merchant,
                    item.Amount, item.Currency, item.Cadence, item.LastChargedOn, item.NextDueOn, item.ChargeCount,
                    item.PreviousAmount, SubscriptionStatuses.Active, null, null, now, now);
                await repository.AddSubscriptionAsync(created, cancellationToken);
                continue;
            }

            seen.Add(existing.Id);
            var revived = existing.Status == SubscriptionStatuses.Cancelled && item.LastChargedOn > existing.LastChargedOn;
            var updated = existing with
            {
                Merchant = item.Merchant, Amount = item.Amount, Cadence = item.Cadence,
                LastChargedOn = item.LastChargedOn, NextDueOn = item.NextDueOn, ChargeCount = item.ChargeCount,
                PreviousAmount = item.PreviousAmount,
                Status = revived ? SubscriptionStatuses.Active : existing.Status, UpdatedAt = now
            };
            updated = await EnsureReminderAsync(updated, today, cancellationToken);
            await repository.UpdateSubscriptionAsync(updated, cancellationToken);
        }

        // An active subscription that no longer repeats has probably been cancelled.
        foreach (var gone in stored.Where(x => x.Status == SubscriptionStatuses.Active && !seen.Contains(x.Id) &&
                                               !detected.Any(d => d.MerchantKey == x.MerchantKey && d.Currency == x.Currency)))
        {
            await CancelReminderAsync(gone, ownerId, cancellationToken);
            await repository.UpdateSubscriptionAsync(gone with
            {
                Status = SubscriptionStatuses.Cancelled, ReminderId = null, UpdatedAt = now
            }, cancellationToken);
        }

        return (await repository.ListSubscriptionsAsync(ownerId, cancellationToken))
            .OrderBy(x => x.Status == SubscriptionStatuses.Active ? 0 : 1).ThenBy(x => x.NextDueOn).ToArray();
    }

    public async Task<FinanceOperation<Subscription>> UpdateSubscriptionAsync(Guid id, Guid ownerId, string? status,
        int? remindDaysBefore, bool clearReminder, DateOnly today, CancellationToken cancellationToken)
    {
        var subscription = await repository.GetSubscriptionAsync(id, ownerId, cancellationToken);
        if (subscription is null) return FinanceOperation<Subscription>.NotFound();
        if (status is not null && !SubscriptionStatuses.IsValid(status))
            return FinanceOperation<Subscription>.Invalid("status", "Use active, dismissed, or cancelled.");
        if (remindDaysBefore is < 0 or > FinanceRules.MaxRemindDays)
            return FinanceOperation<Subscription>.Invalid("remindDaysBefore",
                $"Pick between 0 and {FinanceRules.MaxRemindDays} days.");

        var updated = subscription with
        {
            Status = status ?? subscription.Status,
            RemindDaysBefore = clearReminder ? null : remindDaysBefore ?? subscription.RemindDaysBefore,
            UpdatedAt = clock.GetUtcNow()
        };
        updated = updated.Status == SubscriptionStatuses.Active && updated.RemindDaysBefore is not null
            ? await EnsureReminderAsync(updated, today, cancellationToken)
            : await DropReminderAsync(updated, ownerId, cancellationToken);
        await repository.UpdateSubscriptionAsync(updated, cancellationToken);
        return FinanceOperation<Subscription>.Ok(updated);
    }

    // ---- Forecast, alerts, overview ----

    public async Task<SpendingForecast> ForecastAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken)
    {
        var (from, to) = ExpenseService.MonthRange(today.Year, today.Month);
        var summary = await expenseService.SummarizeMonthAsync(ownerId, today.Year, today.Month, cancellationToken);
        var subscriptions = (await repository.ListSubscriptionsAsync(ownerId, cancellationToken))
            .Where(x => x.Status == SubscriptionStatuses.Active && x.Currency == summary.Currency).ToArray();
        var keys = subscriptions.Select(x => x.MerchantKey).ToHashSet(StringComparer.Ordinal);

        var thisMonth = (await expenses.ListAsync(ownerId, from, to, cancellationToken))
            .Where(x => x.Currency == summary.Currency).ToArray();
        var other = thisMonth.Where(x => !keys.Contains(ExpenseRules.Key(x.Merchant))).Sum(x => x.Amount);
        var daysLeft = to.Day - today.Day;

        decimal dailyOther;
        if (today.Day >= 5)
        {
            dailyOther = other / today.Day;
        }
        else
        {
            // Early in the month the few days so far say little: lean on last month's pace.
            var (pFrom, pTo) = ExpenseService.MonthRange(from.AddMonths(-1).Year, from.AddMonths(-1).Month);
            var previous = (await expenses.ListAsync(ownerId, pFrom, pTo, cancellationToken))
                .Where(x => x.Currency == summary.Currency && !keys.Contains(ExpenseRules.Key(x.Merchant)))
                .Sum(x => x.Amount);
            dailyOther = previous > 0 ? previous / pTo.Day : other / Math.Max(1, today.Day);
        }

        var upcoming = new List<UpcomingBill>();
        foreach (var subscription in subscriptions)
        {
            for (var due = subscription.NextDueOn; due <= today.AddDays(UpcomingDays) && upcoming.Count < 60;
                 due = SubscriptionCadences.Next(due, subscription.Cadence))
            {
                if (due > today) upcoming.Add(new UpcomingBill(subscription.Merchant, subscription.Amount,
                    subscription.Currency, due, subscription.Cadence));
                if (subscription.Cadence == SubscriptionCadences.Weekly && due > to) break;
            }
        }
        var recurring = upcoming.Where(x => x.DueOn <= to).Sum(x => x.Amount);
        var projectedOther = ExpenseRules.Round(dailyOther * daysLeft);
        return new SpendingForecast(today.Year, today.Month, summary.Currency, summary.Total, recurring,
            projectedOther, ExpenseRules.Round(summary.Total + recurring + projectedOther), summary.PreviousTotal,
            daysLeft, upcoming.OrderBy(x => x.DueOn).Where(x => x.DueOn <= today.AddDays(UpcomingDays)).ToArray());
    }

    public async Task<IReadOnlyList<FinanceAlert>> AlertsAsync(Guid ownerId, DateOnly today,
        CancellationToken cancellationToken)
    {
        var history = await expenses.ListAsync(ownerId, today.AddMonths(-12), today, cancellationToken);
        var alerts = new List<FinanceAlert>(FindOutliers(history, today));
        foreach (var subscription in await repository.ListSubscriptionsAsync(ownerId, cancellationToken))
        {
            if (subscription is { Status: SubscriptionStatuses.Active, PreviousAmount: { } before } &&
                subscription.Amount > before)
                alerts.Add(new FinanceAlert(FinanceAlertKinds.PriceIncrease,
                    $"{subscription.Merchant} got more expensive",
                    $"{FormatMoney(before, subscription.Currency)} to {FormatMoney(subscription.Amount, subscription.Currency)} per {CadenceNoun(subscription.Cadence)}.",
                    null, subscription.LastChargedOn));
        }
        foreach (var budget in await BudgetStatusAsync(ownerId, today.Year, today.Month, today, cancellationToken))
        {
            if (budget.State == BudgetStates.Over)
                alerts.Add(new FinanceAlert(FinanceAlertKinds.BudgetOver, $"{Label(budget.Category)} budget is used up",
                    $"{FormatMoney(budget.Spent, budget.Currency)} of {FormatMoney(budget.Limit, budget.Currency)}.",
                    null, today));
            else if (budget.State is BudgetStates.Warning or BudgetStates.ProjectedOver)
                alerts.Add(new FinanceAlert(FinanceAlertKinds.BudgetWarning,
                    $"{Label(budget.Category)} budget is at {budget.Percent}%",
                    budget.Projected is { } projected
                        ? $"At this pace the month ends at {FormatMoney(projected, budget.Currency)} of {FormatMoney(budget.Limit, budget.Currency)}."
                        : $"{FormatMoney(budget.Spent, budget.Currency)} of {FormatMoney(budget.Limit, budget.Currency)}.",
                    null, today));
        }
        return alerts.OrderByDescending(x => x.Date).ToArray();
    }

    public static IReadOnlyList<FinanceAlert> FindOutliers(IReadOnlyList<Expense> history, DateOnly today)
    {
        var alerts = new List<FinanceAlert>();
        foreach (var expense in history.Where(x => x.SpentOn >= today.AddDays(-30)).OrderByDescending(x => x.SpentOn))
        {
            var before = history.Where(x => x.SpentOn < expense.SpentOn && x.Currency == expense.Currency).ToArray();
            var merchantKey = ExpenseRules.Key(expense.Merchant);
            var sameMerchant = merchantKey.Length == 0
                ? []
                : before.Where(x => ExpenseRules.Key(x.Merchant) == merchantKey).Select(x => x.Amount).ToArray();
            if (sameMerchant.Length >= 4 && Median(sameMerchant) is var usual && expense.Amount > usual * 2.5m &&
                expense.Amount - usual >= 20m)
            {
                alerts.Add(new FinanceAlert(FinanceAlertKinds.Outlier, $"Unusually high at {expense.Merchant}",
                    $"{FormatMoney(expense.Amount, expense.Currency)} against a usual {FormatMoney(usual, expense.Currency)}.",
                    expense.Id, expense.SpentOn));
                continue;
            }
            var sameCategory = before.Where(x => x.Category == expense.Category).Select(x => x.Amount).ToArray();
            if (sameCategory.Length >= 8 && Median(sameCategory) is var typical && expense.Amount > typical * 3m &&
                expense.Amount >= 50m)
                alerts.Add(new FinanceAlert(FinanceAlertKinds.Outlier, $"Big {expense.Category} expense",
                    $"{FormatMoney(expense.Amount, expense.Currency)}{(expense.Merchant is null ? "" : " at " + expense.Merchant)}; a typical {expense.Category} expense is {FormatMoney(typical, expense.Currency)}.",
                    expense.Id, expense.SpentOn));
        }
        return alerts.Take(5).ToArray();
    }

    public async Task<FinanceOverview> OverviewAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken)
    {
        var summary = await expenseService.SummarizeMonthAsync(ownerId, today.Year, today.Month, cancellationToken);
        var budgets = await BudgetStatusAsync(ownerId, today.Year, today.Month, today, cancellationToken);
        var subscriptions = await RefreshSubscriptionsAsync(ownerId, today, cancellationToken);
        var active = subscriptions.Where(x => x.Status == SubscriptionStatuses.Active).ToArray();
        var perMonth = active.Where(x => x.Currency == summary.Currency)
            .Sum(x => SubscriptionCadences.PerMonth(x.Amount, x.Cadence));
        return new FinanceOverview(today.Year, today.Month, summary.Currency, summary.Total, budgets,
            await ForecastAsync(ownerId, today, cancellationToken), subscriptions, ExpenseRules.Round(perMonth),
            await AlertsAsync(ownerId, today, cancellationToken));
    }

    // ---- Import and export ----

    public async Task<ImportReport> ImportAsync(Guid ownerId, string csv, bool commit, string? currency,
        DateOnly today, CancellationToken cancellationToken)
    {
        if (Encoding.UTF8.GetByteCount(csv ?? string.Empty) > FinanceRules.MaxImportBytes)
            return new ImportReport(false, 0, 0, 0, 0, 0, "The file is too large. Import at most 1 MB at a time.", []);
        var parsed = BankCsvParser.Parse(csv, currency);
        if (parsed.Problem is not null && parsed.Spending.Count == 0)
            return new ImportReport(false, 0, 0, 0, parsed.IncomeSkipped, parsed.Unreadable, parsed.Problem, []);

        var rows = parsed.Spending.Take(FinanceRules.MaxImportRows).ToArray();
        var problem = parsed.Spending.Count > rows.Length
            ? $"Only the first {FinanceRules.MaxImportRows} rows are imported. Import the rest in another file."
            : null;
        if (!commit)
            return new ImportReport(false, rows.Length, 0, 0, parsed.IncomeSkipped, parsed.Unreadable, problem,
                rows.Take(FinanceRules.PreviewRows).ToArray());

        int imported = 0, duplicates = 0, unreadable = parsed.Unreadable;
        foreach (var row in rows)
        {
            var result = await expenseService.CreateAsync(ownerId, new ExpenseDraft(row.Amount, row.Currency,
                row.Merchant, null, row.Note, row.Date, null, ExpenseSources.Import), today, true, cancellationToken);
            if (!result.Succeeded) unreadable++;
            else if (result.Value!.IsDuplicate) duplicates++;
            else imported++;
        }
        return new ImportReport(true, rows.Length, imported, duplicates, parsed.IncomeSkipped, unreadable, problem,
            rows.Take(FinanceRules.PreviewRows).ToArray());
    }

    public async Task<string> ExportCsvAsync(Guid ownerId, DateOnly from, DateOnly to, string? category,
        CancellationToken cancellationToken)
    {
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 366 * 2) from = to.AddYears(-2);
        var list = await expenses.ListAsync(ownerId, from, to, cancellationToken);
        var key = ExpenseCategories.Normalize(category);
        return ToCsv(list.Where(x => key is null || x.Category == key).OrderBy(x => x.SpentOn).ToArray());
    }

    public static string ToCsv(IReadOnlyList<Expense> list)
    {
        var text = new StringBuilder("Date,Amount,Currency,Merchant,Category,Note,Source\n");
        foreach (var x in list)
            text.Append(x.SpentOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(x.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(x.Currency)
                .Append(',').Append(Cell(x.Merchant)).Append(',').Append(Cell(x.Category)).Append(',')
                .Append(Cell(x.Note)).Append(',').Append(Cell(x.Source)).Append('\n');
        return text.ToString();
    }

    /// <summary>Quotes a cell and defuses spreadsheet formulas ("=1+1", "@SUM").</summary>
    internal static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
        return text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.StartsWith('\'')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }

    // ---- Helpers ----

    private async Task<Subscription> EnsureReminderAsync(Subscription subscription, DateOnly today,
        CancellationToken cancellationToken)
    {
        if (subscription.Status != SubscriptionStatuses.Active || subscription.RemindDaysBefore is not { } days)
            return subscription;
        try
        {
            var zone = await ZoneAsync(subscription.OwnerId, cancellationToken);
            var at = LocalClock.Resolve(subscription.NextDueOn.AddDays(-days).ToDateTime(ReminderTime), zone);
            if (subscription.ReminderId is { } current &&
                await reminders.GetAsync(current, subscription.OwnerId, cancellationToken) is
                    { Status: "pending" } existing)
            {
                if (existing.DueAt == at) return subscription;
                await reminders.CancelAsync(current, subscription.OwnerId, cancellationToken);
            }
            if (at <= clock.GetUtcNow().AddMinutes(1)) return subscription with { ReminderId = null };
            var when = days == 0 ? "today" : $"in {days} day{(days == 1 ? "" : "s")}";
            var reminder = await reminders.CreateAsync(subscription.OwnerId, new CreateReminderRequest(
                $"{subscription.Merchant} charges {FormatMoney(subscription.Amount, subscription.Currency)} {when}",
                at, TimeZoneId: zone.Id), cancellationToken);
            return subscription with { ReminderId = reminder.Id };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return subscription with { ReminderId = null };
        }
    }

    private async Task<Subscription> DropReminderAsync(Subscription subscription, Guid ownerId,
        CancellationToken cancellationToken)
    {
        await CancelReminderAsync(subscription, ownerId, cancellationToken);
        return subscription with { ReminderId = null };
    }

    private async Task CancelReminderAsync(Subscription subscription, Guid ownerId, CancellationToken cancellationToken)
    {
        if (subscription.ReminderId is not { } id) return;
        try
        {
            await reminders.CancelAsync(id, ownerId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private async Task<TimeZoneInfo> ZoneAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        return LocalClock.TryFind(zoneId, out var zone) ? zone : TimeZoneInfo.Utc;
    }

    private static decimal Median(decimal[] values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    public static string FormatMoney(decimal amount, string currency) => currency switch
    {
        "EUR" => "€" + amount.ToString("0.00", CultureInfo.InvariantCulture),
        "USD" => "$" + amount.ToString("0.00", CultureInfo.InvariantCulture),
        "GBP" => "£" + amount.ToString("0.00", CultureInfo.InvariantCulture),
        _ => amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + currency
    };

    public static string Label(string category) =>
        category == BudgetCategories.Total ? "Overall" : char.ToUpperInvariant(category[0]) + category[1..];

    private static string CadenceNoun(string cadence) => cadence switch
    {
        SubscriptionCadences.Weekly => "week",
        SubscriptionCadences.Quarterly => "quarter",
        SubscriptionCadences.Yearly => "year",
        _ => "month"
    };
}

/// <summary>
/// Warns once when spending in a budgeted category reaches 80% and again at 100% of the monthly limit. Only this
/// month's expenses count, so importing old statements stays quiet.
/// </summary>
public sealed class BudgetExpenseObserver(IFinanceRepository repository, IExpenseRepository expenses,
    INotificationRepository notifications, IDailyBriefingRepository briefings, TimeProvider? timeProvider = null)
    : IExpenseObserver
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task OnExpenseCreatedAsync(Expense expense, CancellationToken cancellationToken)
    {
        var zoneId = (await briefings.GetAsync(expense.OwnerId, cancellationToken))?.TimeZoneId;
        var zone = LocalClock.TryFind(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        if (expense.SpentOn.Year != today.Year || expense.SpentOn.Month != today.Month) return;

        var budgets = (await repository.ListBudgetsAsync(expense.OwnerId, cancellationToken)).Where(b =>
            b.Currency == expense.Currency &&
            (b.Category == BudgetCategories.Total || b.Category == expense.Category)).ToArray();
        if (budgets.Length == 0) return;

        var (from, to) = ExpenseService.MonthRange(today.Year, today.Month);
        var month = await expenses.ListAsync(expense.OwnerId, from, to, cancellationToken);
        var monthKey = $"{today.Year:0000}-{today.Month:00}";
        foreach (var budget in budgets)
        {
            var status = FinanceService.StatusOf(budget, month, today.Year, today.Month, today);
            var level = status.Percent >= FinanceRules.OverPercent ? 100 : status.Percent >= FinanceRules.WarnPercent ? 80 : 0;
            var alerted = budget.AlertedMonth == monthKey ? budget.AlertedLevel : 0;
            if (level <= alerted) continue;

            var label = FinanceService.Label(budget.Category);
            await notifications.CreateAsync(expense.OwnerId, "budget.alert",
                level == 100 ? $"{label} budget used up" : $"{label} budget at {status.Percent}%",
                $"{FinanceService.FormatMoney(status.Spent, budget.Currency)} of {FinanceService.FormatMoney(budget.Limit, budget.Currency)} this month.",
                budget.Id, cancellationToken);
            await repository.UpdateBudgetAsync(budget with
            {
                AlertedMonth = monthKey, AlertedLevel = level, UpdatedAt = clock.GetUtcNow()
            }, cancellationToken);
        }
    }
}
