using Jarvis.Application.Expenses;

namespace Jarvis.Application.Finance;

public sealed record MonthFlow(int Year, int Month, decimal Income, decimal Spending);

public sealed record WealthOverview(
    string Currency,
    decimal NetWorth,
    decimal CashTotal,
    decimal PortfolioValue,
    decimal MonthIncome,
    decimal MonthSpending,
    decimal? SavingsRate,
    IReadOnlyList<MonthFlow> CashFlow,
    IReadOnlyList<CurrencyValue> OtherCurrencies);

public interface IWealthService
{
    Task<WealthOverview> OverviewAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken);
}

/// <summary>Adds accounts and the portfolio into a net worth and a monthly income-versus-spending view.</summary>
public sealed class WealthService(IAccountService accounts, IPortfolioService portfolio, IExpenseRepository expenses)
    : IWealthService
{
    private const int FlowMonths = 6;

    public async Task<WealthOverview> OverviewAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken)
    {
        var list = await accounts.ListAsync(ownerId, today, false, cancellationToken);
        var summary = await portfolio.SummaryAsync(ownerId, today, cancellationToken);

        var recent = (await expenses.ListRecentAsync(ownerId, 1, cancellationToken)).FirstOrDefault()?.Currency;
        var currency = list.GroupBy(x => x.Account.Currency).OrderByDescending(g => g.Sum(x => Math.Abs(x.Balance)))
            .Select(g => g.Key).FirstOrDefault() ?? (summary.TotalValue > 0 ? summary.Currency : recent) ??
                       ExpenseRules.DefaultCurrency;

        var cash = list.Where(x => x.Account.Currency == currency).Sum(x => x.Balance);
        var invested = summary.Currency == currency ? summary.TotalValue : 0m;
        var others = list.Where(x => x.Account.Currency != currency).GroupBy(x => x.Account.Currency)
            .Select(g => new CurrencyValue(g.Key, g.Sum(x => x.Balance), 0m, 0m))
            .Concat(summary.Currency != currency && summary.TotalValue > 0
                ? [new CurrencyValue(summary.Currency, summary.TotalValue, summary.TotalCost, summary.UnrealizedProfit)]
                : [])
            .ToArray();

        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(FlowMonths - 1));
        var all = await expenses.QueryAsync(ownerId,
            new TransactionQuery(firstMonth, today, Limit: 10_000), cancellationToken);
        var flow = Enumerable.Range(0, FlowMonths).Select(i =>
        {
            var month = firstMonth.AddMonths(i);
            var rows = all.Where(x => x.Currency == currency && x.SpentOn.Year == month.Year &&
                                      x.SpentOn.Month == month.Month).ToArray();
            return new MonthFlow(month.Year, month.Month,
                rows.Where(x => x.Kind == TransactionKinds.Income).Sum(x => x.Amount),
                rows.Where(x => x.Kind == TransactionKinds.Expense).Sum(x => x.Amount));
        }).ToArray();

        var current = flow[^1];
        decimal? rate = current.Income > 0
            ? Math.Round((current.Income - current.Spending) / current.Income * 100, 1)
            : null;
        return new WealthOverview(currency, cash + invested, cash, invested, current.Income, current.Spending, rate,
            flow, others);
    }
}
