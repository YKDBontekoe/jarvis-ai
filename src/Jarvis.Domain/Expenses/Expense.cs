namespace Jarvis.Domain.Expenses;

/// <summary>
/// Money the owner spent, logged from chat ("€12 lunch"), a receipt photo, or the app. <see cref="Amount"/> is
/// always positive and in <see cref="Currency"/> (ISO 4217). <see cref="Kind"/> is expense, income or transfer; <see cref="AccountId"/> is the bank account it
/// belongs to, and <see cref="TransferAccountId"/> the account a transfer moves money into. <see cref="SpentOn"/> is the owner's local date.
/// </summary>
public sealed record Expense(
    Guid Id,
    Guid OwnerId,
    decimal Amount,
    string Currency,
    string? Merchant,
    string Category,
    string? Note,
    DateOnly SpentOn,
    Guid? ReceiptFileId,
    string Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Kind = "expense",
    Guid? AccountId = null,
    Guid? TransferAccountId = null);
