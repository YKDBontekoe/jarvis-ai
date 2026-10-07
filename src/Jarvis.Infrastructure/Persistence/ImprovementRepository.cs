using Jarvis.Application.Improvements;
using Jarvis.Domain.Improvements;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ImprovementProposalEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Kind { get; set; } = ImprovementKinds.Memory;
    public string Fingerprint { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = ImprovementStatuses.Pending;
    public Guid? ResultingRef { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ImprovementProposal ToRecord() => new(Id, OwnerId, Kind, Fingerprint, Title, Evidence, Confidence,
        PayloadJson, Status, ResultingRef, CreatedAt, UpdatedAt);

    public static ImprovementProposalEntity From(ImprovementProposal proposal) => new()
    {
        Id = proposal.Id, OwnerId = proposal.OwnerId, Kind = proposal.Kind, Fingerprint = proposal.Fingerprint,
        Title = proposal.Title, Evidence = proposal.Evidence, Confidence = proposal.Confidence,
        PayloadJson = proposal.PayloadJson, Status = proposal.Status, ResultingRef = proposal.ResultingRef,
        CreatedAt = proposal.CreatedAt, UpdatedAt = proposal.UpdatedAt
    };

    public void Apply(ImprovementProposal proposal)
    {
        Title = proposal.Title;
        Evidence = proposal.Evidence;
        Confidence = proposal.Confidence;
        PayloadJson = proposal.PayloadJson;
        Status = proposal.Status;
        ResultingRef = proposal.ResultingRef;
        UpdatedAt = proposal.UpdatedAt;
    }
}

public sealed class ImprovementRepository(JarvisDbContext db) : IImprovementRepository
{
    public async Task<IReadOnlyList<ImprovementProposal>> ListAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.ImprovementProposals.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<ImprovementProposal?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.ImprovementProposals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<ImprovementProposal?> FindByFingerprintAsync(Guid ownerId, string fingerprint,
        CancellationToken cancellationToken) =>
        (await db.ImprovementProposals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Fingerprint == fingerprint, cancellationToken))
        ?.ToRecord();

    public async Task AddAsync(ImprovementProposal proposal, CancellationToken cancellationToken)
    {
        db.ImprovementProposals.Add(ImprovementProposalEntity.From(proposal));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(ImprovementProposal proposal, CancellationToken cancellationToken)
    {
        var entity = await db.ImprovementProposals
            .SingleOrDefaultAsync(x => x.Id == proposal.Id && x.OwnerId == proposal.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(proposal);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<int> DeleteManyAsync(Guid ownerId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.ImprovementProposals.Where(x => x.OwnerId == ownerId && ids.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
}
