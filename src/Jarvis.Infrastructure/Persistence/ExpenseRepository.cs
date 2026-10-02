using Jarvis.Application.Expenses;
using Jarvis.Domain.Expenses;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ExpenseEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = ExpenseRules.DefaultCurrency;
    public string? Merchant { get; set; }
    public string Category { get; set; } = ExpenseCategories.Other;
    public string? Note { get; set; }
    public DateOnly SpentOn { get; set; }
    public Guid? ReceiptFileId { get; set; }
    public string Source { get; set; } = ExpenseSources.App;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Expense ToRecord() => new(Id, OwnerId, Amount, Currency, Merchant, Category, Note, SpentOn, ReceiptFileId,
        Source, CreatedAt, UpdatedAt);

    public void Apply(Expense expense)
    {
        Amount = expense.Amount;
        Currency = expense.Currency;
        Merchant = expense.Merchant;
        Category = expense.Category;
        Note = expense.Note;
        SpentOn = expense.SpentOn;
        ReceiptFileId = expense.ReceiptFileId;
        Source = expense.Source;
        UpdatedAt = expense.UpdatedAt;
    }
}

public sealed class ExpenseRepository(JarvisDbContext db) : IExpenseRepository
{
    public async Task<IReadOnlyList<Expense>> ListAsync(Guid ownerId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken) =>
        (await db.Expenses.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.SpentOn >= from && x.SpentOn <= to)
            .OrderByDescending(x => x.SpentOn).ThenByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToArray();

    public async Task<IReadOnlyList<Expense>> ListRecentAsync(Guid ownerId, int limit,
        CancellationToken cancellationToken) =>
        (await db.Expenses.AsNoTracking()
            .Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToArray();

    public async Task<Expense?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Expenses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddAsync(Expense expense, CancellationToken cancellationToken)
    {
        var entity = new ExpenseEntity { Id = expense.Id, OwnerId = expense.OwnerId, CreatedAt = expense.CreatedAt };
        entity.Apply(expense);
        db.Expenses.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(Expense expense, CancellationToken cancellationToken)
    {
        var entity = await db.Expenses
            .SingleOrDefaultAsync(x => x.Id == expense.Id && x.OwnerId == expense.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(expense);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Expenses.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) > 0;
}
