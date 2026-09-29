using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
            builder.ToTable("notifications");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Type).HasMaxLength(60).IsRequired();
            builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
            builder.Property(x => x.Body).HasMaxLength(2_000).IsRequired();
            builder.Property(x => x.SourceId).HasColumnName("source_id");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.ReadAt).HasColumnName("read_at");
            builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        }
}
