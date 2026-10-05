using Jarvis.Application.Expenses;
using Jarvis.Domain.Expenses;
using Jarvis.Domain.Finance;

namespace Jarvis.Application.Finance;

public static class BudgetCategories
{
    /// <summary>A budget for all spending together.</summary>
    public const string Total = "total";

    public static string? Normalize(string? category)
    {
        var key = ExpenseRules.Key(category);
        if (key is "total" or "all" or "everything" or "overall" or "alles" or "totaal") return Total;
        return ExpenseCategories.Normalize(category);
    }
}

public static class SubscriptionCadences
{
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Quarterly = "quarterly";
    public const string Yearly = "yearly";

    public static DateOnly Next(DateOnly from, string cadence) => cadence switch
    {
        Weekly => from.AddDays(7),
        Quarterly => from.AddMonths(3),
        Yearly => from.AddYears(1),
        _ => from.AddMonths(1)
    };

    /// <summary>What a charge costs per month, so subscriptions with different rhythms can be added up.</summary>
    public static decimal PerMonth(decimal amount, string cadence) => cadence switch
    {
        Weekly => amount * 52m / 12m,
        Quarterly => amount / 3m,
        Yearly => amount / 12m,
        _ => amount
    };
}

public static class SubscriptionStatuses
{
    public const string Active = "active";
    public const string Dismissed = "dismissed";
    public const string Cancelled = "cancelled";

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? status) =>
        status is Active or Dismissed or Cancelled;
}

public static class FinanceRules
{
    public const decimal MaxBudget = 10_000_000m;
    public const int MaxBudgets = 20;
    public const int WarnPercent = 80;
    public const int OverPercent = 100;
    public const int MaxImportBytes = 1_000_000;
    public const int MaxImportRows = 2_000;
    public const int MaxRemindDays = 14;
    public const int DetectionMonths = 14;
    public const int PreviewRows = 25;
}

/// <summary>Where one budget stands in a month.</summary>
public sealed record BudgetStatus(
    Guid Id,
    string Category,
    decimal Limit,
    string Currency,
    decimal Spent,
    decimal Remaining,
    int Percent,
    decimal? Projected,
    string State);

public static class BudgetStates
{
    public const string Ok = "ok";
    public const string Warning = "warning";
    public const string ProjectedOver = "projected_over";
    public const string Over = "over";
}

/// <summary>A recurring charge the detector found in a list of expenses.</summary>
public sealed record DetectedSubscription(
    string MerchantKey,
    string Merchant,
    decimal Amount,
    string Currency,
    string Cadence,
    DateOnly LastChargedOn,
    DateOnly NextDueOn,
    int ChargeCount,
    decimal? PreviousAmount);

public sealed record UpcomingBill(string Merchant, decimal Amount, string Currency, DateOnly DueOn, string Cadence);

public sealed record SpendingForecast(
    int Year,
    int Month,
    string Currency,
    decimal SpentSoFar,
    decimal ExpectedRecurring,
    decimal ProjectedOther,
    decimal ProjectedTotal,
    decimal PreviousMonthTotal,
    int DaysLeft,
    IReadOnlyList<UpcomingBill> Upcoming);

public static class FinanceAlertKinds
{
    public const string Outlier = "outlier";
    public const string PriceIncrease = "price_increase";
    public const string BudgetOver = "budget_over";
    public const string BudgetWarning = "budget_warning";
}

public sealed record FinanceAlert(string Kind, string Title, string Detail, Guid? ExpenseId, DateOnly Date);

public sealed record FinanceOverview(
    int Year,
    int Month,
    string Currency,
    decimal Total,
    IReadOnlyList<BudgetStatus> Budgets,
    SpendingForecast Forecast,
    IReadOnlyList<Subscription> Subscriptions,
    decimal SubscriptionsPerMonth,
    IReadOnlyList<FinanceAlert> Alerts);

public sealed record ImportRow(DateOnly Date, decimal Amount, string Currency, string? Merchant, string? Note,
    string Kind = "expense");

/// <summary><see cref="Income"/> holds the incoming payments; the spending-only import skips them.</summary>
public sealed record CsvParseResult(IReadOnlyList<ImportRow> Spending, int IncomeSkipped, int Unreadable,
    string? Problem, IReadOnlyList<ImportRow>? Income = null);

public sealed record ImportReport(
    bool Committed,
    int Rows,
    int Imported,
    int Duplicates,
    int IncomeSkipped,
    int Unreadable,
    string? Problem,
    IReadOnlyList<ImportRow> Preview);

public enum FinanceFailure
{
    None,
    NotFound,
    Invalid
}

public sealed record FinanceOperation<T>(T? Value, FinanceFailure Failure = FinanceFailure.None, string? Field = null,
    string? Message = null)
{
    public bool Succeeded => Failure == FinanceFailure.None;

    public static FinanceOperation<T> Ok(T value) => new(value);
    public static FinanceOperation<T> NotFound() => new(default, FinanceFailure.NotFound);
    public static FinanceOperation<T> Invalid(string field, string message) =>
        new(default, FinanceFailure.Invalid, field, message);
}

public interface IFinanceRepository
{
    Task<IReadOnlyList<Budget>> ListBudgetsAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Budget?> FindBudgetAsync(Guid ownerId, string category, CancellationToken cancellationToken);
    Task AddBudgetAsync(Budget budget, CancellationToken cancellationToken);
    Task<bool> UpdateBudgetAsync(Budget budget, CancellationToken cancellationToken);
    Task<bool> DeleteBudgetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Subscription?> GetSubscriptionAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken);
    Task<bool> UpdateSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken);
}

public interface IFinanceService
{
    Task<IReadOnlyList<BudgetStatus>> BudgetStatusAsync(Guid ownerId, int year, int month, DateOnly today,
        CancellationToken cancellationToken);
    Task<FinanceOperation<Budget>> SetBudgetAsync(Guid ownerId, string? category, decimal? limit, string? currency,
        CancellationToken cancellationToken);
    Task<bool> DeleteBudgetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Finds recurring charges in recent expenses, keeps the owner's choices, and returns them all.</summary>
    Task<IReadOnlyList<Subscription>> RefreshSubscriptionsAsync(Guid ownerId, DateOnly today,
        CancellationToken cancellationToken);
    Task<FinanceOperation<Subscription>> UpdateSubscriptionAsync(Guid id, Guid ownerId, string? status,
        int? remindDaysBefore, bool clearReminder, DateOnly today, CancellationToken cancellationToken);

    Task<SpendingForecast> ForecastAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinanceAlert>> AlertsAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken);
    Task<FinanceOverview> OverviewAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken);

    Task<ImportReport> ImportAsync(Guid ownerId, string csv, bool commit, string? currency, DateOnly today,
        CancellationToken cancellationToken);
    Task<string> ExportCsvAsync(Guid ownerId, DateOnly from, DateOnly to, string? category,
        CancellationToken cancellationToken);
    Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken);
}
