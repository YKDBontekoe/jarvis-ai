namespace Jarvis.Domain.Finance;

/// <summary>
/// A place the owner keeps money: a bank account, card, savings pot, cash, or brokerage. The balance is not stored;
/// it is <see cref="OpeningBalance"/> plus the transactions dated on or after <see cref="OpeningOn"/>.
/// </summary>
public sealed record FinancialAccount(
    Guid Id,
    Guid OwnerId,
    string Name,
    string Type,
    string Currency,
    string? Institution,
    string? Last4,
    decimal OpeningBalance,
    DateOnly OpeningOn,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A stock, ETF, fund, or other asset the owner holds, with its last known price.</summary>
public sealed record Holding(
    Guid Id,
    Guid OwnerId,
    Guid? AccountId,
    string Symbol,
    string Name,
    string AssetType,
    string Currency,
    decimal? LastPrice,
    DateTimeOffset? LastPriceAt,
    string? PriceSource,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One buy, sell, dividend, or fee on a holding. Quantity and Price are positive; Kind gives the direction.</summary>
public sealed record InvestmentTrade(
    Guid Id,
    Guid OwnerId,
    Guid HoldingId,
    string Kind,
    DateOnly TradedOn,
    decimal Quantity,
    decimal Price,
    decimal Fees,
    string? Note,
    DateTimeOffset CreatedAt);
