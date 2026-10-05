using System.Text.Json;
using Jarvis.Agents.Expenses;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Expenses;
using Jarvis.Application.Files;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Expenses;
using Jarvis.Domain.Files;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ExpenseTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000bbbb");
    private static readonly Guid Photo = Guid.Parse("01996b8c-6000-7000-8000-00000000cccc");
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 2);

    [Theory]
    [InlineData("Albert Heijn", null, ExpenseCategories.Groceries)]
    [InlineData("AH to go", "broodje", ExpenseCategories.Groceries)]
    [InlineData(null, "lunch met Sanne", ExpenseCategories.Dining)]
    [InlineData("Shell", null, ExpenseCategories.Transport)]
    [InlineData("NS", "treinkaartje", ExpenseCategories.Transport)]
    [InlineData("Netflix", null, ExpenseCategories.Subscriptions)]
    [InlineData("Pathé", null, ExpenseCategories.Entertainment)]
    [InlineData("Barbershop", null, ExpenseCategories.Other)]
    [InlineData(null, null, ExpenseCategories.Other)]
    public void Category_is_guessed_from_the_shop_before_the_note(string? merchant, string? note, string expected) =>
        Assert.Equal(expected, ExpenseCategories.Guess(merchant, note));

    [Theory]
    [InlineData("Groceries", ExpenseCategories.Groceries)]
    [InlineData("boodschappen", ExpenseCategories.Groceries)]
    [InlineData("eten", ExpenseCategories.Dining)]
    [InlineData("vaste lasten", ExpenseCategories.Bills)]
    [InlineData("subscriptions", ExpenseCategories.Subscriptions)]
    [InlineData("rocket", null)]
    [InlineData("", null)]
    public void Categories_are_normalized(string input, string? expected) =>
        Assert.Equal(expected, ExpenseCategories.Normalize(input));

    [Theory]
    [InlineData("€", "EUR")]
    [InlineData("usd", "USD")]
    [InlineData(" gbp ", "GBP")]
    [InlineData("euro", null)]
    [InlineData("E1R", null)]
    public void Currencies_are_three_letter_codes(string input, string? expected) =>
        Assert.Equal(expected, ExpenseRules.NormalizeCurrency(input));

    [Fact]
    public async Task Creating_fills_in_today_the_last_currency_and_a_guessed_category()
    {
        var (service, repository) = Create();
        await service.CreateAsync(Owner, new ExpenseDraft(5m, "USD"), Today, false, default);

        var result = await service.CreateAsync(Owner, new ExpenseDraft(12.345m, Merchant: "  Albert   Heijn "), Today,
            false, default);

        var expense = result.Value!.Expense;
        Assert.Equal(12.35m, expense.Amount);
        Assert.Equal("USD", expense.Currency);
        Assert.Equal("Albert Heijn", expense.Merchant);
        Assert.Equal(ExpenseCategories.Groceries, expense.Category);
        Assert.Equal(Today, expense.SpentOn);
        Assert.Equal(2, repository.Expenses.Count);
    }

    [Theory]
    [InlineData(0, "amount")]
    [InlineData(-3, "amount")]
    [InlineData(2_000_000, "amount")]
    public async Task Amounts_must_be_positive_and_bounded(decimal amount, string field)
    {
        var (service, repository) = Create();

        var result = await service.CreateAsync(Owner, new ExpenseDraft(amount), Today, false, default);

        Assert.Equal(ExpenseFailure.Invalid, result.Failure);
        Assert.Equal(field, result.Field);
        Assert.Empty(repository.Expenses);
    }

    [Fact]
    public async Task Future_dates_unknown_categories_and_foreign_receipts_are_refused()
    {
        var (service, _) = Create();

        var future = await service.CreateAsync(Owner, new ExpenseDraft(1m, SpentOn: Today.AddDays(3)), Today, false,
            default);
        var category = await service.CreateAsync(Owner, new ExpenseDraft(1m, Category: "rocket"), Today, false,
            default);
        var receipt = await service.CreateAsync(Other, new ExpenseDraft(1m, ReceiptFileId: Photo), Today, false,
            default);

        Assert.Equal("spentOn", future.Field);
        Assert.Equal("category", category.Field);
        Assert.Equal("receiptFileId", receipt.Field);
    }

    [Fact]
    public async Task Duplicates_are_skipped_only_when_asked()
    {
        var (service, repository) = Create();
        var first = await service.CreateAsync(Owner, new ExpenseDraft(12m, "EUR", "Bagels & Beans"), Today, true,
            default);

        var again = await service.CreateAsync(Owner, new ExpenseDraft(12m, "EUR", "bagels & beans"), Today, true,
            default);
        var forced = await service.CreateAsync(Owner, new ExpenseDraft(12m, "EUR", "Bagels & Beans"), Today, false,
            default);

        Assert.True(again.Value!.IsDuplicate);
        Assert.Equal(first.Value!.Expense.Id, again.Value.Expense.Id);
        Assert.False(forced.Value!.IsDuplicate);
        Assert.Equal(2, repository.Expenses.Count);
    }

    [Fact]
    public async Task Updates_change_only_given_fields_and_empty_text_clears()
    {
        var (service, _) = Create();
        var created = (await service.CreateAsync(Owner,
            new ExpenseDraft(20m, "EUR", "Jumbo", Note: "weekend"), Today, false, default)).Value!.Expense;

        var updated = await service.UpdateAsync(created.Id, Owner,
            new ExpenseDraft(25m, Note: "", Category: "dining"), Today, default);
        var foreign = await service.UpdateAsync(created.Id, Other, new ExpenseDraft(1m), Today, default);

        Assert.Equal(25m, updated.Value!.Amount);
        Assert.Equal("Jumbo", updated.Value.Merchant);
        Assert.Null(updated.Value.Note);
        Assert.Equal(ExpenseCategories.Dining, updated.Value.Category);
        Assert.Equal(ExpenseFailure.NotFound, foreign.Failure);
    }

    [Fact]
    public void Month_summary_uses_the_main_currency_and_compares_with_last_month()
    {
        Expense Make(decimal amount, string category, string? merchant, int day, string currency = "EUR", int month = 10) =>
            new(Guid.NewGuid(), Owner, amount, currency, merchant, category, null, new DateOnly(2026, month, day),
                null, ExpenseSources.App, Now, Now);
        var october = new[]
        {
            Make(40m, ExpenseCategories.Groceries, "Albert Heijn", 1),
            Make(10m, ExpenseCategories.Groceries, "albert heijn", 2),
            Make(15m, ExpenseCategories.Dining, "Cafe", 2),
            Make(99m, ExpenseCategories.Travel, "Hotel", 2, "USD")
        };
        var september = new[] { Make(30m, ExpenseCategories.Dining, null, 20, month: 9) };

        var summary = ExpenseService.Summarize(2026, 10, october, september, "EUR");

        Assert.Equal("EUR", summary.Currency);
        Assert.Equal(65m, summary.Total);
        Assert.Equal(3, summary.Count);
        Assert.Equal(30m, summary.PreviousTotal);
        Assert.Equal([ExpenseCategories.Groceries, ExpenseCategories.Dining], summary.Categories.Select(x => x.Category));
        Assert.Equal(50m, summary.Categories[0].Total);
        Assert.Equal([40m, 25m], summary.Days.Select(x => x.Total));
        var top = summary.TopMerchants[0];
        Assert.Equal((50m, 2), (top.Total, top.Count));
        var usd = Assert.Single(summary.OtherCurrencies);
        Assert.Equal(("USD", 99m), (usd.Currency, usd.Total));
    }

    [Theory]
    [InlineData("""{"amount": 23.4, "currency": "eur", "merchant": "Jumbo", "date": "2026-10-01", "category": "boodschappen", "note": "weekboodschappen"}""", 23.40, "EUR", "Jumbo", ExpenseCategories.Groceries)]
    [InlineData("""Here you go: {"amount": "1.234,50", "currency": null, "merchant": null, "date": "bad", "category": "x", "note": null}""", 1234.50, null, null, null)]
    [InlineData("""{"amount": -5, "currency": "EURO", "merchant": "Shop", "date": null, "category": null, "note": null}""", null, null, "Shop", null)]
    public void Receipt_readings_are_parsed_defensively(string json, double? amount, string? currency,
        string? merchant, string? category)
    {
        var reading = ReceiptReader.Parse(json)!;

        Assert.Equal(amount is null ? null : (decimal)amount.Value, reading.Amount);
        Assert.Equal(currency, reading.Currency);
        Assert.Equal(merchant, reading.Merchant);
        Assert.Equal(category, reading.Category);
    }

    [Theory]
    [InlineData("12,50", 12.50)]
    [InlineData("€ 1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1.234", 1234)]
    public void Receipt_amounts_accept_both_decimal_styles(string text, double expected) =>
        Assert.Equal((decimal)expected, ReceiptReader.ParseAmount(text));

    [Fact]
    public async Task Logging_from_a_photo_keeps_the_photo_and_audits_without_amounts()
    {
        var (service, repository) = Create();
        var audit = new RecordingAudit();
        var conversationId = Guid.NewGuid();
        var photoMessage = new Message(conversationId, "user", "Shared a photo.",
            attachmentsJson: MessageAttachments.Serialize([new MessageAttachment(Photo, "bon.jpg", "image/jpeg")]));
        var tools = CreateTools(service, audit, conversationId,
            [photoMessage, new Message(conversationId, "assistant", "Nice receipt.")]);

        var reply = await tools.LogExpenseAsync(23.40m, "Jumbo", fromPhoto: true);
        var again = await tools.LogExpenseAsync(23.40m, "Jumbo", fromPhoto: true);

        var expense = Assert.Single(repository.Expenses);
        Assert.Equal(Photo, expense.ReceiptFileId);
        Assert.Equal(ExpenseSources.Receipt, expense.Source);
        Assert.Equal(ExpenseCategories.Groceries, expense.Category);
        Assert.Contains("Logged €23.40 at Jumbo", reply);
        Assert.Contains("already logged", again);
        Assert.Equal(["expense.logged"], audit.Actions);
        using var metadata = JsonDocument.Parse(audit.Metadata[0]);
        Assert.Equal(["resourceId", "source"],
            metadata.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(expense.Id, metadata.RootElement.GetProperty("resourceId").GetGuid());
        Assert.Equal("agent", metadata.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Logging_from_a_photo_without_one_asks_for_the_receipt_again()
    {
        var (service, repository) = Create();
        var tools = CreateTools(service, new RecordingAudit(), Guid.NewGuid(), []);

        var reply = await tools.LogExpenseAsync(10m, fromPhoto: true);

        Assert.Contains("could not find a photo", reply);
        Assert.Empty(repository.Expenses);
    }

    [Fact]
    public async Task Deleting_through_the_agent_requires_approval()
    {
        var (service, _) = Create();
        var contributor = new ExpenseToolContributor(service, Fake<IConversationStore>.Create(),
            Fake<IDailyBriefingRepository>.Create(), new RecordingAudit(), new FixedUser(),
            NullLoggerFactory.Instance);

        var tools = contributor.GetTools(new Jarvis.Agents.AgentBuildContext(Owner, null)).ToArray();

        Assert.Equal(["LogExpense", "GetExpenses", "UpdateExpense", "DeleteExpense"], tools.Select(x => x.Name));
        Assert.IsType<Microsoft.Extensions.AI.ApprovalRequiredAIFunction>(tools[^1]);
        Assert.All(tools[..^1], tool => Assert.IsNotType<Microsoft.Extensions.AI.ApprovalRequiredAIFunction>(tool));
    }

    private static ExpenseAgentTools CreateTools(IExpenseService service, IAuditEventStore audit,
        Guid conversationId, Message[] messages)
    {
        var conversations = Fake<IConversationStore>.Create(
            ("GetAsync", args => (Guid)args[1]! == Owner ? new Conversation(Owner, "Chat") : null),
            ("GetMessagePageAsync", _ => new MessagePage(messages, null, false)));
        var briefings = Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null));
        return new ExpenseAgentTools(service, conversations, briefings, audit, new FixedUser(), NullLogger.Instance,
            new FixedClock(Now), conversationId);
    }

    private static (ExpenseService Service, FakeExpenseRepository Repository) Create()
    {
        var repository = new FakeExpenseRepository();
        var files = Fake<IFileService>.Create(("GetAsync", args =>
            (Guid)args[0]! == Photo && (Guid)args[1]! == Owner
                ? new StoredFile(Photo, Owner, "key", "bon.jpg", "image/jpeg", 1000, "sha", Now, "ready")
                : null));
        return (new ExpenseService(repository, files, new FixedClock(Now)), repository);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingAudit : IAuditEventStore
    {
        public List<string> Actions { get; } = [];
        public List<string> Metadata { get; } = [];

        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass,
            bool success, Guid? approvalId, string? metadataJson, CancellationToken cancellationToken,
            Guid? agentRunId = null)
        {
            Actions.Add(action);
            Metadata.Add(metadataJson ?? "");
            return Task.FromResult(new AuditEventRecord(Guid.NewGuid(), agentRunId, tool, action, riskClass,
                approvalId, DateTimeOffset.UtcNow, success, metadataJson));
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeExpenseRepository : IExpenseRepository
    {
        public List<Expense> Expenses { get; } = [];

        public Task<IReadOnlyList<Expense>> ListAsync(Guid ownerId, DateOnly from, DateOnly to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Expense>>(Expenses
                .Where(x => x.OwnerId == ownerId && x.SpentOn >= from && x.SpentOn <= to)
                .OrderByDescending(x => x.SpentOn).ThenByDescending(x => x.CreatedAt).ToArray());

        public Task<IReadOnlyList<Expense>> ListRecentAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Expense>>(Expenses.Where(x => x.OwnerId == ownerId)
                .Reverse().Take(limit).ToArray());

        public Task<Expense?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Expenses.FirstOrDefault(x => x.Id == id && x.OwnerId == ownerId));

        public Task<IReadOnlyList<Expense>> QueryAsync(Guid ownerId, TransactionQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Expense>>(Expenses
                .Where(x => x.OwnerId == ownerId && (query.From is null || x.SpentOn >= query.From) &&
                            (query.To is null || x.SpentOn <= query.To) &&
                            (query.AccountId is null || x.AccountId == query.AccountId ||
                             x.TransferAccountId == query.AccountId) &&
                            (query.Kind is null || x.Kind == query.Kind))
                .OrderByDescending(x => x.SpentOn).Skip(query.Offset).Take(query.Limit).ToArray());

        public Task<IReadOnlyList<Expense>> ListAccountMovementsAsync(Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Expense>>(Expenses
                .Where(x => x.OwnerId == ownerId && (x.AccountId is not null || x.TransferAccountId is not null))
                .ToArray());

        public Task AddAsync(Expense expense, CancellationToken cancellationToken)
        {
            Expenses.Add(expense);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(Expense expense, CancellationToken cancellationToken)
        {
            var index = Expenses.FindIndex(x => x.Id == expense.Id && x.OwnerId == expense.OwnerId);
            if (index < 0) return Task.FromResult(false);
            Expenses[index] = expense;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Expenses.RemoveAll(x => x.Id == id && x.OwnerId == ownerId) > 0);
    }
}
