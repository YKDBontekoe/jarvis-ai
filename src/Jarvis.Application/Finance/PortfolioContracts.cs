using Jarvis.Application.Expenses;
using Jarvis.Domain.Finance;

namespace Jarvis.Application.Finance;

public static class AssetTypes
{
    public const string Stock = "stock";
    public const string Etf = "etf";
    public const string Fund = "fund";
    public const string Crypto = "crypto";
    public const string Bond = "bond";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [Stock, Etf, Fund, Crypto, Bond, Other];

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? type) =>
        type is not null && All.Contains(type);
}

public static class TradeKinds
{
    public const string Buy = "buy";
    public const string Sell = "sell";

    /// <summary>Cash received; the amount is Quantity × Price (shares × dividend per share).</summary>
    public const string Dividend = "dividend";

    /// <summary>A cost not tied to a buy or sell (custody fee); the amount is Quantity × Price.</summary>
    public const string Fee = "fee";

    /// <summary>A stock split; Quantity is the ratio (2 for a 2-for-1 split) and Price is ignored.</summary>
    public const string Split = "split";

    public static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? kind) =>
        kind is Buy or Sell or Dividend or Fee or Split;
}

public static class PortfolioRules
{
    public const int MaxHoldings = 200;
    public const int MaxSymbolLength = 20;
    public const int MaxNameLength = 80;
    public const decimal MaxQuantity = 1_000_000_000m;
    public const decimal MaxPrice = 100_000_000m;

    /// <summary>A quote fetched less than this long ago is reused instead of asking the provider again.</summary>
    public static readonly TimeSpan QuoteFreshness = TimeSpan.FromMinutes(15);

    public static string? CleanSymbol(string? symbol)
    {
        var clean = new string((symbol ?? "").Trim().ToUpperInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or ':' or '^' or '=').ToArray());
        return clean.Length is > 0 and <= MaxSymbolLength ? clean : null;
    }
}

/// <summary>What a trade list adds up to for one holding, using the average-cost method.</summary>
public sealed record Position(
    decimal Quantity,
    decimal CostBasis,
    decimal AverageCost,
    decimal RealizedProfit,
    decimal Dividends,
    decimal Fees);

public static class PortfolioMath
{
    public static Position Calculate(IEnumerable<InvestmentTrade> trades)
    {
        decimal quantity = 0, cost = 0, realized = 0, dividends = 0, fees = 0;
        foreach (var t in trades.OrderBy(x => x.TradedOn).ThenBy(x => x.CreatedAt))
        {
            switch (t.Kind)
            {
                case TradeKinds.Buy:
                    quantity += t.Quantity;
                    cost += t.Quantity * t.Price + t.Fees;
                    fees += t.Fees;
                    break;
                case TradeKinds.Sell:
                {
                    var sold = Math.Min(t.Quantity, quantity);
                    var average = quantity > 0 ? cost / quantity : 0m;
                    realized += t.Quantity * t.Price - t.Fees - average * sold;
                    cost -= average * sold;
                    quantity -= sold;
                    fees += t.Fees;
                    break;
                }
                case TradeKinds.Dividend:
                    dividends += t.Quantity * t.Price - t.Fees;
                    break;
                case TradeKinds.Fee:
                    realized -= t.Quantity * t.Price;
                    fees += t.Quantity * t.Price;
                    break;
                case TradeKinds.Split when t.Quantity > 0:
                    quantity *= t.Quantity;
                    break;
            }
        }
        if (quantity <= 0) { quantity = 0; cost = 0; }
        return new Position(quantity, cost, quantity > 0 ? cost / quantity : 0m, realized, dividends, fees);
    }
}

public sealed record HoldingDraft(
    string? Symbol,
    string? Name = null,
    string? AssetType = null,
    string? Currency = null,
    Guid? AccountId = null);

public sealed record TradeDraft(
    string? Kind,
    DateOnly? TradedOn,
    decimal? Quantity,
    decimal? Price = null,
    decimal? Fees = null,
    string? Note = null);

/// <summary>A holding with its position and what it is worth at the last known price.</summary>
public sealed record HoldingSummary(
    Holding Holding,
    Position Position,
    decimal? MarketValue,
    decimal? UnrealizedProfit,
    decimal? UnrealizedPercent,
    bool PriceIsStale);

public sealed record AllocationSlice(string Label, decimal Value, decimal Percent);

public sealed record CurrencyValue(string Currency, decimal Value, decimal Cost, decimal Profit);

public sealed record PortfolioSummary(
    string Currency,
    decimal TotalValue,
    decimal TotalCost,
    decimal UnrealizedProfit,
    decimal RealizedProfit,
    decimal Dividends,
    IReadOnlyList<HoldingSummary> Holdings,
    IReadOnlyList<AllocationSlice> Allocation,
    IReadOnlyList<CurrencyValue> OtherCurrencies,
    bool QuotesConfigured);

public sealed record PricePoint(DateOnly Date, decimal Price);

public sealed record Quote(string Symbol, decimal Price, DateTimeOffset AsOf);

public interface IQuoteProvider
{
    bool IsConfigured { get; }

    /// <summary>Latest prices for the symbols; symbols the provider does not know are left out.</summary>
    Task<IReadOnlyDictionary<string, Quote>> GetQuotesAsync(IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken);
}

public sealed class NullQuoteProvider : IQuoteProvider
{
    public bool IsConfigured => false;

    public Task<IReadOnlyDictionary<string, Quote>> GetQuotesAsync(IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, Quote>>(new Dictionary<string, Quote>());
}

public interface IPortfolioRepository
{
    Task<IReadOnlyList<Holding>> ListHoldingsAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Holding?> GetHoldingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddHoldingAsync(Holding holding, CancellationToken cancellationToken);
    Task<bool> UpdateHoldingAsync(Holding holding, CancellationToken cancellationToken);
    Task<bool> DeleteHoldingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvestmentTrade>> ListTradesAsync(Guid ownerId, Guid? holdingId,
        CancellationToken cancellationToken);

    Task<InvestmentTrade?> GetTradeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task AddTradeAsync(InvestmentTrade trade, CancellationToken cancellationToken);
    Task<bool> DeleteTradeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task SavePricePointAsync(Guid holdingId, DateOnly date, decimal price, CancellationToken cancellationToken);
    Task<IReadOnlyList<PricePoint>> ListPriceHistoryAsync(Guid holdingId, Guid ownerId, int days,
        CancellationToken cancellationToken);
}

public interface IPortfolioService
{
    Task<PortfolioSummary> SummaryAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken);
    Task<HoldingSummary?> GetHoldingAsync(Guid id, Guid ownerId, DateOnly today, CancellationToken cancellationToken);
    Task<FinanceOperation<Holding>> AddHoldingAsync(Guid ownerId, HoldingDraft draft,
        CancellationToken cancellationToken);
    Task<FinanceOperation<Holding>> UpdateHoldingAsync(Guid id, Guid ownerId, HoldingDraft draft,
        CancellationToken cancellationToken);
    Task<bool> DeleteHoldingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvestmentTrade>> ListTradesAsync(Guid ownerId, Guid holdingId,
        CancellationToken cancellationToken);
    Task<FinanceOperation<InvestmentTrade>> AddTradeAsync(Guid holdingId, Guid ownerId, TradeDraft draft,
        DateOnly today, CancellationToken cancellationToken);
    Task<bool> DeleteTradeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);

    Task<FinanceOperation<Holding>> SetPriceAsync(Guid holdingId, Guid ownerId, decimal? price, DateOnly today,
        CancellationToken cancellationToken);

    /// <summary>Fetches fresh quotes when a provider is configured; returns how many prices changed.</summary>
    Task<int> RefreshPricesAsync(Guid ownerId, DateOnly today, bool force, CancellationToken cancellationToken);

    Task<IReadOnlyList<PricePoint>> HistoryAsync(Guid holdingId, Guid ownerId, int days,
        CancellationToken cancellationToken);
}
