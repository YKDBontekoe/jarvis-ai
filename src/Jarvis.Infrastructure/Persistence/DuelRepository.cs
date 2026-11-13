using Jarvis.Application.Discover;
using Jarvis.Domain.Discover;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence;

public sealed class DuelEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid RemoteAgentId { get; set; }
    public string PeerName { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? TheirScore { get; set; }
    public DateTimeOffset? TheirScoreAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Duel ToRecord() => new(Id, OwnerId, Code, Role, RemoteAgentId, PeerName, Metric, StartsOn, EndsOn, Status,
        TheirScore, TheirScoreAt, LastError, CreatedAt);
}

public sealed class DuelRepository(JarvisDbContext db) : IDuelRepository
{
    public async Task<IReadOnlyList<Duel>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Duels.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.CreatedAt)
            .Take(100).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<Duel?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Duels.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken))?.ToRecord();

    public async Task<Duel?> FindAsync(Guid ownerId, string code, string role, CancellationToken cancellationToken) =>
        (await db.Duels.AsNoTracking().SingleOrDefaultAsync(
            x => x.OwnerId == ownerId && x.Code == code && x.Role == role, cancellationToken))?.ToRecord();

    public async Task AddAsync(Duel duel, CancellationToken cancellationToken)
    {
        db.Duels.Add(new DuelEntity
        {
            Id = duel.Id, OwnerId = duel.OwnerId, Code = duel.Code, Role = duel.Role,
            RemoteAgentId = duel.RemoteAgentId, PeerName = duel.PeerName, Metric = duel.Metric,
            StartsOn = duel.StartsOn, EndsOn = duel.EndsOn, Status = duel.Status, TheirScore = duel.TheirScore,
            TheirScoreAt = duel.TheirScoreAt, LastError = duel.LastError, CreatedAt = duel.CreatedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(Duel duel, CancellationToken cancellationToken) =>
        await db.Duels.Where(x => x.Id == duel.Id && x.OwnerId == duel.OwnerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, duel.Status)
                .SetProperty(x => x.TheirScore, duel.TheirScore)
                .SetProperty(x => x.TheirScoreAt, duel.TheirScoreAt)
                .SetProperty(x => x.LastError, duel.LastError), cancellationToken) > 0;
}

internal sealed class DuelConfiguration : IEntityTypeConfiguration<DuelEntity>
{
    public void Configure(EntityTypeBuilder<DuelEntity> builder)
    {
        builder.ToTable("duels");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(DuelRules.CodeLength).IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(16).IsRequired();
        builder.Property(x => x.RemoteAgentId).HasColumnName("remote_agent_id");
        builder.Property(x => x.PeerName).HasColumnName("peer_name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Metric).HasColumnName("metric").HasMaxLength(32).IsRequired();
        builder.Property(x => x.StartsOn).HasColumnName("starts_on").HasColumnType("date");
        builder.Property(x => x.EndsOn).HasColumnName("ends_on").HasColumnType("date");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(x => x.TheirScore).HasColumnName("their_score");
        builder.Property(x => x.TheirScoreAt).HasColumnName("their_score_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(200);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.OwnerId, x.Code, x.Role }).IsUnique();
    }
}

/// <summary>
/// Where Agent2Agent is not wired (the worker), duels cannot reach a friend's Jarvis. The API registers the real
/// client after this one, which replaces it.
/// </summary>
internal sealed class UnavailableDuelPeerClient : IDuelPeerClient
{
    public Task<PeerDuelTerms> GetTermsAsync(Guid ownerId, Guid remoteAgentId, string code,
        CancellationToken cancellationToken) =>
        throw new DuelPeerException("Duels are not available here.");

    public Task<int> GetScoreAsync(Guid ownerId, Guid remoteAgentId, string code, string myRole,
        CancellationToken cancellationToken) =>
        throw new DuelPeerException("Duels are not available here.");
}
