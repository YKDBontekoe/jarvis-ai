using Jarvis.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class BrowserSessionEntityConfiguration : IEntityTypeConfiguration<BrowserSessionEntity>
{
    public void Configure(EntityTypeBuilder<BrowserSessionEntity> builder)
    {
            builder.ToTable("browser_sessions");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
            builder.Property(x => x.Goal).HasColumnName("goal").HasMaxLength(1_000).IsRequired();
            builder.Property(x => x.StartUrl).HasColumnName("start_url").HasMaxLength(500);
            builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => new { x.OwnerId, x.ConversationId, x.Status });
        }
}
