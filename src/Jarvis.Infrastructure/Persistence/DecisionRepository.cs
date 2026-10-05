using Jarvis.Application.Decisions;
using Jarvis.Domain.Decisions;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class DecisionEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Context { get; set; }
    public string Prediction { get; set; } = string.Empty;
    public double Probability { get; set; }
    public DateOnly ReviewOn { get; set; }
    public bool? Outcome { get; set; }
    public string? OutcomeNote { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ReminderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Decision ToRecord() => new(Id, OwnerId, Title, Context, Prediction, Probability, ReviewOn, Outcome,
        OutcomeNote, ResolvedAt, ReminderId, CreatedAt, UpdatedAt);

    public void Apply(Decision decision)
    {
        Title = decision.Title;
        Context = decision.Context;
        Prediction = decision.Prediction;
        Probability = decision.Probability;
        ReviewOn = decision.ReviewOn;
        Outcome = decision.Outcome;
        OutcomeNote = decision.OutcomeNote;
        ResolvedAt = decision.ResolvedAt;
        ReminderId = decision.ReminderId;
        UpdatedAt = decision.UpdatedAt;
    }
}

public sealed class DecisionRepository(JarvisDbContext db) : IDecisionRepository
{
    public async Task<IReadOnlyList<Decision>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Decisions.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.ReviewOn).ThenByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<Decision?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Decisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public Task<int> CountUnresolvedAsync(Guid ownerId, CancellationToken cancellationToken) =>
        db.Decisions.CountAsync(x => x.OwnerId == ownerId && x.Outcome == null, cancellationToken);

    public async Task AddAsync(Decision decision, CancellationToken cancellationToken)
    {
        var entity = new DecisionEntity { Id = decision.Id, OwnerId = decision.OwnerId, CreatedAt = decision.CreatedAt };
        entity.Apply(decision);
        db.Decisions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(Decision decision, CancellationToken cancellationToken)
    {
        var entity = await db.Decisions
            .SingleOrDefaultAsync(x => x.Id == decision.Id && x.OwnerId == decision.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(decision);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Decisions.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<Decision>> ListActiveBetweenAsync(Guid ownerId, DateTimeOffset from,
        DateTimeOffset to, int limit, CancellationToken cancellationToken) =>
        (await db.Decisions.AsNoTracking()
            .Where(x => x.OwnerId == ownerId &&
                ((x.CreatedAt >= from && x.CreatedAt < to) || (x.ResolvedAt >= from && x.ResolvedAt < to)))
            .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 5_000))
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();
}
