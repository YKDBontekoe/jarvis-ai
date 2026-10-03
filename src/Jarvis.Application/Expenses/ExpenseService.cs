using Jarvis.Application.Files;
using Jarvis.Domain.Expenses;

namespace Jarvis.Application.Expenses;

public sealed class ExpenseService(IExpenseRepository expenses, IFileService files, TimeProvider? timeProvider = null,
    IExpenseObserver? observer = null) : IExpenseService
{
    private const int TopMerchantCount = 5;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<Expense>> ListRecentAsync(Guid ownerId, int limit, CancellationToken cancellationToken) =>
        expenses.ListRecentAsync(ownerId, Math.Clamp(limit, 1, 200), cancellationToken);

    public async Task<IReadOnlyList<Expense>> ListMonthAsync(Guid ownerId, int year, int month, string? category,
        CancellationToken cancellationToken)
    {
        var (from, to) = MonthRange(year, month);
        var all = await expenses.ListAsync(ownerId, from, to, cancellationToken);
        return category is null ? all : all.Where(x => x.Category == category).ToArray();
    }

    public Task<Expense?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        expenses.GetAsync(id, ownerId, cancellationToken);

    public async Task<ExpenseOperation<ExpenseCreated>> CreateAsync(Guid ownerId, ExpenseDraft draft, DateOnly today,
        bool skipDuplicates, CancellationToken cancellationToken)
    {
        if (ExpenseRules.ValidateAmount(draft.Amount) is { } amountError)
            return ExpenseOperation<ExpenseCreated>.Invalid("amount", amountError);
        if (await ValidateAsync(ownerId, draft, today, cancellationToken) is { } invalid)
            return ExpenseOperation<ExpenseCreated>.Invalid(invalid.Field, invalid.Message);

        var merchant = ExpenseRules.Clean(draft.Merchant);
        var note = ExpenseRules.Clean(draft.Note);
        var currency = ExpenseRules.NormalizeCurrency(draft.Currency) ??
                       (await expenses.ListRecentAsync(ownerId, 1, cancellationToken)).FirstOrDefault()?.Currency ??
                       ExpenseRules.DefaultCurrency;
        var amount = ExpenseRules.Round(draft.Amount!.Value);
        var spentOn = draft.SpentOn ?? today;

        if (skipDuplicates)
        {
            var sameDay = await expenses.ListAsync(ownerId, spentOn, spentOn, cancellationToken);
            var duplicate = sameDay.FirstOrDefault(x =>
                x.Amount == amount && x.Currency == currency &&
                ExpenseRules.Key(x.Merchant) == ExpenseRules.Key(merchant) &&
                (draft.ReceiptFileId is null || x.ReceiptFileId is null || x.ReceiptFileId == draft.ReceiptFileId));
            if (duplicate is not null)
                return ExpenseOperation<ExpenseCreated>.Ok(new ExpenseCreated(duplicate, true));
        }

        var now = clock.GetUtcNow();
        var expense = new Expense(Guid.CreateVersion7(), ownerId, amount, currency, merchant,
            ExpenseCategories.Normalize(draft.Category) ?? ExpenseCategories.Guess(merchant, note), note, spentOn,
            draft.ReceiptFileId, ExpenseSources.IsValid(draft.Source) ? draft.Source : ExpenseSources.App, now, now);
        await expenses.AddAsync(expense, cancellationToken);
        await NotifyAsync(expense, cancellationToken);
        return ExpenseOperation<ExpenseCreated>.Ok(new ExpenseCreated(expense, false));
    }

    public async Task<ExpenseOperation<Expense>> UpdateAsync(Guid id, Guid ownerId, ExpenseDraft draft,
        DateOnly today, CancellationToken cancellationToken)
    {
        if (draft.Amount is not null && ExpenseRules.ValidateAmount(draft.Amount) is { } amountError)
            return ExpenseOperation<Expense>.Invalid("amount", amountError);
        var existing = await expenses.GetAsync(id, ownerId, cancellationToken);
        if (existing is null) return ExpenseOperation<Expense>.NotFound();
        if (draft.ReceiptFileId == existing.ReceiptFileId) draft = draft with { ReceiptFileId = null };
        if (await ValidateAsync(ownerId, draft, today, cancellationToken) is { } invalid)
            return ExpenseOperation<Expense>.Invalid(invalid.Field, invalid.Message);

        // An empty string clears an optional text field; null leaves it as it was.
        var updated = existing with
        {
            Amount = draft.Amount is { } amount ? ExpenseRules.Round(amount) : existing.Amount,
            Currency = ExpenseRules.NormalizeCurrency(draft.Currency) ?? existing.Currency,
            Merchant = draft.Merchant is null ? existing.Merchant : ExpenseRules.Clean(draft.Merchant),
            Note = draft.Note is null ? existing.Note : ExpenseRules.Clean(draft.Note),
            Category = ExpenseCategories.Normalize(draft.Category) ?? existing.Category,
            SpentOn = draft.SpentOn ?? existing.SpentOn,
            ReceiptFileId = draft.ReceiptFileId ?? existing.ReceiptFileId,
            UpdatedAt = clock.GetUtcNow()
        };
        return await expenses.UpdateAsync(updated, cancellationToken)
            ? ExpenseOperation<Expense>.Ok(updated)
            : ExpenseOperation<Expense>.NotFound();
    }

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        expenses.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<ExpenseMonthSummary> SummarizeMonthAsync(Guid ownerId, int year, int month,
        CancellationToken cancellationToken)
    {
        var (from, to) = MonthRange(year, month);
        var previousStart = from.AddMonths(-1);
        var both = await expenses.ListAsync(ownerId, previousStart, to, cancellationToken);
        var fallback = (await expenses.ListRecentAsync(ownerId, 1, cancellationToken)).FirstOrDefault()?.Currency;
        return Summarize(year, month, both.Where(x => x.SpentOn >= from).ToArray(),
            both.Where(x => x.SpentOn < from).ToArray(), fallback ?? ExpenseRules.DefaultCurrency);
    }

    /// <summary>Builds a month summary from that month's and the previous month's expenses.</summary>
    public static ExpenseMonthSummary Summarize(int year, int month, IReadOnlyList<Expense> thisMonth,
        IReadOnlyList<Expense> previousMonth, string fallbackCurrency)
    {
        var currency = thisMonth.GroupBy(x => x.Currency)
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Sum(x => x.Amount))
            .Select(g => g.Key).FirstOrDefault() ?? fallbackCurrency;
        var main = thisMonth.Where(x => x.Currency == currency).ToArray();

        var categories = main.GroupBy(x => x.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(x => x.Amount), g.Count()))
            .OrderByDescending(x => x.Total).ThenBy(x => x.Category, StringComparer.Ordinal)
            .ToArray();
        var days = main.GroupBy(x => x.SpentOn)
            .Select(g => new DayTotal(g.Key, g.Sum(x => x.Amount)))
            .OrderBy(x => x.Date)
            .ToArray();
        var merchants = main.Where(x => x.Merchant is not null)
            .GroupBy(x => ExpenseRules.Key(x.Merchant))
            .Select(g => new MerchantTotal(g.OrderByDescending(x => x.CreatedAt).First().Merchant!,
                g.Sum(x => x.Amount), g.Count()))
            .OrderByDescending(x => x.Total).ThenByDescending(x => x.Count)
            .Take(TopMerchantCount)
            .ToArray();
        var others = thisMonth.Where(x => x.Currency != currency).GroupBy(x => x.Currency)
            .Select(g => new CurrencyTotal(g.Key, g.Sum(x => x.Amount), g.Count()))
            .OrderByDescending(x => x.Count)
            .ToArray();

        return new ExpenseMonthSummary(year, month, currency, main.Sum(x => x.Amount), main.Length,
            previousMonth.Where(x => x.Currency == currency).Sum(x => x.Amount), categories, days, merchants, others);
    }

    public static (DateOnly From, DateOnly To) MonthRange(int year, int month)
    {
        var from = new DateOnly(Math.Clamp(year, 2000, 2100), Math.Clamp(month, 1, 12), 1);
        return (from, from.AddMonths(1).AddDays(-1));
    }

    // Budget warnings are a bonus: a failing observer must never lose the expense that was just saved.
    private async Task NotifyAsync(Expense expense, CancellationToken cancellationToken)
    {
        if (observer is null) return;
        try
        {
            await observer.OnExpenseCreatedAsync(expense, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private async Task<(string Field, string Message)?> ValidateAsync(Guid ownerId, ExpenseDraft draft,
        DateOnly today, CancellationToken cancellationToken)
    {
        if (draft.Currency is not null && ExpenseRules.NormalizeCurrency(draft.Currency) is null)
            return ("currency", "Use a three-letter currency code such as EUR.");
        if (ExpenseRules.Clean(draft.Merchant) is { Length: > ExpenseRules.MaxMerchantLength })
            return ("merchant", $"The shop name can contain at most {ExpenseRules.MaxMerchantLength} characters.");
        if (ExpenseRules.Clean(draft.Note) is { Length: > ExpenseRules.MaxNoteLength })
            return ("note", $"The note can contain at most {ExpenseRules.MaxNoteLength} characters.");
        if (draft.Category is not null && ExpenseRules.Clean(draft.Category) is not null &&
            ExpenseCategories.Normalize(draft.Category) is null)
            return ("category", "Unknown category.");
        if (draft.SpentOn is { } date)
        {
            if (date > today.AddDays(1)) return ("spentOn", "The date cannot be in the future.");
            if (date < today.AddYears(-ExpenseRules.MaxYearsBack))
                return ("spentOn", $"The date can be at most {ExpenseRules.MaxYearsBack} years ago.");
        }
        if (draft.ReceiptFileId is { } fileId &&
            await files.GetAsync(fileId, ownerId, cancellationToken) is null)
            return ("receiptFileId", "The receipt photo could not be found.");
        return null;
    }
}
