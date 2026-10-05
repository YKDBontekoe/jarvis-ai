using Jarvis.Agents.Finance;
using Jarvis.Application.Conversations;
using Jarvis.Application.Expenses;
using Jarvis.Application.Files;
using Jarvis.Application.Finance;
using Jarvis.Domain.Expenses;
using Jarvis.Domain.Finance;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class AccountsAndPortfolioTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000bbbb");
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 5);

    // ---- Portfolio math ----

    private static InvestmentTrade Trade(string kind, string date, decimal quantity, decimal price, decimal fees = 0) =>
        new(Guid.NewGuid(), Owner, Guid.NewGuid(), kind, DateOnly.Parse(date), quantity, price, fees, null, Now);

    [Fact]
    public void Position_uses_average_cost_and_realizes_profit_on_sells()
    {
        var position = PortfolioMath.Calculate([
            Trade(TradeKinds.Buy, "2026-01-01", 10, 100),
            Trade(TradeKinds.Buy, "2026-02-01", 10, 120, fees: 5),
            Trade(TradeKinds.Sell, "2026-03-01", 5, 150, fees: 1)
        ]);
        Assert.Equal(15, position.Quantity);
        Assert.Equal(2205m / 20m * 15m, position.CostBasis);
        Assert.Equal(110.25m, position.AverageCost);
        Assert.Equal(5 * 150m - 1m - 110.25m * 5, position.RealizedProfit);
    }

    [Fact]
    public void Position_handles_dividends_splits_and_selling_everything()
    {
        var position = PortfolioMath.Calculate([
            Trade(TradeKinds.Buy, "2026-01-01", 10, 100),
            Trade(TradeKinds.Split, "2026-02-01", 2, 0),
            Trade(TradeKinds.Dividend, "2026-03-01", 20, 0.5m),
            Trade(TradeKinds.Sell, "2026-04-01", 20, 60)
        ]);
        Assert.Equal(0, position.Quantity);
        Assert.Equal(0, position.CostBasis);
        Assert.Equal(10m, position.Dividends);
        Assert.Equal(200m, position.RealizedProfit);
    }

    [Fact]
    public void Position_orders_trades_by_date_not_by_input_order()
    {
        var position = PortfolioMath.Calculate([
            Trade(TradeKinds.Sell, "2026-03-01", 5, 20),
            Trade(TradeKinds.Buy, "2026-01-01", 10, 10)
        ]);
        Assert.Equal(5, position.Quantity);
        Assert.Equal(50m, position.RealizedProfit);
    }

    [Fact]
    public void Summary_values_holdings_at_the_last_price_and_groups_other_currencies()
    {
        var now = Now;
        var eur = new Holding(Guid.NewGuid(), Owner, null, "VWRL.AS", "World", AssetTypes.Etf, "EUR", 110m, now,
            "manual", now, now);
        var usd = new Holding(Guid.NewGuid(), Owner, null, "AAPL", "Apple", AssetTypes.Stock, "USD", 200m, now,
            "manual", now, now);
        var summary = PortfolioService.Build([
            PortfolioService.Summarize(eur, [Trade(TradeKinds.Buy, "2026-01-01", 10, 100)], now),
            PortfolioService.Summarize(usd, [Trade(TradeKinds.Buy, "2026-01-01", 1, 150)], now)
        ], false);

        Assert.Equal("EUR", summary.Currency);
        Assert.Equal(1100m, summary.TotalValue);
        Assert.Equal(100m, summary.UnrealizedProfit);
        var other = Assert.Single(summary.OtherCurrencies);
        Assert.Equal(("USD", 200m), (other.Currency, other.Value));
        Assert.Equal(100m, Assert.Single(summary.Allocation).Percent);
    }

    [Fact]
    public async Task Selling_more_than_held_is_refused_and_the_first_buy_sets_a_price()
    {
        var (portfolio, _, _) = CreatePortfolio();
        var holding = (await portfolio.AddHoldingAsync(Owner, new HoldingDraft(" vwrl.as ", "World ETF",
            AssetTypes.Etf, "eur"), default)).Value!;
        Assert.Equal("VWRL.AS", holding.Symbol);

        await portfolio.AddTradeAsync(holding.Id, Owner, new TradeDraft(TradeKinds.Buy, null, 4, 100m), Today, default);
        var tooMany = await portfolio.AddTradeAsync(holding.Id, Owner, new TradeDraft(TradeKinds.Sell, null, 5, 120m),
            Today, default);

        Assert.Equal(FinanceFailure.Invalid, tooMany.Failure);
        Assert.Equal("quantity", tooMany.Field);
        var summary = await portfolio.GetHoldingAsync(holding.Id, Owner, Today, default);
        Assert.Equal(100m, summary!.Holding.LastPrice);
        Assert.Equal(400m, summary.MarketValue);
    }

    [Fact]
    public async Task Holdings_and_trades_are_owner_scoped_and_validated()
    {
        var (portfolio, _, _) = CreatePortfolio();
        var holding = (await portfolio.AddHoldingAsync(Owner, new HoldingDraft("AAPL"), default)).Value!;

        Assert.Null(await portfolio.GetHoldingAsync(holding.Id, Other, Today, default));
        Assert.Equal(FinanceFailure.NotFound,
            (await portfolio.AddTradeAsync(holding.Id, Other, new TradeDraft(TradeKinds.Buy, null, 1, 1m), Today,
                default)).Failure);
        Assert.Equal("symbol", (await portfolio.AddHoldingAsync(Owner, new HoldingDraft("AAPL"), default)).Field);
        Assert.Equal("symbol", (await portfolio.AddHoldingAsync(Owner, new HoldingDraft("   "), default)).Field);
        Assert.Equal("quantity", (await portfolio.AddTradeAsync(holding.Id, Owner,
            new TradeDraft(TradeKinds.Buy, null, 0, 1m), Today, default)).Field);
        Assert.Equal("tradedOn", (await portfolio.AddTradeAsync(holding.Id, Owner,
            new TradeDraft(TradeKinds.Buy, Today.AddDays(30), 1, 1m), Today, default)).Field);
        Assert.Equal("price", (await portfolio.SetPriceAsync(holding.Id, Owner, -3m, Today, default)).Field);
    }

    [Fact]
    public async Task Refresh_does_nothing_without_a_provider_and_updates_prices_with_one()
    {
        var (without, _, _) = CreatePortfolio();
        Assert.Equal(0, await without.RefreshPricesAsync(Owner, Today, true, default));

        var provider = new FakeQuotes(new Dictionary<string, decimal> { ["AAPL"] = 210m });
        var (portfolio, _, repository) = CreatePortfolio(provider);
        var holding = (await portfolio.AddHoldingAsync(Owner, new HoldingDraft("AAPL"), default)).Value!;
        await portfolio.AddTradeAsync(holding.Id, Owner, new TradeDraft(TradeKinds.Buy, null, 2, 100m), Today, default);
        await portfolio.SetPriceAsync(holding.Id, Owner, 100m, Today, default);

        Assert.Equal(0, await portfolio.RefreshPricesAsync(Owner, Today, false, default));
        Assert.Equal(1, await portfolio.RefreshPricesAsync(Owner, Today, true, default));
        var updated = (await repository.GetHoldingAsync(holding.Id, Owner, default))!;
        Assert.Equal((210m, "provider"), (updated.LastPrice, updated.PriceSource));
    }

    // ---- Accounts ----

    [Fact]
    public async Task Balance_adds_income_subtracts_spending_and_moves_transfers_between_accounts()
    {
        var (_, expenses, accounts, service, _) = CreateLedger();
        var checking = (await accounts.CreateAsync(Owner, new AccountDraft("Checking", AccountTypes.Checking, "EUR",
            OpeningBalance: 1000m, OpeningOn: new DateOnly(2026, 10, 1)), Today, default)).Value!;
        var savings = (await accounts.CreateAsync(Owner, new AccountDraft("Savings", AccountTypes.Savings, "EUR"),
            Today, default)).Value!;

        await Create(service, new ExpenseDraft(50m, Merchant: "Albert Heijn", AccountId: checking.Id));
        await Create(service, new ExpenseDraft(2500m, Merchant: "Employer", Kind: TransactionKinds.Income,
            Category: "salary", AccountId: checking.Id));
        await Create(service, new ExpenseDraft(300m, Kind: TransactionKinds.Transfer, AccountId: checking.Id,
            TransferAccountId: savings.Id));
        // Before the opening date: already part of the opening balance.
        await Create(service, new ExpenseDraft(999m, AccountId: checking.Id, SpentOn: new DateOnly(2026, 9, 20)));

        var list = await accounts.ListAsync(Owner, Today, false, default);
        var c = list.Single(x => x.Account.Id == checking.Id);
        var s = list.Single(x => x.Account.Id == savings.Id);
        Assert.Equal(1000m - 50m + 2500m - 300m, c.Balance);
        Assert.Equal(2500m, c.MonthIn);
        Assert.Equal(350m, c.MonthOut);
        Assert.Equal(300m, s.Balance);
        // Budgets and summaries still see spending only.
        Assert.Equal(2, (await expenses.ListAsync(Owner, new DateOnly(2026, 9, 1), Today, default)).Count);
    }

    [Fact]
    public async Task Reconcile_moves_the_opening_balance_to_match_the_bank()
    {
        var (_, _, accounts, service, _) = CreateLedger();
        var account = (await accounts.CreateAsync(Owner, new AccountDraft("Checking", OpeningBalance: 100m), Today,
            default)).Value!;
        await Create(service, new ExpenseDraft(40m, AccountId: account.Id));

        var result = await accounts.ReconcileAsync(account.Id, Owner, 75m, Today, default);

        Assert.Equal(15m, result.Value!.Adjustment);
        Assert.Equal(75m, (await accounts.GetAsync(account.Id, Owner, Today, default))!.Balance);
        Assert.Equal("balance", (await accounts.ReconcileAsync(account.Id, Owner, null, Today, default)).Field);
    }

    [Fact]
    public async Task Accounts_are_owner_scoped_and_validated()
    {
        var (_, _, accounts, service, _) = CreateLedger();
        var mine = (await accounts.CreateAsync(Owner, new AccountDraft("Mine", Last4: "NL91 ABNA 0417 1643 00"),
            Today, default)).Value!;
        var other = (await accounts.CreateAsync(Other, new AccountDraft("Theirs"), Today, default)).Value!;

        Assert.Equal("4300", mine.Last4);
        Assert.Null(await accounts.GetAsync(mine.Id, Other, Today, default));
        Assert.Equal("name", (await accounts.CreateAsync(Owner, new AccountDraft(" "), Today, default)).Field);
        Assert.Equal("type", (await accounts.CreateAsync(Owner, new AccountDraft("X", "bogus"), Today, default)).Field);
        Assert.Equal("currency",
            (await accounts.CreateAsync(Owner, new AccountDraft("X", Currency: "euro"), Today, default)).Field);

        // A transaction cannot point at someone else's account.
        var stolen = await service.CreateAsync(Owner, new ExpenseDraft(5m, AccountId: other.Id), Today, false, default);
        Assert.Equal("accountId", stolen.Field);
        var badTransfer = await service.CreateAsync(Owner, new ExpenseDraft(5m, Kind: TransactionKinds.Transfer,
            AccountId: mine.Id, TransferAccountId: mine.Id), Today, false, default);
        Assert.Equal("transferAccountId", badTransfer.Field);
    }

    [Fact]
    public async Task Transfers_need_the_same_currency_and_income_gets_an_income_category()
    {
        var (_, _, accounts, service, _) = CreateLedger();
        var eur = (await accounts.CreateAsync(Owner, new AccountDraft("EUR", Currency: "EUR"), Today, default)).Value!;
        var usd = (await accounts.CreateAsync(Owner, new AccountDraft("USD", Currency: "USD"), Today, default)).Value!;

        var cross = await service.CreateAsync(Owner, new ExpenseDraft(5m, Kind: TransactionKinds.Transfer,
            AccountId: eur.Id, TransferAccountId: usd.Id), Today, false, default);
        Assert.Equal("transferAccountId", cross.Field);

        var income = await service.CreateAsync(Owner, new ExpenseDraft(3000m, Merchant: "ACME salaris",
            Kind: TransactionKinds.Income, AccountId: eur.Id), Today, false, default);
        Assert.Equal(IncomeCategories.Salary, income.Value!.Expense.Category);
        Assert.Equal("EUR", income.Value.Expense.Currency);

        var unknown = await service.CreateAsync(Owner, new ExpenseDraft(5m, Kind: TransactionKinds.Income,
            Category: "groceries"), Today, false, default);
        Assert.Equal("category", unknown.Field);
    }

    [Fact]
    public async Task Statement_import_keeps_income_for_the_account_and_skips_repeats()
    {
        var (_, expenses, accounts, _, _) = CreateLedger();
        var account = (await accounts.CreateAsync(Owner, new AccountDraft("Checking", OpeningBalance: 0m,
            OpeningOn: new DateOnly(2026, 9, 1)), Today, default)).Value!;
        const string csv = "Date,Amount,Description\n2026-10-01,-12.50,Albert Heijn\n2026-10-02,2500.00,Salaris ACME\n";

        var preview = (await accounts.ImportAsync(account.Id, Owner, csv, false, Today, default)).Value!;
        Assert.Equal((false, 2), (preview.Committed, preview.Rows));
        Assert.Empty(await expenses.ListAccountMovementsAsync(Owner, default));

        var done = (await accounts.ImportAsync(account.Id, Owner, csv, true, Today, default)).Value!;
        Assert.Equal(2, done.Imported);
        var again = (await accounts.ImportAsync(account.Id, Owner, csv, true, Today, default)).Value!;
        Assert.Equal((0, 2), (again.Imported, again.Duplicates));
        Assert.Equal(2487.5m, (await accounts.GetAsync(account.Id, Owner, Today, default))!.Balance);
        Assert.Equal(FinanceFailure.NotFound,
            (await accounts.ImportAsync(account.Id, Other, csv, true, Today, default)).Failure);
    }

    [Fact]
    public void Bank_csv_returns_incoming_payments_separately_from_spending()
    {
        var parsed = BankCsvParser.Parse("Date,Amount,Description\n2026-10-01,-5,Shop\n2026-10-02,10,Refund\n", "EUR");
        Assert.Single(parsed.Spending);
        Assert.Equal(1, parsed.IncomeSkipped);
        Assert.Equal(TransactionKinds.Income, Assert.Single(parsed.Income!).Kind);
    }

    // ---- Net worth ----

    [Fact]
    public async Task Wealth_adds_accounts_and_portfolio_and_reports_the_savings_rate()
    {
        var (portfolio, _, _) = CreatePortfolio();
        var (_, _, accounts, service, repository) = CreateLedger();
        var wealth = new WealthService(accounts, portfolio, repository);
        var account = (await accounts.CreateAsync(Owner, new AccountDraft("Checking", Currency: "EUR",
            OpeningBalance: 1000m, OpeningOn: new DateOnly(2026, 10, 1)), Today, default)).Value!;
        await Create(service, new ExpenseDraft(1000m, Kind: TransactionKinds.Income, AccountId: account.Id));
        await Create(service, new ExpenseDraft(250m, AccountId: account.Id));

        var overview = await wealth.OverviewAsync(Owner, Today, default);

        Assert.Equal("EUR", overview.Currency);
        Assert.Equal(1750m, overview.CashTotal);
        Assert.Equal(1750m, overview.NetWorth);
        Assert.Equal((1000m, 250m, 75m), (overview.MonthIncome, overview.MonthSpending, overview.SavingsRate));
        Assert.Equal(6, overview.CashFlow.Count);
        Assert.Equal(10, overview.CashFlow[^1].Month);
    }

    // ---- Agent tools ----

    [Fact]
    public async Task Agent_tools_add_accounts_log_income_record_trades_and_describe_the_result()
    {
        var (portfolio, _, _) = CreatePortfolio();
        var (_, _, accounts, service, repository) = CreateLedger();
        var tools = new WealthAgentTools(accounts, service, portfolio, new WealthService(accounts, portfolio, repository),
            new FixedFinance(), new User());

        Assert.Contains("No accounts yet", await tools.GetAccountsAsync());
        Assert.Contains("Added ING", await tools.AddAccountAsync("ING", "checking", "EUR", 500m));
        Assert.Contains("Logged income of", await tools.LogIncomeAsync(1200m, "ACME", account: "ing"));
        Assert.Contains("no account with that name", await tools.LogIncomeAsync(5m, account: "nope"));
        Assert.Contains("€1700.00", await tools.GetAccountsAsync());
        Assert.Contains("+", await tools.GetTransactionsAsync(kind: "income"));

        Assert.Contains("Recorded buy", await tools.RecordTradeAsync("vwrl.as", "buy", 10, 105m, name: "World",
            assetType: "etf"));
        Assert.Contains("only hold", await tools.RecordTradeAsync("VWRL.AS", "sell", 50, 110m));
        Assert.Contains("Price of VWRL.AS", await tools.SetHoldingPriceAsync("VWRL.AS", 110m));
        var summary = await tools.GetPortfolioAsync();
        Assert.Contains("VWRL.AS", summary);
        Assert.Contains("€1100.00", summary);
        Assert.Contains("not hold", await tools.SetHoldingPriceAsync("NOPE", 1m));
    }

    private sealed class User : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class FixedFinance : IFinanceService
    {
        public Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken) => Task.FromResult(Today);
        public Task<IReadOnlyList<BudgetStatus>> BudgetStatusAsync(Guid ownerId, int year, int month, DateOnly today,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FinanceOperation<Budget>> SetBudgetAsync(Guid ownerId, string? category, decimal? limit,
            string? currency, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteBudgetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Subscription>> RefreshSubscriptionsAsync(Guid ownerId, DateOnly today,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FinanceOperation<Subscription>> UpdateSubscriptionAsync(Guid id, Guid ownerId, string? status,
            int? remindDaysBefore, bool clearReminder, DateOnly today, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<SpendingForecast> ForecastAsync(Guid ownerId, DateOnly today,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<FinanceAlert>> AlertsAsync(Guid ownerId, DateOnly today,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FinanceOverview> OverviewAsync(Guid ownerId, DateOnly today,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImportReport> ImportAsync(Guid ownerId, string csv, bool commit, string? currency, DateOnly today,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> ExportCsvAsync(Guid ownerId, DateOnly from, DateOnly to, string? category,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // ---- Helpers ----

    private static async Task Create(IExpenseService service, ExpenseDraft draft) =>
        Assert.True((await service.CreateAsync(Owner, draft, Today, false, default)).Succeeded);

    private static (IPortfolioService Portfolio, FakeAccounts Accounts, FakePortfolioRepository Repository)
        CreatePortfolio(IQuoteProvider? quotes = null)
    {
        var accounts = new FakeAccounts();
        var repository = new FakePortfolioRepository();
        return (new PortfolioService(repository, accounts, quotes ?? new NullQuoteProvider(), new Clock(Now)),
            accounts, repository);
    }

    private static (IExpenseService Service, IExpenseRepository Expenses, IAccountService Accounts,
        IExpenseService Same, IExpenseRepository Repository) CreateLedger()
    {
        var repository = new FakeExpenseRepository();
        var accountRepository = new FakeAccounts();
        var service = new ExpenseService(repository, Fake<IFileService>.Create(), new Clock(Now),
            accounts: accountRepository);
        var accounts = new AccountService(accountRepository, repository, service, new Clock(Now));
        return (service, repository, accounts, service, repository);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeQuotes(Dictionary<string, decimal> prices) : IQuoteProvider
    {
        public bool IsConfigured => true;

        public Task<IReadOnlyDictionary<string, Quote>> GetQuotesAsync(IReadOnlyCollection<string> symbols,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, Quote>>(symbols.Where(prices.ContainsKey)
                .ToDictionary(x => x, x => new Quote(x, prices[x], Now)));
    }

    private sealed class FakeAccounts : IAccountRepository
    {
        private readonly List<FinancialAccount> items = [];

        public Task<IReadOnlyList<FinancialAccount>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FinancialAccount>>(items.Where(x => x.OwnerId == ownerId).ToArray());

        public Task<FinancialAccount?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(items.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task AddAsync(FinancialAccount account, CancellationToken cancellationToken)
        {
            items.Add(account);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(FinancialAccount account, CancellationToken cancellationToken)
        {
            var index = items.FindIndex(x => x.Id == account.Id && x.OwnerId == account.OwnerId);
            if (index < 0) return Task.FromResult(false);
            items[index] = account;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(items.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);
    }

    private sealed class FakePortfolioRepository : IPortfolioRepository
    {
        private readonly List<Holding> holdings = [];
        private readonly List<InvestmentTrade> trades = [];
        private readonly Dictionary<(Guid, DateOnly), decimal> prices = [];

        public Task<IReadOnlyList<Holding>> ListHoldingsAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Holding>>(holdings.Where(x => x.OwnerId == ownerId).ToArray());

        public Task<Holding?> GetHoldingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(holdings.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task AddHoldingAsync(Holding holding, CancellationToken cancellationToken)
        {
            holdings.Add(holding);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateHoldingAsync(Holding holding, CancellationToken cancellationToken)
        {
            var index = holdings.FindIndex(x => x.Id == holding.Id && x.OwnerId == holding.OwnerId);
            if (index < 0) return Task.FromResult(false);
            holdings[index] = holding;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteHoldingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(holdings.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);

        public Task<IReadOnlyList<InvestmentTrade>> ListTradesAsync(Guid ownerId, Guid? holdingId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InvestmentTrade>>(trades
                .Where(x => x.OwnerId == ownerId && (holdingId is null || x.HoldingId == holdingId)).ToArray());

        public Task<InvestmentTrade?> GetTradeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(trades.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task AddTradeAsync(InvestmentTrade trade, CancellationToken cancellationToken)
        {
            trades.Add(trade);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteTradeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(trades.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);

        public Task SavePricePointAsync(Guid holdingId, DateOnly date, decimal price,
            CancellationToken cancellationToken)
        {
            prices[(holdingId, date)] = price;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PricePoint>> ListPriceHistoryAsync(Guid holdingId, Guid ownerId, int days,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PricePoint>>(prices.Where(x => x.Key.Item1 == holdingId)
                .Select(x => new PricePoint(x.Key.Item2, x.Value)).ToArray());
    }

    private sealed class FakeExpenseRepository : IExpenseRepository
    {
        private readonly List<Expense> items = [];

        public Task<IReadOnlyList<Expense>> ListAsync(Guid ownerId, DateOnly from, DateOnly to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Expense>>(items.Where(x => x.OwnerId == ownerId &&
                x.Kind == TransactionKinds.Expense && x.SpentOn >= from && x.SpentOn <= to).ToArray());

        public Task<IReadOnlyList<Expense>> ListRecentAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Expense>>(items.Where(x => x.OwnerId == ownerId &&
                x.Kind == TransactionKinds.Expense).AsEnumerable().Reverse().Take(limit).ToArray());

        public Task<Expense?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(items.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task<IReadOnlyList<Expense>> QueryAsync(Guid ownerId, TransactionQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Expense>>(items.Where(x => x.OwnerId == ownerId &&
                    (query.From is null || x.SpentOn >= query.From) && (query.To is null || x.SpentOn <= query.To) &&
                    (query.AccountId is null || x.AccountId == query.AccountId || x.TransferAccountId == query.AccountId) &&
                    (query.Kind is null || x.Kind == query.Kind))
                .OrderByDescending(x => x.SpentOn).Skip(query.Offset).Take(query.Limit).ToArray());

        public Task<IReadOnlyList<Expense>> ListAccountMovementsAsync(Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Expense>>(items.Where(x => x.OwnerId == ownerId &&
                (x.AccountId is not null || x.TransferAccountId is not null)).ToArray());

        public Task AddAsync(Expense expense, CancellationToken cancellationToken)
        {
            items.Add(expense);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(Expense expense, CancellationToken cancellationToken)
        {
            var index = items.FindIndex(x => x.Id == expense.Id && x.OwnerId == expense.OwnerId);
            if (index < 0) return Task.FromResult(false);
            items[index] = expense;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(items.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);
    }
}
