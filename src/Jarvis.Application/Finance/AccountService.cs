using Jarvis.Application.Expenses;
using Jarvis.Domain.Expenses;
using Jarvis.Domain.Finance;

namespace Jarvis.Application.Finance;

public sealed class AccountService(IAccountRepository accounts, IExpenseRepository expenses,
    IExpenseService expenseService, TimeProvider? timeProvider = null) : IAccountService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<AccountSummary>> ListAsync(Guid ownerId, DateOnly today, bool includeArchived,
        CancellationToken cancellationToken)
    {
        var list = await accounts.ListAsync(ownerId, cancellationToken);
        var movements = await expenses.ListAccountMovementsAsync(ownerId, cancellationToken);
        return list.Where(x => includeArchived || !x.Archived)
            .Select(x => Summarize(x, movements, today))
            .OrderBy(x => x.Account.Archived).ThenBy(x => x.Account.CreatedAt).ToArray();
    }

    public async Task<AccountSummary?> GetAsync(Guid id, Guid ownerId, DateOnly today,
        CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(id, ownerId, cancellationToken);
        if (account is null) return null;
        return Summarize(account, await expenses.ListAccountMovementsAsync(ownerId, cancellationToken), today);
    }

    public static AccountSummary Summarize(FinancialAccount account, IReadOnlyList<Expense> movements, DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var mine = movements.Where(x => x.AccountId == account.Id || x.TransferAccountId == account.Id).ToArray();
        var thisMonth = mine.Where(x => x.SpentOn >= monthStart && x.SpentOn <= today)
            .Select(x => AccountBalances.Delta(account.Id, x)).ToArray();
        return new AccountSummary(account, AccountBalances.Balance(account, mine, today),
            thisMonth.Where(x => x > 0).Sum(), -thisMonth.Where(x => x < 0).Sum(), mine.Length);
    }

    public async Task<FinanceOperation<FinancialAccount>> CreateAsync(Guid ownerId, AccountDraft draft,
        DateOnly today, CancellationToken cancellationToken)
    {
        if (Validate(draft, creating: true) is { } invalid) return invalid;
        if ((await accounts.ListAsync(ownerId, cancellationToken)).Count >= AccountRules.MaxAccounts)
            return FinanceOperation<FinancialAccount>.Invalid("name",
                $"You can have at most {AccountRules.MaxAccounts} accounts.");

        var now = clock.GetUtcNow();
        var account = new FinancialAccount(Guid.CreateVersion7(), ownerId, ExpenseRules.Clean(draft.Name)!,
            draft.Type ?? AccountTypes.Checking, ExpenseRules.NormalizeCurrency(draft.Currency) ??
                                                 ExpenseRules.DefaultCurrency,
            ExpenseRules.Clean(draft.Institution), Last4(draft.Last4),
            ExpenseRules.Round(draft.OpeningBalance ?? 0m), draft.OpeningOn ?? today, false, now, now);
        await accounts.AddAsync(account, cancellationToken);
        return FinanceOperation<FinancialAccount>.Ok(account);
    }

    public async Task<FinanceOperation<FinancialAccount>> UpdateAsync(Guid id, Guid ownerId, AccountDraft draft,
        CancellationToken cancellationToken)
    {
        var existing = await accounts.GetAsync(id, ownerId, cancellationToken);
        if (existing is null) return FinanceOperation<FinancialAccount>.NotFound();
        if (Validate(draft, creating: false) is { } invalid) return invalid;

        var updated = existing with
        {
            Name = draft.Name is null ? existing.Name : ExpenseRules.Clean(draft.Name)!,
            Type = draft.Type ?? existing.Type,
            Currency = ExpenseRules.NormalizeCurrency(draft.Currency) ?? existing.Currency,
            Institution = draft.Institution is null ? existing.Institution : ExpenseRules.Clean(draft.Institution),
            Last4 = draft.Last4 is null ? existing.Last4 : Last4(draft.Last4),
            OpeningBalance = draft.OpeningBalance is { } opening ? ExpenseRules.Round(opening) : existing.OpeningBalance,
            OpeningOn = draft.OpeningOn ?? existing.OpeningOn,
            Archived = draft.Archived ?? existing.Archived,
            UpdatedAt = clock.GetUtcNow()
        };
        return await accounts.UpdateAsync(updated, cancellationToken)
            ? FinanceOperation<FinancialAccount>.Ok(updated)
            : FinanceOperation<FinancialAccount>.NotFound();
    }

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        accounts.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<FinanceOperation<ReconcileResult>> ReconcileAsync(Guid id, Guid ownerId,
        decimal? actualBalance, DateOnly today, CancellationToken cancellationToken)
    {
        if (actualBalance is not { } actual || Math.Abs(actual) > AccountRules.MaxBalance)
            return FinanceOperation<ReconcileResult>.Invalid("balance", "Enter the balance your bank shows.");
        var account = await accounts.GetAsync(id, ownerId, cancellationToken);
        if (account is null) return FinanceOperation<ReconcileResult>.NotFound();

        var movements = await expenses.ListAccountMovementsAsync(ownerId, cancellationToken);
        var current = AccountBalances.Balance(account, movements, today);
        var adjustment = ExpenseRules.Round(actual) - current;
        var updated = account with
        {
            OpeningBalance = account.OpeningBalance + adjustment, UpdatedAt = clock.GetUtcNow()
        };
        await accounts.UpdateAsync(updated, cancellationToken);
        return FinanceOperation<ReconcileResult>.Ok(new ReconcileResult(updated, ExpenseRules.Round(actual),
            adjustment));
    }

    public async Task<FinanceOperation<ImportReport>> ImportAsync(Guid id, Guid ownerId, string? csv, bool commit,
        DateOnly today, CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(id, ownerId, cancellationToken);
        if (account is null) return FinanceOperation<ImportReport>.NotFound();
        if (System.Text.Encoding.UTF8.GetByteCount(csv ?? string.Empty) > FinanceRules.MaxImportBytes)
            return FinanceOperation<ImportReport>.Ok(new ImportReport(false, 0, 0, 0, 0, 0,
                "The file is too large. Import at most 1 MB at a time.", []));

        var parsed = BankCsvParser.Parse(csv, account.Currency);
        var all = parsed.Spending.Concat(parsed.Income ?? []).OrderBy(x => x.Date).ToArray();
        if (parsed.Problem is not null && all.Length == 0)
            return FinanceOperation<ImportReport>.Ok(new ImportReport(false, 0, 0, 0, 0, parsed.Unreadable,
                parsed.Problem, []));

        var rows = all.Take(FinanceRules.MaxImportRows).ToArray();
        var problem = all.Length > rows.Length
            ? $"Only the first {FinanceRules.MaxImportRows} rows are imported. Import the rest in another file."
            : null;
        if (!commit)
            return FinanceOperation<ImportReport>.Ok(new ImportReport(false, rows.Length, 0, 0, 0, parsed.Unreadable,
                problem, rows.Take(FinanceRules.PreviewRows).ToArray()));

        int imported = 0, duplicates = 0, unreadable = parsed.Unreadable;
        foreach (var row in rows)
        {
            var result = await expenseService.CreateAsync(ownerId, new ExpenseDraft(row.Amount, row.Currency,
                row.Merchant, null, row.Note, row.Date, null, ExpenseSources.Import, row.Kind, id), today, true,
                cancellationToken);
            if (!result.Succeeded) unreadable++;
            else if (result.Value!.IsDuplicate) duplicates++;
            else imported++;
        }
        return FinanceOperation<ImportReport>.Ok(new ImportReport(true, rows.Length, imported, duplicates, 0,
            unreadable, problem, rows.Take(FinanceRules.PreviewRows).ToArray()));
    }

    private static FinanceOperation<FinancialAccount>? Validate(AccountDraft draft, bool creating)
    {
        if (creating && ExpenseRules.Clean(draft.Name) is null)
            return FinanceOperation<FinancialAccount>.Invalid("name", "Give the account a name.");
        if (draft.Name is not null && ExpenseRules.Clean(draft.Name) is { } name &&
            name.Length > AccountRules.MaxNameLength)
            return FinanceOperation<FinancialAccount>.Invalid("name",
                $"The name can contain at most {AccountRules.MaxNameLength} characters.");
        if (draft.Name is not null && ExpenseRules.Clean(draft.Name) is null)
            return FinanceOperation<FinancialAccount>.Invalid("name", "Give the account a name.");
        if (draft.Type is not null && !AccountTypes.IsValid(draft.Type))
            return FinanceOperation<FinancialAccount>.Invalid("type", "Unknown account type.");
        if (draft.Currency is not null && ExpenseRules.NormalizeCurrency(draft.Currency) is null)
            return FinanceOperation<FinancialAccount>.Invalid("currency", "Use a three-letter currency code such as EUR.");
        if (draft.OpeningBalance is { } balance && Math.Abs(balance) > AccountRules.MaxBalance)
            return FinanceOperation<FinancialAccount>.Invalid("openingBalance", "That balance is too large.");
        return null;
    }

    /// <summary>Keeps only the last four digits, so a full account number is never stored.</summary>
    private static string? Last4(string? value)
    {
        var digits = new string((value ?? "").Where(char.IsLetterOrDigit).ToArray());
        return digits.Length == 0 ? null : digits[^Math.Min(4, digits.Length)..].ToUpperInvariant();
    }
}
