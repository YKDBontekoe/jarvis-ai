using Jarvis.Application.Inbox;
using Jarvis.Domain.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class InboxThreadEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Source { get; set; } = InboxSources.Manual;
    public string ExternalKey { get; set; } = string.Empty;
    public Guid? ConnectionId { get; set; }
    public string? ChatId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Counterparty { get; set; }
    public string State { get; set; } = InboxStates.NeedsReply;
    public int Priority { get; set; } = InboxPriorities.Normal;
    public string? Summary { get; set; }
    public string? SuggestedReply { get; set; }
    public string? LastMessagePreview { get; set; }
    public bool LastFromMe { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public DateTimeOffset? SnoozedUntil { get; set; }
    public DateTimeOffset? TriagedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public InboxThread ToRecord() => new(Id, OwnerId, Source, ExternalKey, ConnectionId, ChatId, Title, Counterparty,
        State, Priority, Summary, SuggestedReply, LastMessagePreview, LastFromMe, LastMessageAt, SnoozedUntil,
        TriagedAt, CreatedAt, UpdatedAt);

    public void Apply(InboxThread thread)
    {
        Source = thread.Source;
        ExternalKey = thread.ExternalKey;
        ConnectionId = thread.ConnectionId;
        ChatId = thread.ChatId;
        Title = thread.Title;
        Counterparty = thread.Counterparty;
        State = thread.State;
        Priority = thread.Priority;
        Summary = thread.Summary;
        SuggestedReply = thread.SuggestedReply;
        LastMessagePreview = thread.LastMessagePreview;
        LastFromMe = thread.LastFromMe;
        LastMessageAt = thread.LastMessageAt;
        SnoozedUntil = thread.SnoozedUntil;
        TriagedAt = thread.TriagedAt;
        UpdatedAt = thread.UpdatedAt;
    }
}

public sealed class CommitmentEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Direction { get; set; } = CommitmentDirections.IOwe;
    public string Counterparty { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly? DueOn { get; set; }
    public string Status { get; set; } = CommitmentStatuses.Open;
    public bool Suggested { get; set; }
    public string Source { get; set; } = CommitmentSources.Manual;
    public Guid? InboxThreadId { get; set; }
    public Guid? ReminderId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Commitment ToRecord() => new(Id, OwnerId, Direction, Counterparty, Description, DueOn, Status, Suggested,
        Source, InboxThreadId, ReminderId, CompletedAt, CreatedAt, UpdatedAt);

    public void Apply(Commitment commitment)
    {
        Direction = commitment.Direction;
        Counterparty = commitment.Counterparty;
        Description = commitment.Description;
        DueOn = commitment.DueOn;
        Status = commitment.Status;
        Suggested = commitment.Suggested;
        Source = commitment.Source;
        InboxThreadId = commitment.InboxThreadId;
        ReminderId = commitment.ReminderId;
        CompletedAt = commitment.CompletedAt;
        UpdatedAt = commitment.UpdatedAt;
    }
}

public sealed class InboxRepository(JarvisDbContext db) : IInboxRepository
{
    public async Task<IReadOnlyList<InboxThread>> ListThreadsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.InboxThreads.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.LastMessageAt ?? x.UpdatedAt).Take(InboxRules.MaxThreads)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<InboxThread?> GetThreadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.InboxThreads.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<InboxThread?> FindThreadAsync(Guid ownerId, string source, string externalKey,
        CancellationToken cancellationToken) =>
        (await db.InboxThreads.AsNoTracking().SingleOrDefaultAsync(
            x => x.OwnerId == ownerId && x.Source == source && x.ExternalKey == externalKey,
            cancellationToken))?.ToRecord();

    public async Task AddThreadAsync(InboxThread thread, CancellationToken cancellationToken)
    {
        var entity = new InboxThreadEntity { Id = thread.Id, OwnerId = thread.OwnerId, CreatedAt = thread.CreatedAt };
        entity.Apply(thread);
        db.InboxThreads.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateThreadAsync(InboxThread thread, CancellationToken cancellationToken)
    {
        var entity = await db.InboxThreads
            .SingleOrDefaultAsync(x => x.Id == thread.Id && x.OwnerId == thread.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(thread);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteThreadAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.InboxThreads.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<Commitment>> ListCommitmentsAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.Commitments.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.CreatedAt).Take(1_000).ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToArray();

    public async Task<Commitment?> GetCommitmentAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Commitments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddCommitmentAsync(Commitment commitment, CancellationToken cancellationToken)
    {
        var entity = new CommitmentEntity
        {
            Id = commitment.Id, OwnerId = commitment.OwnerId, CreatedAt = commitment.CreatedAt
        };
        entity.Apply(commitment);
        db.Commitments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateCommitmentAsync(Commitment commitment, CancellationToken cancellationToken)
    {
        var entity = await db.Commitments
            .SingleOrDefaultAsync(x => x.Id == commitment.Id && x.OwnerId == commitment.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(commitment);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteCommitmentAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Commitments.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
}
