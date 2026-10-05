using Jarvis.Application.Expenses;
using Jarvis.Domain.Expenses;
using Jarvis.Domain.Finance;

namespace Jarvis.Application.Finance;

public static class AccountTypes
{
    public const string Checking = "checking";
    public const string Savings = "savings";
    public const string CreditCard = "credit_card";
    public const string Cash = "cash";
    public const string Brokerage = "brokerage";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [Checking, Savings, CreditCard, Cash, Brokerage, Other];

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? type) =>
        type is not null && All.Contains(type);
}

public static class AccountRules
{
    public const int MaxAccounts = 30;
    public const int MaxNameLength = 60;
    public const decimal MaxBalance = 1_000_000_000m;
}

/// <summary>What a caller wants an account to be. Null fields keep their value on update.</summary>
public sealed record AccountDraft(
    string? Name,
    string? Type = null,
    string? Currency = null,
    string? Institution = null,
    string? Last4 = null,
    decimal? OpeningBalance = null,
    DateOnly? OpeningOn = null,
    bool? Archived = null);

/// <summary>An account with its computed balance and this month's money in and out.</summary>
public sealed record AccountSummary(
    FinancialAccount Account,
    decimal Balance,
    decimal MonthIn,
    decimal MonthOut,
    int TransactionCount);

public sealed record ReconcileResult(FinancialAccount Account, decimal Balance, decimal Adjustment);

public interface IAccountRepository
{
    Task<IReadOnlyList<FinancialAccount>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<FinancialAccount?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddAsync(FinancialAccount account, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(FinancialAccount account, CancellationToken cancellationToken);

    /// <summary>Deletes the account; its transactions stay but lose the link.</summary>
    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public interface IAccountService
{
    Task<IReadOnlyList<AccountSummary>> ListAsync(Guid ownerId, DateOnly today, bool includeArchived,
        CancellationToken cancellationToken);

    Task<AccountSummary?> GetAsync(Guid id, Guid ownerId, DateOnly today, CancellationToken cancellationToken);
    Task<FinanceOperation<FinancialAccount>> CreateAsync(Guid ownerId, AccountDraft draft, DateOnly today,
        CancellationToken cancellationToken);

    Task<FinanceOperation<FinancialAccount>> UpdateAsync(Guid id, Guid ownerId, AccountDraft draft,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a statement for one account. Unlike the spending-only import, incoming payments are kept as income.
    /// With <paramref name="commit"/> false nothing is saved and the report only previews.
    /// </summary>
    Task<FinanceOperation<ImportReport>> ImportAsync(Guid id, Guid ownerId, string? csv, bool commit, DateOnly today,
        CancellationToken cancellationToken);

    /// <summary>Moves the opening balance so the account shows <paramref name="actualBalance"/> right now.</summary>
    Task<FinanceOperation<ReconcileResult>> ReconcileAsync(Guid id, Guid ownerId, decimal? actualBalance,
        DateOnly today, CancellationToken cancellationToken);
}

public static class AccountBalances
{
    /// <summary>
    /// Opening balance plus income, minus spending and outgoing transfers, plus incoming transfers, counting only
    /// transactions on or after the opening date and not after <paramref name="through"/>.
    /// </summary>
    public static decimal Balance(FinancialAccount account, IEnumerable<Expense> movements, DateOnly through)
    {
        var balance = account.OpeningBalance;
        foreach (var x in movements)
        {
            if (x.SpentOn < account.OpeningOn || x.SpentOn > through) continue;
            balance += Delta(account.Id, x);
        }
        return balance;
    }

    /// <summary>How a transaction changes one account's balance (zero when it does not touch it).</summary>
    public static decimal Delta(Guid accountId, Expense x)
    {
        var amount = x.Amount;
        if (x.AccountId == accountId)
            return x.Kind == TransactionKinds.Income ? amount : -amount;
        return x.Kind == TransactionKinds.Transfer && x.TransferAccountId == accountId ? amount : 0m;
    }
}
