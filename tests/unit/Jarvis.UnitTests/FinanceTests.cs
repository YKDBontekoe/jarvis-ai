using Jarvis.Agents.Finance;
using Jarvis.Application.Expenses;
using Jarvis.Application.Files;
using Jarvis.Application.Finance;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Expenses;
using Jarvis.Domain.Finance;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class FinanceTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static Expense Spend(DateOnly date, decimal amount, string? merchant, string category = "other",
        string currency = "EUR") =>
        new(Guid.NewGuid(), Owner, amount, currency, merchant, category, null, date, null, "app", Now, Now);

    private static IEnumerable<Expense> Monthly(string merchant, decimal amount, int months, DateOnly last,
        string category = "subscriptions") =>
        Enumerable.Range(0, months).Select(i => Spend(last.AddMonths(-i), amount, merchant, category));

    [Fact]
    public void Monthly_charges_at_a_steady_amount_are_a_subscription()
    {
        var expenses = Monthly("Netflix", 13.99m, 5, new DateOnly(2026, 10, 3)).ToArray();

        var found = Assert.Single(SubscriptionDetector.Detect(expenses, Today));

        Assert.Equal("netflix", found.MerchantKey);
        Assert.Equal(SubscriptionCadences.Monthly, found.Cadence);
        Assert.Equal(new DateOnly(2026, 11, 3), found.NextDueOn);
        Assert.Null(found.PreviousAmount);
    }

    [Fact]
    public void A_price_change_is_remembered()
    {
        var expenses = Monthly("Spotify", 10.99m, 4, new DateOnly(2026, 9, 20)).ToList();
        expenses.Add(Spend(new DateOnly(2026, 10, 20).AddDays(-1), 12.99m, "Spotify", "subscriptions"));

        // The extra charge is a day earlier than the rhythm, so move it onto the schedule.
        expenses[^1] = expenses[^1] with { SpentOn = new DateOnly(2026, 10, 20) };
        var found = Assert.Single(SubscriptionDetector.Detect(expenses, new DateOnly(2026, 10, 21)));

        Assert.Equal(12.99m, found.Amount);
        Assert.Equal(10.99m, found.PreviousAmount);
    }

    [Fact]
    public void Groceries_varying_amounts_and_stopped_charges_are_not_subscriptions()
    {
        var groceries = Monthly("Albert Heijn", 40m, 6, new DateOnly(2026, 10, 1), "groceries");
        var variable = new[] { 30m, 80m, 12m, 55m }.Select((a, i) =>
            Spend(new DateOnly(2026, 10, 1).AddMonths(-i), a, "Energie BV", "bills"));
        var stopped = Monthly("Old Gym", 25m, 5, new DateOnly(2026, 2, 1), "health");

        Assert.Empty(SubscriptionDetector.Detect([.. groceries, .. variable, .. stopped], Today));
    }

    [Fact]
    public void Yearly_charges_need_only_two_occurrences_and_weekly_ones_are_recognised()
    {
        var yearly = new[] { Spend(new DateOnly(2025, 11, 2), 99m, "Amazon Prime", "subscriptions"),
            Spend(new DateOnly(2024, 11, 2), 99m, "Amazon Prime", "subscriptions") };
        var weekly = Enumerable.Range(0, 5).Select(i =>
            Spend(new DateOnly(2026, 10, 12).AddDays(-7 * i), 5m, "Kranten Abo", "subscriptions"));

        var found = SubscriptionDetector.Detect([.. yearly, .. weekly], Today);

        Assert.Contains(found, x => x.Cadence == SubscriptionCadences.Yearly);
        Assert.Contains(found, x => x.Cadence == SubscriptionCadences.Weekly);
    }

    [Theory]
    [InlineData("-12,50", -12.50)]
    [InlineData("(12.50)", -12.50)]
    [InlineData("€ 1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("12.50-", -12.50)]
    [InlineData("1500", 1500)]
    public void Bank_amounts_are_read_in_several_notations(string text, double expected) =>
        Assert.Equal((decimal)expected, BankCsvParser.ParseAmount(text));

    [Fact]
    public void A_dutch_semicolon_export_with_af_bij_is_read()
    {
        const string csv = """
            "Datum";"Naam / Omschrijving";"Rekening";"Af Bij";"Bedrag (EUR)"
            "20261003";"Albert Heijn 1234 Amsterdam";"NL01";"Af";"23,45"
            "20261002";"Salaris Werkgever";"NL01";"Bij";"2.500,00"
            "20261001";"NETFLIX.COM";"NL01";"Af";"13,99"
            "garbage";"x";"y";"Af";"1,00"
            """;

        var result = BankCsvParser.Parse(csv, null);

        Assert.Equal(2, result.Spending.Count);
        Assert.Equal(1, result.IncomeSkipped);
        Assert.Equal(1, result.Unreadable);
        Assert.Equal(23.45m, result.Spending[0].Amount);
        Assert.Equal("Albert Heijn", result.Spending[0].Merchant);
        Assert.Equal(new DateOnly(2026, 10, 3), result.Spending[0].Date);
    }

    [Fact]
    public void An_english_comma_export_with_signed_amounts_and_currency_is_read()
    {
        const string csv = "Date,Description,Amount,Currency\n2026-10-01,\"Coffee, Corner\",-3.50,USD\n2026-10-02,Refund,10.00,USD\n";

        var result = BankCsvParser.Parse(csv, null);

        var row = Assert.Single(result.Spending);
        Assert.Equal("USD", row.Currency);
        Assert.Equal(3.5m, row.Amount);
        Assert.Equal("Coffee", row.Merchant);
        Assert.Equal(1, result.IncomeSkipped);
    }

    [Fact]
    public void A_file_without_the_needed_columns_explains_what_is_missing()
    {
        var result = BankCsvParser.Parse("foo,bar\n1,2\n", null);

        Assert.Empty(result.Spending);
        Assert.Contains("date and amount", result.Problem);
    }

    [Fact]
    public void Budget_status_projects_the_month_and_flags_overruns()
    {
        var budget = new Budget(Guid.NewGuid(), Owner, "groceries", 300m, "EUR", null, 0, Now, Now);
        var month = new[] { Spend(new DateOnly(2026, 10, 3), 160m, "AH", "groceries"),
            Spend(new DateOnly(2026, 10, 9), 20m, "Jumbo", "groceries"), Spend(new DateOnly(2026, 10, 5), 500m, "Ikea", "shopping") };

        var status = FinanceService.StatusOf(budget, month, 2026, 10, Today);
        var over = FinanceService.StatusOf(budget with { Limit = 150m }, month, 2026, 10, Today);
        var ok = FinanceService.StatusOf(budget with { Limit = 900m }, month, 2026, 10, new DateOnly(2026, 10, 2));

        Assert.Equal(180m, status.Spent);
        Assert.Equal(60, status.Percent);
        Assert.Equal(372m, status.Projected);
        Assert.Equal(BudgetStates.ProjectedOver, status.State);
        Assert.Equal(BudgetStates.Over, over.State);
        Assert.Equal(BudgetStates.Ok, ok.State);
        Assert.Null(ok.Projected);
    }

    [Fact]
    public void Unusually_large_charges_are_flagged_against_the_shops_normal_amount()
    {
        var history = new List<Expense>();
        for (var i = 1; i <= 5; i++) history.Add(Spend(Today.AddDays(-40 - i * 7), 12m, "Bagels", "dining"));
        history.Add(Spend(Today.AddDays(-2), 95m, "Bagels", "dining"));
        history.Add(Spend(Today.AddDays(-3), 14m, "Bagels", "dining"));

        var alerts = FinanceService.FindOutliers(history, Today);

        var alert = Assert.Single(alerts);
        Assert.Equal(FinanceAlertKinds.Outlier, alert.Kind);
        Assert.Contains("Bagels", alert.Title);
    }

    [Fact]
    public void Exports_defuse_spreadsheet_formulas_and_quote_commas()
    {
        var csv = FinanceService.ToCsv([Spend(Today, 5m, "=HYPERLINK(\"x\")", "other") with { Note = "a, b" }]);

        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);
        Assert.Contains("\"a, b\"", csv);
        Assert.StartsWith("Date,Amount,Currency", csv);
    }

    [Fact]
    public async Task Setting_a_budget_validates_and_replaces_the_existing_one()
    {
        var (service, _, repository, _) = Create();

        var bad = await service.SetBudgetAsync(Owner, "rockets", 100m, null, default);
        var zero = await service.SetBudgetAsync(Owner, "groceries", 0m, null, default);
        var first = await service.SetBudgetAsync(Owner, "boodschappen", 400m, null, default);
        var again = await service.SetBudgetAsync(Owner, "groceries", 450m, "usd", default);
        var total = await service.SetBudgetAsync(Owner, "alles", 2000m, null, default);

        Assert.Equal("category", bad.Field);
        Assert.Equal("limit", zero.Field);
        Assert.Equal(first.Value!.Id, again.Value!.Id);
        Assert.Equal(450m, again.Value.Limit);
        Assert.Equal("USD", again.Value.Currency);
        Assert.Equal(BudgetCategories.Total, total.Value!.Category);
        Assert.Equal(2, repository.Budgets.Count);
    }

    [Fact]
    public async Task Import_previews_first_then_adds_spending_and_skips_repeats()
    {
        var (service, expenses, _, _) = Create();
        const string csv = "Date,Description,Amount\n2026-10-01,Netflix,-13.99\n2026-10-02,Bakker Jansen,-4.20\n2026-10-03,Salary,2500\n";

        var preview = await service.ImportAsync(Owner, csv, false, null, Today, default);
        Assert.Empty(expenses.Expenses);
        var first = await service.ImportAsync(Owner, csv, true, null, Today, default);
        var second = await service.ImportAsync(Owner, csv, true, null, Today, default);

        Assert.Equal((2, 1), (preview.Rows, preview.IncomeSkipped));
        Assert.Equal((2, 0), (first.Imported, first.Duplicates));
        Assert.Equal((0, 2), (second.Imported, second.Duplicates));
        Assert.All(expenses.Expenses, x => Assert.Equal(ExpenseSources.Import, x.Source));
        Assert.Equal(ExpenseCategories.Subscriptions, expenses.Expenses.Single(x => x.Merchant == "Netflix").Category);
    }

    [Fact]
    public async Task Import_refuses_oversized_files()
    {
        var (service, _, _, _) = Create();

        var report = await service.ImportAsync(Owner, new string('x', FinanceRules.MaxImportBytes + 1), true, null,
            Today, default);

        Assert.Equal(0, report.Imported);
        Assert.Contains("too large", report.Problem);
    }

    [Fact]
    public async Task Refreshing_finds_subscriptions_keeps_dismissals_and_marks_vanished_ones_cancelled()
    {
        var (service, expenses, repository, _) = Create();
        expenses.Expenses.AddRange(Monthly("Netflix", 13.99m, 4, new DateOnly(2026, 10, 3)));
        expenses.Expenses.AddRange(Monthly("Old Gym", 25m, 4, new DateOnly(2026, 10, 8), "health"));

        var first = await service.RefreshSubscriptionsAsync(Owner, Today, default);
        var gym = first.Single(x => x.MerchantKey == "old gym");
        await service.UpdateSubscriptionAsync(gym.Id, Owner, SubscriptionStatuses.Dismissed, null, false, Today, default);
        expenses.Expenses.RemoveAll(x => x.Merchant == "Netflix");
        var second = await service.RefreshSubscriptionsAsync(Owner, Today, default);

        Assert.Equal(2, first.Count);
        Assert.Equal(SubscriptionStatuses.Dismissed, second.Single(x => x.MerchantKey == "old gym").Status);
        Assert.Equal(SubscriptionStatuses.Cancelled, second.Single(x => x.MerchantKey == "netflix").Status);
        Assert.Equal(2, repository.Subscriptions.Count);
    }

    [Fact]
    public async Task A_reminder_is_scheduled_before_the_next_charge_and_dropped_when_dismissed()
    {
        var (service, expenses, _, reminders) = Create();
        expenses.Expenses.AddRange(Monthly("Netflix", 13.99m, 4, new DateOnly(2026, 10, 3)));
        var netflix = (await service.RefreshSubscriptionsAsync(Owner, Today, default)).Single();

        var updated = await service.UpdateSubscriptionAsync(netflix.Id, Owner, null, 3, false, Today, default);
        var dismissed = await service.UpdateSubscriptionAsync(netflix.Id, Owner, "dismissed", null, false, Today, default);
        var invalid = await service.UpdateSubscriptionAsync(netflix.Id, Owner, null, 99, false, Today, default);

        Assert.NotNull(updated.Value!.ReminderId);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero), reminders.Created.Single().DueAt);
        Assert.Null(dismissed.Value!.ReminderId);
        Assert.Single(reminders.Cancelled);
        Assert.Equal(FinanceFailure.Invalid, invalid.Failure);
    }

    [Fact]
    public async Task The_forecast_adds_recurring_charges_and_the_daily_pace()
    {
        var (service, expenses, _, _) = Create();
        expenses.Expenses.AddRange(Monthly("Netflix", 15m, 4, new DateOnly(2026, 10, 3)));
        expenses.Expenses.Add(Spend(new DateOnly(2026, 10, 10), 150m, "AH", "groceries"));
        expenses.Expenses.Add(Spend(new DateOnly(2026, 10, 12), 90m, "Gall", "groceries"));
        await service.RefreshSubscriptionsAsync(Owner, Today, default);
        // A weekly subscription due again before the month ends adds more than one charge.
        expenses.Expenses.AddRange(Enumerable.Range(0, 5).Select(i =>
            Spend(new DateOnly(2026, 10, 13).AddDays(-7 * i), 5m, "Kranten", "subscriptions")));
        await service.RefreshSubscriptionsAsync(Owner, Today, default);

        var forecast = await service.ForecastAsync(Owner, Today, default);

        Assert.Equal(16, forecast.DaysLeft);
        Assert.Equal(265m, forecast.SpentSoFar);
        Assert.Equal(10m, forecast.ExpectedRecurring);
        Assert.Equal(256m, forecast.ProjectedOther);
        Assert.Equal(531m, forecast.ProjectedTotal);
        Assert.Equal(2, forecast.Upcoming.Count);
        Assert.All(forecast.Upcoming, x => Assert.Equal("Kranten", x.Merchant));
    }

    [Fact]
    public async Task The_budget_observer_warns_once_at_80_and_again_at_100_percent()
    {
        var (_, expenses, repository, _) = Create();
        var notifications = new RecordingNotifications();
        var budget = new Budget(Guid.NewGuid(), Owner, "groceries", 100m, "EUR", null, 0, Now, Now);
        repository.Budgets.Add(budget);
        var observer = new BudgetExpenseObserver(repository, expenses, notifications.Repository,
            Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null)), new FixedClock());

        async Task Log(decimal amount, DateOnly? date = null)
        {
            var expense = Spend(date ?? Today, amount, "AH", "groceries");
            expenses.Expenses.Add(expense);
            await observer.OnExpenseCreatedAsync(expense, default);
        }

        await Log(50m);
        await Log(35m);
        await Log(2m);
        await Log(20m);
        await Log(5m);
        await Log(900m, new DateOnly(2026, 9, 1));

        Assert.Equal(["Groceries budget at 85%", "Groceries budget used up"], notifications.Titles);
    }

    [Fact]
    public async Task The_agent_tools_report_the_overview_and_guard_imports()
    {
        var (service, expenses, _, _) = Create();
        expenses.Expenses.Add(Spend(Today, 25m, "AH", "groceries"));
        await service.SetBudgetAsync(Owner, "groceries", 100m, null, default);
        var tools = new FinanceAgentTools(service, new FixedUser());

        var overview = await tools.GetFinanceOverviewAsync();
        var preview = await tools.ImportBankStatementAsync("Date,Description,Amount\n2026-10-01,Netflix,-13.99\n");
        var broken = await tools.ImportBankStatementAsync("nonsense");

        Assert.Contains("not instructions", overview);
        Assert.Contains("Groceries: €25.00 of €100.00", overview);
        Assert.Contains("Preview: 1 spending rows", preview);
        Assert.Contains("could not read", broken);
    }

    private static (FinanceService Service, FakeExpenses Expenses, FakeFinanceRepository Repository,
        RecordingReminders Reminders) Create()
    {
        var expenses = new FakeExpenses();
        var repository = new FakeFinanceRepository();
        var reminders = new RecordingReminders();
        var files = Fake<IFileService>.Create(("GetAsync", _ => null));
        var expenseService = new ExpenseService(expenses, files, new FixedClock());
        var service = new FinanceService(repository, expenseService, expenses, reminders.Service,
            Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null)), new FixedClock());
        return (service, expenses, repository, reminders);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedUser : Jarvis.Application.Conversations.ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class RecordingNotifications
    {
        public List<string> Titles { get; } = [];

        public INotificationRepository Repository => Fake<INotificationRepository>.Create(("CreateAsync", args =>
        {
            Titles.Add((string)args[2]!);
            return new NotificationRecord(Guid.NewGuid(), (string)args[1]!, (string)args[2]!, (string)args[3]!, null,
                Now, null);
        }));
    }

    private sealed class RecordingReminders
    {
        public List<CreateReminderRequest> Created { get; } = [];
        public List<Guid> Cancelled { get; } = [];
        private readonly Dictionary<Guid, ReminderRecord> store = [];

        public IReminderService Service => Fake<IReminderService>.Create(
            ("CreateAsync", args =>
            {
                var request = (CreateReminderRequest)args[1]!;
                Created.Add(request);
                var record = new ReminderRecord(Guid.NewGuid(), Owner, request.Title, request.DueAt, "wf", "pending", Now, null);
                store[record.Id] = record;
                return record;
            }),
            ("GetAsync", args => store.GetValueOrDefault((Guid)args[0]!)),
            ("CancelAsync", args =>
            {
                Cancelled.Add((Guid)args[0]!);
                return (ReminderRecord?)null;
            }));
    }

    private sealed class FakeFinanceRepository : IFinanceRepository
    {
        public List<Budget> Budgets { get; } = [];
        public List<Subscription> Subscriptions { get; } = [];

        public Task<IReadOnlyList<Budget>> ListBudgetsAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Budget>>(Budgets.Where(x => x.OwnerId == ownerId).ToArray());

        public Task<Budget?> FindBudgetAsync(Guid ownerId, string category, CancellationToken cancellationToken) =>
            Task.FromResult(Budgets.FirstOrDefault(x => x.OwnerId == ownerId && x.Category == category));

        public Task AddBudgetAsync(Budget budget, CancellationToken cancellationToken)
        {
            Budgets.Add(budget);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateBudgetAsync(Budget budget, CancellationToken cancellationToken)
        {
            var index = Budgets.FindIndex(x => x.Id == budget.Id);
            if (index < 0) return Task.FromResult(false);
            Budgets[index] = budget;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteBudgetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Budgets.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);

        public Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Subscription>>(Subscriptions.Where(x => x.OwnerId == ownerId).ToArray());

        public Task<Subscription?> GetSubscriptionAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Subscriptions.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task AddSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken)
        {
            Subscriptions.Add(subscription);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken)
        {
            var index = Subscriptions.FindIndex(x => x.Id == subscription.Id);
            if (index < 0) return Task.FromResult(false);
            Subscriptions[index] = subscription;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeExpenses : IExpenseRepository
    {
        public List<Expense> Expenses { get; } = [];

        public Task<IReadOnlyList<Expense>> ListAsync(Guid ownerId, DateOnly from, DateOnly to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Expense>>(Expenses
                .Where(x => x.OwnerId == ownerId && x.SpentOn >= from && x.SpentOn <= to)
                .OrderByDescending(x => x.SpentOn).ToArray());

        public Task<IReadOnlyList<Expense>> ListRecentAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Expense>>(Expenses.Where(x => x.OwnerId == ownerId).AsEnumerable().Reverse()
                .Take(limit).ToArray());

        public Task<Expense?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Expenses.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task AddAsync(Expense expense, CancellationToken cancellationToken)
        {
            Expenses.Add(expense);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(Expense expense, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Expenses.RemoveAll(x => x.Id == id) > 0);
    }
}
