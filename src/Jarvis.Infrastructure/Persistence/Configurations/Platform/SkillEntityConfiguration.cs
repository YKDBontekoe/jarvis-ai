using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class SkillEntityConfiguration : IEntityTypeConfiguration<SkillEntity>
{
    public void Configure(EntityTypeBuilder<SkillEntity> builder)
    {
            builder.ToTable("skills");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
            builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1_024).IsRequired();
            builder.Property(x => x.Instructions).HasColumnName("instructions").IsRequired();
            builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
            builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            builder.Property(x => x.IsLocked).HasColumnName("is_locked");
            builder.Property(x => x.Version).HasColumnName("version");
            builder.Property(x => x.UseCount).HasColumnName("use_count");
            builder.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
            builder.HasIndex(x => new { x.OwnerId, x.Status });
        }
}
