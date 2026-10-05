using Jarvis.Application.Finance;
using Jarvis.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class FinancialAccountEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = AccountTypes.Checking;
    public string Currency { get; set; } = "EUR";
    public string? Institution { get; set; }
    public string? Last4 { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateOnly OpeningOn { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public FinancialAccount ToRecord() => new(Id, OwnerId, Name, Type, Currency, Institution, Last4, OpeningBalance,
        OpeningOn, Archived, CreatedAt, UpdatedAt);

    public void Apply(FinancialAccount account)
    {
        Name = account.Name;
        Type = account.Type;
        Currency = account.Currency;
        Institution = account.Institution;
        Last4 = account.Last4;
        OpeningBalance = account.OpeningBalance;
        OpeningOn = account.OpeningOn;
        Archived = account.Archived;
        UpdatedAt = account.UpdatedAt;
    }
}

public sealed class HoldingEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? AccountId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssetType { get; set; } = AssetTypes.Stock;
    public string Currency { get; set; } = "EUR";
    public decimal? LastPrice { get; set; }
    public DateTimeOffset? LastPriceAt { get; set; }
    public string? PriceSource { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Holding ToRecord() => new(Id, OwnerId, AccountId, Symbol, Name, AssetType, Currency, LastPrice,
        LastPriceAt, PriceSource, CreatedAt, UpdatedAt);

    public void Apply(Holding holding)
    {
        AccountId = holding.AccountId;
        Symbol = holding.Symbol;
        Name = holding.Name;
        AssetType = holding.AssetType;
        Currency = holding.Currency;
        LastPrice = holding.LastPrice;
        LastPriceAt = holding.LastPriceAt;
        PriceSource = holding.PriceSource;
        UpdatedAt = holding.UpdatedAt;
    }
}

public sealed class InvestmentTradeEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid HoldingId { get; set; }
    public string Kind { get; set; } = TradeKinds.Buy;
    public DateOnly TradedOn { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Fees { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public InvestmentTrade ToRecord() => new(Id, OwnerId, HoldingId, Kind, TradedOn, Quantity, Price, Fees, Note,
        CreatedAt);
}

public sealed class PricePointEntity
{
    public Guid HoldingId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Price { get; set; }
}

public sealed class AccountRepository(JarvisDbContext db) : IAccountRepository
{
    public async Task<IReadOnlyList<FinancialAccount>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.FinancialAccounts.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<FinancialAccount?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.FinancialAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddAsync(FinancialAccount account, CancellationToken cancellationToken)
    {
        var entity = new FinancialAccountEntity
        {
            Id = account.Id, OwnerId = account.OwnerId, CreatedAt = account.CreatedAt
        };
        entity.Apply(account);
        db.FinancialAccounts.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(FinancialAccount account, CancellationToken cancellationToken)
    {
        var entity = await db.FinancialAccounts
            .SingleOrDefaultAsync(x => x.Id == account.Id && x.OwnerId == account.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(account);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.FinancialAccounts.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
}

public sealed class PortfolioRepository(JarvisDbContext db) : IPortfolioRepository
{
    public async Task<IReadOnlyList<Holding>> ListHoldingsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Holdings.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderBy(x => x.Symbol)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<Holding?> GetHoldingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Holdings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddHoldingAsync(Holding holding, CancellationToken cancellationToken)
    {
        var entity = new HoldingEntity { Id = holding.Id, OwnerId = holding.OwnerId, CreatedAt = holding.CreatedAt };
        entity.Apply(holding);
        db.Holdings.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateHoldingAsync(Holding holding, CancellationToken cancellationToken)
    {
        var entity = await db.Holdings
            .SingleOrDefaultAsync(x => x.Id == holding.Id && x.OwnerId == holding.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(holding);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteHoldingAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Holdings.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<InvestmentTrade>> ListTradesAsync(Guid ownerId, Guid? holdingId,
        CancellationToken cancellationToken) =>
        (await db.InvestmentTrades.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && (holdingId == null || x.HoldingId == holdingId))
            .OrderByDescending(x => x.TradedOn).ThenByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<InvestmentTrade?> GetTradeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.InvestmentTrades.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddTradeAsync(InvestmentTrade trade, CancellationToken cancellationToken)
    {
        db.InvestmentTrades.Add(new InvestmentTradeEntity
        {
            Id = trade.Id, OwnerId = trade.OwnerId, HoldingId = trade.HoldingId, Kind = trade.Kind,
            TradedOn = trade.TradedOn, Quantity = trade.Quantity, Price = trade.Price, Fees = trade.Fees,
            Note = trade.Note, CreatedAt = trade.CreatedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteTradeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.InvestmentTrades.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task SavePricePointAsync(Guid holdingId, DateOnly date, decimal price,
        CancellationToken cancellationToken)
    {
        var point = await db.PricePoints.SingleOrDefaultAsync(x => x.HoldingId == holdingId && x.Date == date,
            cancellationToken);
        if (point is null) db.PricePoints.Add(new PricePointEntity { HoldingId = holdingId, Date = date, Price = price });
        else point.Price = price;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PricePoint>> ListPriceHistoryAsync(Guid holdingId, Guid ownerId, int days,
        CancellationToken cancellationToken)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
        var owned = await db.Holdings.AnyAsync(x => x.Id == holdingId && x.OwnerId == ownerId, cancellationToken);
        if (!owned) return [];
        return (await db.PricePoints.AsNoTracking().Where(x => x.HoldingId == holdingId && x.Date >= from)
                .OrderBy(x => x.Date).ToListAsync(cancellationToken))
            .Select(x => new PricePoint(x.Date, x.Price)).ToArray();
    }
}
