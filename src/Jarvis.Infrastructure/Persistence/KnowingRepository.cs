using Jarvis.Application.Knowing;
using Jarvis.Domain.Knowing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence;

public sealed class MemoryReviewEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid MemoryId { get; set; }
    public string Verdict { get; set; } = string.Empty;
    public string Snapshot { get; set; } = string.Empty;
    public DateTimeOffset ReviewedAt { get; set; }

    public MemoryReview ToRecord() => new(Id, OwnerId, MemoryId, Verdict, Snapshot, ReviewedAt);
}

public sealed class ChatModeEntity
{
    public Guid ConversationId { get; set; }
    public Guid OwnerId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }

    public ChatMode ToRecord() => new(ConversationId, OwnerId, Kind, PayloadJson, CreatedAt);
}

public sealed class KnowingRepository(JarvisDbContext db) : IKnowingRepository
{
    public async Task AddReviewAsync(MemoryReview review, CancellationToken cancellationToken)
    {
        db.MemoryReviews.Add(new MemoryReviewEntity
        {
            Id = review.Id, OwnerId = review.OwnerId, MemoryId = review.MemoryId, Verdict = review.Verdict,
            Snapshot = review.Snapshot, ReviewedAt = review.ReviewedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryReview>> ListReviewsAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.MemoryReviews.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderBy(x => x.ReviewedAt)
            .Take(20_000).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task AddChatModeAsync(ChatMode mode, CancellationToken cancellationToken)
    {
        db.ChatModes.Add(new ChatModeEntity
        {
            ConversationId = mode.ConversationId, OwnerId = mode.OwnerId, Kind = mode.Kind,
            PayloadJson = mode.PayloadJson, CreatedAt = mode.CreatedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ChatMode?> GetChatModeAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.ChatModes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ConversationId == conversationId && x.OwnerId == ownerId, cancellationToken))
        ?.ToRecord();

    public async Task<IReadOnlyList<ChatMode>> ListChatModesAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.ChatModes.AsNoTracking().Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.CreatedAt)
            .Take(200).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();
}

/// <summary>Reads the owner's decided approvals for the trust ladder; never changes them.</summary>
public sealed class ApprovalHistory(JarvisDbContext db) : IApprovalHistory
{
    public async Task<IReadOnlyList<DecidedApproval>> ListDecidedAsync(Guid ownerId, DateTimeOffset since, int limit,
        CancellationToken cancellationToken) =>
        (await db.ToolApprovals.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.Approved != null && x.DecidedAt != null && x.DecidedAt >= since)
            .OrderByDescending(x => x.DecidedAt).Take(Math.Clamp(limit, 1, 10_000))
            .Select(x => new { x.ToolName, x.ArgumentsJson, Approved = x.Approved!.Value, DecidedAt = x.DecidedAt!.Value })
            .ToListAsync(cancellationToken))
        .Select(x => new DecidedApproval(x.ToolName, x.ArgumentsJson, x.Approved, x.DecidedAt))
        .OrderBy(x => x.DecidedAt).ToArray();
}

internal sealed class MemoryReviewConfiguration : IEntityTypeConfiguration<MemoryReviewEntity>
{
    public void Configure(EntityTypeBuilder<MemoryReviewEntity> builder)
    {
        builder.ToTable("memory_reviews");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.MemoryId).HasColumnName("memory_id");
        builder.Property(x => x.Verdict).HasColumnName("verdict").HasMaxLength(16).IsRequired();
        builder.Property(x => x.Snapshot).HasColumnName("snapshot").HasMaxLength(KnowingRules.SnapshotLength)
            .IsRequired();
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.HasIndex(x => new { x.OwnerId, x.ReviewedAt });
    }
}

internal sealed class ChatModeConfiguration : IEntityTypeConfiguration<ChatModeEntity>
{
    public void Configure(EntityTypeBuilder<ChatModeEntity> builder)
    {
        builder.ToTable("chat_modes");
        builder.HasKey(x => x.ConversationId);
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
    }
}
