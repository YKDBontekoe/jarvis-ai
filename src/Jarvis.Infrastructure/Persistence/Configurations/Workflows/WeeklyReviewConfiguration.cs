using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class WeeklyReviewConfiguration : IEntityTypeConfiguration<WeeklyReview>
{
    public void Configure(EntityTypeBuilder<WeeklyReview> builder)
    {
        builder.ToTable("weekly_reviews");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.WeekStart).HasColumnName("week_start").HasColumnType("date");
        builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Story).HasColumnName("story").HasMaxLength(WeeklyReview.MaxStoryLength).IsRequired();
        builder.Property(x => x.Narrated).HasColumnName("narrated");
        builder.Property(x => x.StatsJson).HasColumnName("stats").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.NotifiedAt).HasColumnName("notified_at");
        builder.HasIndex(x => new { x.OwnerId, x.WeekStart }).IsUnique();
    }
}
