using Jarvis.Application.People.Radar;
using Jarvis.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class PersonChannelLinkEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid PersonId { get; set; }
    public Guid ConnectionId { get; set; }
    public string ChatId { get; set; } = string.Empty;
    public int? ToneScore { get; set; }
    public string? ToneReason { get; set; }
    public DateTimeOffset? ToneAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public PersonChannelLink ToRecord() => new(Id, OwnerId, PersonId, ConnectionId, ChatId, ToneScore, ToneReason,
        ToneAt, CreatedAt);
}

public sealed class PersonLinkRepository(JarvisDbContext db) : IPersonLinkRepository
{
    public async Task<IReadOnlyList<PersonChannelLink>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.PersonChannelLinks.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<PersonChannelLink?> FindAsync(Guid ownerId, Guid connectionId, string chatId,
        CancellationToken cancellationToken) =>
        (await db.PersonChannelLinks.AsNoTracking().SingleOrDefaultAsync(
            x => x.OwnerId == ownerId && x.ConnectionId == connectionId && x.ChatId == chatId,
            cancellationToken))?.ToRecord();

    public async Task AddAsync(PersonChannelLink link, CancellationToken cancellationToken)
    {
        db.PersonChannelLinks.Add(new PersonChannelLinkEntity
        {
            Id = link.Id, OwnerId = link.OwnerId, PersonId = link.PersonId, ConnectionId = link.ConnectionId,
            ChatId = link.ChatId, ToneScore = link.ToneScore, ToneReason = link.ToneReason, ToneAt = link.ToneAt,
            CreatedAt = link.CreatedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid personId, Guid ownerId, CancellationToken cancellationToken) =>
        await db.PersonChannelLinks.Where(x => x.Id == id && x.PersonId == personId && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task UpdateToneAsync(Guid id, Guid ownerId, int? score, string? reason, DateTimeOffset at,
        CancellationToken cancellationToken) =>
        await db.PersonChannelLinks.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ToneScore, score).SetProperty(x => x.ToneReason, reason)
                .SetProperty(x => x.ToneAt, at), cancellationToken);
}
