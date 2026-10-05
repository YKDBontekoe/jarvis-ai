using Jarvis.Application.Expenses;
using Jarvis.Domain.Finance;

namespace Jarvis.Application.Finance;

public sealed class PortfolioService(IPortfolioRepository repository, IAccountRepository accounts,
    IQuoteProvider quotes, TimeProvider? timeProvider = null) : IPortfolioService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    // ---- Summary ----

    public async Task<PortfolioSummary> SummaryAsync(Guid ownerId, DateOnly today, CancellationToken cancellationToken)
    {
        var holdings = await repository.ListHoldingsAsync(ownerId, cancellationToken);
        var trades = (await repository.ListTradesAsync(ownerId, null, cancellationToken))
            .GroupBy(x => x.HoldingId).ToDictionary(g => g.Key, g => g.ToArray());
        var items = holdings.Select(h => Summarize(h, trades.GetValueOrDefault(h.Id) ?? [], clock.GetUtcNow()))
            .ToArray();
        return Build(items, quotes.IsConfigured);
    }

    public static PortfolioSummary Build(IReadOnlyList<HoldingSummary> items, bool quotesConfigured)
    {
        var open = items.Where(x => x.Position.Quantity > 0).ToArray();
        var currency = open.GroupBy(x => x.Holding.Currency)
            .OrderByDescending(g => g.Sum(x => x.MarketValue ?? x.Position.CostBasis)).Select(g => g.Key)
            .FirstOrDefault() ?? items.Select(x => x.Holding.Currency).FirstOrDefault() ?? ExpenseRules.DefaultCurrency;
        var main = open.Where(x => x.Holding.Currency == currency).ToArray();
        var value = main.Sum(x => x.MarketValue ?? x.Position.CostBasis);
        var cost = main.Sum(x => x.Position.CostBasis);

        var allocation = main.GroupBy(x => x.Holding.AssetType)
            .Select(g => new { Label = g.Key, Value = g.Sum(x => x.MarketValue ?? x.Position.CostBasis) })
            .OrderByDescending(x => x.Value)
            .Select(x => new AllocationSlice(x.Label, Round(x.Value), value > 0 ? Math.Round(x.Value / value * 100, 1) : 0))
            .ToArray();
        var others = open.Where(x => x.Holding.Currency != currency).GroupBy(x => x.Holding.Currency)
            .Select(g => new CurrencyValue(g.Key, Round(g.Sum(x => x.MarketValue ?? x.Position.CostBasis)),
                Round(g.Sum(x => x.Position.CostBasis)),
                Round(g.Sum(x => (x.MarketValue ?? x.Position.CostBasis) - x.Position.CostBasis))))
            .ToArray();

        return new PortfolioSummary(currency, Round(value), Round(cost), Round(value - cost),
            Round(items.Where(x => x.Holding.Currency == currency).Sum(x => x.Position.RealizedProfit)),
            Round(items.Where(x => x.Holding.Currency == currency).Sum(x => x.Position.Dividends)),
            items.OrderByDescending(x => x.MarketValue ?? x.Position.CostBasis).ToArray(), allocation, others,
            quotesConfigured);
    }

    public static HoldingSummary Summarize(Holding holding, IReadOnlyList<InvestmentTrade> trades, DateTimeOffset now)
    {
        var position = PortfolioMath.Calculate(trades);
        decimal? value = holding.LastPrice is { } price ? Round(position.Quantity * price) : null;
        decimal? profit = value is { } v && position.Quantity > 0 ? Round(v - position.CostBasis) : null;
        decimal? percent = profit is { } p && position.CostBasis > 0
            ? Math.Round(p / position.CostBasis * 100, 2)
            : null;
        var stale = position.Quantity > 0 && (holding.LastPriceAt is null || now - holding.LastPriceAt > TimeSpan.FromDays(3));
        return new HoldingSummary(holding, position with
        {
            CostBasis = Round(position.CostBasis), AverageCost = Math.Round(position.AverageCost, 4),
            RealizedProfit = Round(position.RealizedProfit), Dividends = Round(position.Dividends),
            Fees = Round(position.Fees)
        }, value, profit, percent, stale);
    }

    public async Task<HoldingSummary?> GetHoldingAsync(Guid id, Guid ownerId, DateOnly today,
        CancellationToken cancellationToken)
    {
        var holding = await repository.GetHoldingAsync(id, ownerId, cancellationToken);
        if (holding is null) return null;
        return Summarize(holding, await repository.ListTradesAsync(ownerId, id, cancellationToken), clock.GetUtcNow());
    }

    // ---- Holdings ----

    public async Task<FinanceOperation<Holding>> AddHoldingAsync(Guid ownerId, HoldingDraft draft,
        CancellationToken cancellationToken)
    {
        var symbol = PortfolioRules.CleanSymbol(draft.Symbol);
        if (symbol is null) return FinanceOperation<Holding>.Invalid("symbol", "Enter a ticker such as VWRL.AS or AAPL.");
        if (Validate(draft) is { } invalid) return invalid;
        var existing = await repository.ListHoldingsAsync(ownerId, cancellationToken);
        if (existing.Count >= PortfolioRules.MaxHoldings)
            return FinanceOperation<Holding>.Invalid("symbol", $"You can track at most {PortfolioRules.MaxHoldings} holdings.");
        if (existing.Any(x => x.Symbol == symbol && x.AccountId == draft.AccountId))
            return FinanceOperation<Holding>.Invalid("symbol", "You already track this holding.");
        if (draft.AccountId is { } accountId && await accounts.GetAsync(accountId, ownerId, cancellationToken) is null)
            return FinanceOperation<Holding>.Invalid("accountId", "The account could not be found.");

        var now = clock.GetUtcNow();
        var holding = new Holding(Guid.CreateVersion7(), ownerId, draft.AccountId, symbol,
            ExpenseRules.Clean(draft.Name) ?? symbol, draft.AssetType ?? AssetTypes.Stock,
            ExpenseRules.NormalizeCurrency(draft.Currency) ?? ExpenseRules.DefaultCurrency, null, null, null, now, now);
        await repository.AddHoldingAsync(holding, cancellationToken);
        return FinanceOperation<Holding>.Ok(holding);
    }

    public async Task<FinanceOperation<Holding>> UpdateHoldingAsync(Guid id, Guid ownerId, HoldingDraft draft,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetHoldingAsync(id, ownerId, cancellationToken);
        if (existing is null) return FinanceOperation<Holding>.NotFound();
        if (Validate(draft) is { } invalid) return invalid;
        if (draft.AccountId is { } accountId && await accounts.GetAsync(accountId, ownerId, cancellationToken) is null)
            return FinanceOperation<Holding>.Invalid("accountId", "The account could not be found.");
        var updated = existing with
        {
            Name = ExpenseRules.Clean(draft.Name) ?? existing.Name,
            AssetType = draft.AssetType ?? existing.AssetType,
            Currency = ExpenseRules.NormalizeCurrency(draft.Currency) ?? existing.Currency,
            AccountId = draft.AccountId ?? existing.AccountId,
            UpdatedAt = clock.GetUtcNow()
        };
        await repository.UpdateHoldingAsync(updated, cancellationToken);
        return FinanceOperation<Holding>.Ok(updated);
    }

    public Task<bool> DeleteHoldingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteHoldingAsync(id, ownerId, cancellationToken);

    private static FinanceOperation<Holding>? Validate(HoldingDraft draft)
    {
        if (draft.AssetType is not null && !AssetTypes.IsValid(draft.AssetType))
            return FinanceOperation<Holding>.Invalid("assetType", "Use stock, etf, fund, crypto, bond, or other.");
        if (draft.Currency is not null && ExpenseRules.NormalizeCurrency(draft.Currency) is null)
            return FinanceOperation<Holding>.Invalid("currency", "Use a three-letter currency code such as EUR.");
        if (ExpenseRules.Clean(draft.Name) is { Length: > PortfolioRules.MaxNameLength })
            return FinanceOperation<Holding>.Invalid("name",
                $"The name can contain at most {PortfolioRules.MaxNameLength} characters.");
        return null;
    }

    // ---- Trades ----

    public Task<IReadOnlyList<InvestmentTrade>> ListTradesAsync(Guid ownerId, Guid holdingId,
        CancellationToken cancellationToken) => repository.ListTradesAsync(ownerId, holdingId, cancellationToken);

    public async Task<FinanceOperation<InvestmentTrade>> AddTradeAsync(Guid holdingId, Guid ownerId, TradeDraft draft,
        DateOnly today, CancellationToken cancellationToken)
    {
        var holding = await repository.GetHoldingAsync(holdingId, ownerId, cancellationToken);
        if (holding is null) return FinanceOperation<InvestmentTrade>.NotFound();
        if (!TradeKinds.IsValid(draft.Kind))
            return FinanceOperation<InvestmentTrade>.Invalid("kind", "Use buy, sell, dividend, fee, or split.");
        if (draft.Quantity is not > 0 || draft.Quantity > PortfolioRules.MaxQuantity)
            return FinanceOperation<InvestmentTrade>.Invalid("quantity", "Enter a quantity above zero.");
        var isSplit = draft.Kind == TradeKinds.Split;
        if (!isSplit && (draft.Price is not >= 0 || draft.Price > PortfolioRules.MaxPrice ||
                         draft.Price == 0 && draft.Kind != TradeKinds.Buy))
            return FinanceOperation<InvestmentTrade>.Invalid("price", "Enter a price.");
        if (draft.Fees is < 0 or > PortfolioRules.MaxPrice)
            return FinanceOperation<InvestmentTrade>.Invalid("fees", "Fees cannot be negative.");
        var date = draft.TradedOn ?? today;
        if (date > today.AddDays(1))
            return FinanceOperation<InvestmentTrade>.Invalid("tradedOn", "The date cannot be in the future.");
        if (date < today.AddYears(-30))
            return FinanceOperation<InvestmentTrade>.Invalid("tradedOn", "The date is too far back.");
        if (ExpenseRules.Clean(draft.Note) is { Length: > ExpenseRules.MaxNoteLength })
            return FinanceOperation<InvestmentTrade>.Invalid("note",
                $"The note can contain at most {ExpenseRules.MaxNoteLength} characters.");

        var trade = new InvestmentTrade(Guid.CreateVersion7(), ownerId, holdingId, draft.Kind!, date,
            draft.Quantity!.Value, isSplit ? 0m : draft.Price ?? 0m, draft.Fees ?? 0m,
            ExpenseRules.Clean(draft.Note), clock.GetUtcNow());

        // Selling more than is held would silently clamp; refuse so the owner fixes the typo.
        if (draft.Kind == TradeKinds.Sell)
        {
            var held = PortfolioMath.Calculate(await repository.ListTradesAsync(ownerId, holdingId, cancellationToken))
                .Quantity;
            if (draft.Quantity > held)
                return FinanceOperation<InvestmentTrade>.Invalid("quantity", $"You only hold {held:0.####}.");
        }
        await repository.AddTradeAsync(trade, cancellationToken);

        // A trade is a fresh price observation when the holding has none yet.
        if (holding.LastPrice is null && !isSplit && draft.Kind is TradeKinds.Buy or TradeKinds.Sell)
            await SetPriceAsync(holdingId, ownerId, trade.Price, today, cancellationToken, source: "trade");
        return FinanceOperation<InvestmentTrade>.Ok(trade);
    }

    public Task<bool> DeleteTradeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.DeleteTradeAsync(id, ownerId, cancellationToken);

    // ---- Prices ----

    public Task<FinanceOperation<Holding>> SetPriceAsync(Guid holdingId, Guid ownerId, decimal? price, DateOnly today,
        CancellationToken cancellationToken) => SetPriceAsync(holdingId, ownerId, price, today, cancellationToken, "manual");

    private async Task<FinanceOperation<Holding>> SetPriceAsync(Guid holdingId, Guid ownerId, decimal? price,
        DateOnly today, CancellationToken cancellationToken, string source)
    {
        if (price is not > 0 || price > PortfolioRules.MaxPrice)
            return FinanceOperation<Holding>.Invalid("price", "Enter a price above zero.");
        var holding = await repository.GetHoldingAsync(holdingId, ownerId, cancellationToken);
        if (holding is null) return FinanceOperation<Holding>.NotFound();
        var now = clock.GetUtcNow();
        var updated = holding with { LastPrice = price, LastPriceAt = now, PriceSource = source, UpdatedAt = now };
        await repository.UpdateHoldingAsync(updated, cancellationToken);
        await repository.SavePricePointAsync(holdingId, today, price.Value, cancellationToken);
        return FinanceOperation<Holding>.Ok(updated);
    }

    public async Task<int> RefreshPricesAsync(Guid ownerId, DateOnly today, bool force,
        CancellationToken cancellationToken)
    {
        if (!quotes.IsConfigured) return 0;
        var now = clock.GetUtcNow();
        var held = PortfolioMathHolders(await repository.ListHoldingsAsync(ownerId, cancellationToken),
            await repository.ListTradesAsync(ownerId, null, cancellationToken));
        var due = held.Where(x => force || x.LastPriceAt is null || now - x.LastPriceAt > PortfolioRules.QuoteFreshness)
            .ToArray();
        if (due.Length == 0) return 0;

        IReadOnlyDictionary<string, Quote> found;
        try
        {
            found = await quotes.GetQuotesAsync(due.Select(x => x.Symbol).Distinct().ToArray(), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                              or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
        {
            return 0;
        }

        var changed = 0;
        foreach (var holding in due)
        {
            if (!found.TryGetValue(holding.Symbol, out var quote) || quote.Price <= 0) continue;
            await repository.UpdateHoldingAsync(holding with
            {
                LastPrice = quote.Price, LastPriceAt = quote.AsOf, PriceSource = "provider", UpdatedAt = now
            }, cancellationToken);
            await repository.SavePricePointAsync(holding.Id, today, quote.Price, cancellationToken);
            changed++;
        }
        return changed;
    }

    /// <summary>Only holdings the owner still owns need a quote.</summary>
    private static IReadOnlyList<Holding> PortfolioMathHolders(IReadOnlyList<Holding> holdings,
        IReadOnlyList<InvestmentTrade> trades)
    {
        var byHolding = trades.GroupBy(x => x.HoldingId).ToDictionary(g => g.Key, g => PortfolioMath.Calculate(g));
        return holdings.Where(x => byHolding.TryGetValue(x.Id, out var p) && p.Quantity > 0).ToArray();
    }

    public Task<IReadOnlyList<PricePoint>> HistoryAsync(Guid holdingId, Guid ownerId, int days,
        CancellationToken cancellationToken) =>
        repository.ListPriceHistoryAsync(holdingId, ownerId, Math.Clamp(days, 7, 1825), cancellationToken);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
