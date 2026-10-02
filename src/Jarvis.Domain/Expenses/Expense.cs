namespace Jarvis.Domain.Expenses;

/// <summary>
/// Money the owner spent, logged from chat ("€12 lunch"), a receipt photo, or the app. <see cref="Amount"/> is
/// always positive and in <see cref="Currency"/> (ISO 4217). <see cref="SpentOn"/> is the owner's local date.
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
    DateTimeOffset UpdatedAt);
