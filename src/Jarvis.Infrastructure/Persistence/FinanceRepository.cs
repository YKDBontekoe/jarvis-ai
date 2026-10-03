using Jarvis.Application.Finance;
using Jarvis.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class BudgetEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Category { get; set; } = BudgetCategories.Total;
    public decimal Limit { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? AlertedMonth { get; set; }
    public int AlertedLevel { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Budget ToRecord() => new(Id, OwnerId, Category, Limit, Currency, AlertedMonth, AlertedLevel, CreatedAt,
        UpdatedAt);

    public void Apply(Budget budget)
    {
        Category = budget.Category;
        Limit = budget.Limit;
        Currency = budget.Currency;
        AlertedMonth = budget.AlertedMonth;
        AlertedLevel = budget.AlertedLevel;
        UpdatedAt = budget.UpdatedAt;
    }
}

public sealed class SubscriptionEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string MerchantKey { get; set; } = string.Empty;
    public string Merchant { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Cadence { get; set; } = SubscriptionCadences.Monthly;
    public DateOnly LastChargedOn { get; set; }
    public DateOnly NextDueOn { get; set; }
    public int ChargeCount { get; set; }
    public decimal? PreviousAmount { get; set; }
    public string Status { get; set; } = SubscriptionStatuses.Active;
    public int? RemindDaysBefore { get; set; }
    public Guid? ReminderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Subscription ToRecord() => new(Id, OwnerId, MerchantKey, Merchant, Amount, Currency, Cadence,
        LastChargedOn, NextDueOn, ChargeCount, PreviousAmount, Status, RemindDaysBefore, ReminderId, CreatedAt,
        UpdatedAt);

    public void Apply(Subscription subscription)
    {
        MerchantKey = subscription.MerchantKey;
        Merchant = subscription.Merchant;
        Amount = subscription.Amount;
        Currency = subscription.Currency;
        Cadence = subscription.Cadence;
        LastChargedOn = subscription.LastChargedOn;
        NextDueOn = subscription.NextDueOn;
        ChargeCount = subscription.ChargeCount;
        PreviousAmount = subscription.PreviousAmount;
        Status = subscription.Status;
        RemindDaysBefore = subscription.RemindDaysBefore;
        ReminderId = subscription.ReminderId;
        UpdatedAt = subscription.UpdatedAt;
    }
}

public sealed class FinanceRepository(JarvisDbContext db) : IFinanceRepository
{
    public async Task<IReadOnlyList<Budget>> ListBudgetsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Budgets.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderBy(x => x.Category)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<Budget?> FindBudgetAsync(Guid ownerId, string category, CancellationToken cancellationToken) =>
        (await db.Budgets.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Category == category, cancellationToken))?.ToRecord();

    public async Task AddBudgetAsync(Budget budget, CancellationToken cancellationToken)
    {
        var entity = new BudgetEntity { Id = budget.Id, OwnerId = budget.OwnerId, CreatedAt = budget.CreatedAt };
        entity.Apply(budget);
        db.Budgets.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateBudgetAsync(Budget budget, CancellationToken cancellationToken)
    {
        var entity = await db.Budgets
            .SingleOrDefaultAsync(x => x.Id == budget.Id && x.OwnerId == budget.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(budget);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteBudgetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Budgets.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.Subscriptions.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderBy(x => x.NextDueOn)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<Subscription?> GetSubscriptionAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Subscriptions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var entity = new SubscriptionEntity
        {
            Id = subscription.Id, OwnerId = subscription.OwnerId, CreatedAt = subscription.CreatedAt
        };
        entity.Apply(subscription);
        db.Subscriptions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var entity = await db.Subscriptions.SingleOrDefaultAsync(
            x => x.Id == subscription.Id && x.OwnerId == subscription.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(subscription);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
