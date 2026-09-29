using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class SkillRevisionEntityConfiguration : IEntityTypeConfiguration<SkillRevisionEntity>
{
    public void Configure(EntityTypeBuilder<SkillRevisionEntity> builder)
    {
            builder.ToTable("skill_revisions");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.SkillId).HasColumnName("skill_id");
            builder.Property(x => x.Version).HasColumnName("version");
            builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1_024).IsRequired();
            builder.Property(x => x.Instructions).HasColumnName("instructions").IsRequired();
            builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
            builder.Property(x => x.ChangeNote).HasColumnName("change_note").HasMaxLength(500);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.HasOne<SkillEntity>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => new { x.SkillId, x.Version }).IsUnique();
        }
}
