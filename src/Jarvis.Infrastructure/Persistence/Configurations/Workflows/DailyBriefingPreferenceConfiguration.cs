using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class DailyBriefingPreferenceConfiguration : IEntityTypeConfiguration<DailyBriefingPreference>
{
    public void Configure(EntityTypeBuilder<DailyBriefingPreference> builder)
    {
            builder.ToTable("daily_briefings");
            builder.HasKey(x => x.OwnerId);
            builder.Property(x => x.OwnerId).HasColumnName("owner_id").ValueGeneratedNever();
            builder.Property(x => x.Enabled).HasColumnName("enabled");
            builder.Property(x => x.LocalTime).HasColumnName("local_time").HasColumnType("time without time zone");
            builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100).IsRequired();
            builder.Property(x => x.WorkflowId).HasColumnName("workflow_id").HasMaxLength(300).IsRequired();
            builder.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
            builder.Property(x => x.LastDeliveredDate).HasColumnName("last_delivered_date").HasColumnType("date");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasIndex(x => x.WorkflowId).IsUnique();
            builder.HasIndex(x => new { x.Enabled, x.ScheduleDispatchedAt });
        }
}
