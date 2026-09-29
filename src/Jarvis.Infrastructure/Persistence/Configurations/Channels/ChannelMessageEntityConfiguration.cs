using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Channels;

internal sealed class ChannelMessageEntityConfiguration : IEntityTypeConfiguration<ChannelMessageEntity>
{
    public void Configure(EntityTypeBuilder<ChannelMessageEntity> builder)
    {
            builder.ToTable("channel_messages");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
            builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(4).IsRequired();
            builder.Property(x => x.Peer).HasColumnName("peer").HasMaxLength(80).IsRequired();
            builder.Property(x => x.Text).HasColumnName("text").IsRequired();
            builder.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(200);
            builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            builder.Property(x => x.Error).HasColumnName("error").HasMaxLength(500);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            builder.Property(x => x.LeaseUntil).HasColumnName("lease_until");
            builder.HasOne<ChannelConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => new { x.ConnectionId, x.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");
            builder.HasIndex(x => new { x.Direction, x.Status, x.CreatedAt });
            builder.HasIndex(x => new { x.ConnectionId, x.CreatedAt });
        }
}
