using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class OwnerSettingEntityConfiguration : IEntityTypeConfiguration<OwnerSettingEntity>
{
    public void Configure(EntityTypeBuilder<OwnerSettingEntity> builder)
    {
            builder.ToTable("owner_settings");
            builder.HasKey(x => new { x.OwnerId, x.Section });
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Section).HasColumnName("section").HasMaxLength(60);
            builder.Property(x => x.ValueJson).HasColumnName("value").HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasIndex(x => x.Section);
        }
}
