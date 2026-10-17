using Jarvis.Application.Meetings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Meetings;

internal sealed class MeetingConfiguration : IEntityTypeConfiguration<MeetingEntity>
{
    public void Configure(EntityTypeBuilder<MeetingEntity> builder)
    {
        builder.ToTable("meetings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(MeetingRules.MaxTitleLength).IsRequired();
        builder.Property(x => x.StartsAt).HasColumnName("starts_at");
        builder.Property(x => x.ProjectId).HasColumnName("project_id");
        builder.Property(x => x.DocumentJson).HasColumnName("document").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.StartsAt });
        builder.HasIndex(x => new { x.OwnerId, x.ProjectId });
    }
}
