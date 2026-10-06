using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class ConditionWatchConfiguration : IEntityTypeConfiguration<ConditionWatch>
{
    public void Configure(EntityTypeBuilder<ConditionWatch> builder)
    {
            builder.ToTable("condition_watches");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
            builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40).HasDefaultValue(WatchKinds.PublicJson)
                .IsRequired();
            builder.Property(x => x.Url).HasMaxLength(2048).IsRequired();
            builder.Property(x => x.JsonPath).HasMaxLength(512).IsRequired();
            builder.Property(x => x.Comparison).HasMaxLength(16).IsRequired();
            builder.Property(x => x.Threshold).HasColumnName("threshold");
            builder.Property(x => x.IntervalMinutes).HasColumnName("interval_minutes");
            builder.Property(x => x.CredentialProvider).HasColumnName("credential_provider").HasMaxLength(80);
            builder.Property(x => x.Latitude).HasColumnName("latitude");
            builder.Property(x => x.Longitude).HasColumnName("longitude");
            builder.Property(x => x.RadiusMeters).HasColumnName("radius_meters");
            builder.Property(x => x.MinutesBefore).HasColumnName("minutes_before");
            builder.Property(x => x.WorkflowId).HasColumnName("workflow_id").HasMaxLength(300).IsRequired();
            builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
            builder.Property(x => x.LastCheckedAt).HasColumnName("last_checked_at");
            builder.Property(x => x.LastValue).HasColumnName("last_value");
            builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
            builder.Property(x => x.Repeat).HasColumnName("repeat").HasDefaultValue(false);
            builder.Property(x => x.CooldownMinutes).HasColumnName("cooldown_minutes").HasDefaultValue(0);
            builder.Property(x => x.Armed).HasColumnName("armed").HasDefaultValue(true);
            builder.Property(x => x.LastTriggeredAt).HasColumnName("last_triggered_at");
            builder.Property(x => x.TriggerCount).HasColumnName("trigger_count").HasDefaultValue(0);
            builder.HasIndex(x => x.WorkflowId).IsUnique();
            builder.HasIndex(x => new { x.OwnerId, x.Status, x.CreatedAt });
            builder.HasIndex(x => new { x.Status, x.ScheduleDispatchedAt });
        }
}
