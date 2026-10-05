using Jarvis.Application.Expenses;
using Jarvis.Application.Finance;
using Jarvis.Domain.Expenses;
using Jarvis.Domain.Finance;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class AccountsAndPortfolioPersistenceTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 10, 5);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Accounts_and_transactions_are_stored_per_owner_and_keep_their_kind()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var accounts = new AccountRepository(database);
        var expenses = new ExpenseRepository(database);
        var checking = Account(owner, "Checking");
        var savings = Account(owner, "Savings");
        await accounts.AddAsync(checking, default);
        await accounts.AddAsync(savings, default);

        await expenses.AddAsync(Tx(owner, 12.5m, TransactionKinds.Expense, "groceries", checking.Id), default);
        await expenses.AddAsync(Tx(owner, 2000m, TransactionKinds.Income, IncomeCategories.Salary, checking.Id), default);
        await expenses.AddAsync(Tx(owner, 300m, TransactionKinds.Transfer, IncomeCategories.Transfer, checking.Id,
            savings.Id), default);

        // Budgets and subscriptions only ever see spending.
        var spending = await expenses.ListAsync(owner, Day, Day, default);
        Assert.Equal(12.5m, Assert.Single(spending).Amount);
        Assert.Equal(3, (await expenses.QueryAsync(owner, new TransactionQuery(), default)).Count);
        Assert.Single(await expenses.QueryAsync(owner, new TransactionQuery(Kind: TransactionKinds.Income), default));
        // The savings account is only touched by the incoming transfer.
        Assert.Equal(TransactionKinds.Transfer,
            Assert.Single(await expenses.QueryAsync(owner, new TransactionQuery(AccountId: savings.Id), default)).Kind);
        Assert.Equal(3, (await expenses.ListAccountMovementsAsync(owner, default)).Count);

        Assert.Empty(await expenses.QueryAsync(other, new TransactionQuery(), default));
        Assert.Null(await accounts.GetAsync(checking.Id, other, default));
        Assert.Empty(await accounts.ListAsync(other, default));
    }

    [Fact]
    public async Task Searching_matches_shop_and_note_and_escapes_wildcards()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var expenses = new ExpenseRepository(database);
        await expenses.AddAsync(Tx(owner, 5m, TransactionKinds.Expense, "groceries", null, merchant: "Albert Heijn"),
            default);
        await expenses.AddAsync(Tx(owner, 6m, TransactionKinds.Expense, "other", null, merchant: "100% Juice"), default);

        Assert.Single(await expenses.QueryAsync(owner, new TransactionQuery(Search: "albert"), default));
        Assert.Single(await expenses.QueryAsync(owner, new TransactionQuery(Search: "100%"), default));
        // A bare % is a literal character, not "match everything".
        Assert.Equal("100% Juice",
            Assert.Single(await expenses.QueryAsync(owner, new TransactionQuery(Search: "%"), default)).Merchant);
    }

    [Fact]
    public async Task Deleting_an_account_keeps_its_transactions_without_the_link()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var accounts = new AccountRepository(database);
        var expenses = new ExpenseRepository(database);
        var account = Account(owner, "Old");
        await accounts.AddAsync(account, default);
        var tx = Tx(owner, 9m, TransactionKinds.Expense, "other", account.Id);
        await expenses.AddAsync(tx, default);

        Assert.True(await accounts.DeleteAsync(account.Id, owner, default));

        await using var reader = CreateDbContext();
        var stored = (await new ExpenseRepository(reader).GetAsync(tx.Id, owner, default))!;
        Assert.Null(stored.AccountId);
        Assert.Equal(9m, stored.Amount);
    }

    [Fact]
    public async Task The_database_rejects_an_unknown_kind_and_an_unknown_category()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var expenses = new ExpenseRepository(database);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            expenses.AddAsync(Tx(owner, 1m, "gift", "other", null), default));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new ExpenseRepository(CreateDbContext()).AddAsync(Tx(owner, 1m, TransactionKinds.Expense, "bogus", null),
                default));
    }

    [Fact]
    public async Task Holdings_trades_and_prices_round_trip_and_cascade_on_delete()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var repository = new PortfolioRepository(database);
        var now = DateTimeOffset.UtcNow;
        var holding = new Holding(Guid.CreateVersion7(), owner, null, "VWRL.AS", "World", AssetTypes.Etf, "EUR", null,
            null, null, now, now);
        await repository.AddHoldingAsync(holding, default);
        await repository.AddTradeAsync(new InvestmentTrade(Guid.CreateVersion7(), owner, holding.Id, TradeKinds.Buy,
            Day, 10.5m, 105.123456m, 2m, "first", now), default);
        Assert.True(await repository.UpdateHoldingAsync(holding with
        {
            LastPrice = 110.5m, LastPriceAt = now, PriceSource = "manual"
        }, default));
        await repository.SavePricePointAsync(holding.Id, Day, 110.5m, default);
        await repository.SavePricePointAsync(holding.Id, Day, 111m, default);

        await using var reader = CreateDbContext();
        var read = new PortfolioRepository(reader);
        var stored = (await read.GetHoldingAsync(holding.Id, owner, default))!;
        Assert.Equal(110.5m, stored.LastPrice);
        var trade = Assert.Single(await read.ListTradesAsync(owner, holding.Id, default));
        Assert.Equal((10.5m, 105.123456m), (trade.Quantity, trade.Price));
        var history = Assert.Single(await read.ListPriceHistoryAsync(holding.Id, owner, 3650, default));
        Assert.Equal(111m, history.Price);

        // Other owners see nothing.
        Assert.Null(await read.GetHoldingAsync(holding.Id, other, default));
        Assert.Empty(await read.ListTradesAsync(other, null, default));
        Assert.Empty(await read.ListPriceHistoryAsync(holding.Id, other, 3650, default));

        Assert.True(await read.DeleteHoldingAsync(holding.Id, owner, default));
        Assert.Empty(await new PortfolioRepository(CreateDbContext()).ListTradesAsync(owner, null, default));
    }

    [Fact]
    public async Task A_holding_is_unique_per_owner_symbol_and_account_even_without_an_account()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var repository = new PortfolioRepository(database);
        var now = DateTimeOffset.UtcNow;
        Holding Make() => new(Guid.CreateVersion7(), owner, null, "AAPL", "Apple", AssetTypes.Stock, "USD", null, null,
            null, now, now);
        await repository.AddHoldingAsync(Make(), default);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new PortfolioRepository(CreateDbContext()).AddHoldingAsync(Make(), default));
    }

    [Fact]
    public async Task The_database_rejects_a_zero_quantity_trade_and_an_unknown_trade_kind()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var repository = new PortfolioRepository(database);
        var now = DateTimeOffset.UtcNow;
        var holding = new Holding(Guid.CreateVersion7(), owner, null, "MSFT", "Microsoft", AssetTypes.Stock, "USD",
            null, null, null, now, now);
        await repository.AddHoldingAsync(holding, default);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new PortfolioRepository(CreateDbContext()).AddTradeAsync(new InvestmentTrade(Guid.CreateVersion7(), owner,
                holding.Id, TradeKinds.Buy, Day, 0m, 1m, 0m, null, now), default));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new PortfolioRepository(CreateDbContext()).AddTradeAsync(new InvestmentTrade(Guid.CreateVersion7(), owner,
                holding.Id, "short", Day, 1m, 1m, 0m, null, now), default));
    }

    private static FinancialAccount Account(Guid owner, string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new FinancialAccount(Guid.CreateVersion7(), owner, name, AccountTypes.Checking, "EUR", "ING", "4300",
            100m, Day.AddDays(-30), false, now, now);
    }

    private static Expense Tx(Guid owner, decimal amount, string kind, string category, Guid? account,
        Guid? transferTo = null, string? merchant = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Expense(Guid.CreateVersion7(), owner, amount, "EUR", merchant, category, null, Day, null,
            ExpenseSources.App, now, now, kind, account, transferTo);
    }

    private JarvisDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), postgres => postgres.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
