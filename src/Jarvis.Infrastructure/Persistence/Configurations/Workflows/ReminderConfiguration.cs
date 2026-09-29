using Jarvis.Domain.Conversations;
using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
            builder.ToTable("reminders");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
            builder.Property(x => x.DueAt).HasColumnName("due_at");
            builder.Property(x => x.WorkflowId).HasColumnName("workflow_id").HasMaxLength(300).IsRequired();
            builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
            builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
            builder.Property(x => x.Recurrence).HasColumnName("recurrence").HasMaxLength(20).HasDefaultValue("none")
                .IsRequired();
            builder.Property(x => x.Weekdays).HasColumnName("weekdays");
            builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100).HasDefaultValue("UTC")
                .IsRequired();
            builder.Property(x => x.LocalTime).HasColumnName("local_time").HasColumnType("time without time zone");
            builder.Property(x => x.Until).HasColumnName("until").HasColumnType("date");
            builder.Property(x => x.LastDeliveredAt).HasColumnName("last_delivered_at");
            builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
            builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.SetNull);
            builder.HasIndex(x => x.WorkflowId).IsUnique();
            builder.HasIndex(x => x.ConversationId).IsUnique().HasFilter("conversation_id IS NOT NULL");
            builder.HasIndex(x => new { x.OwnerId, x.DueAt });
            builder.HasIndex(x => new { x.Status, x.ScheduleDispatchedAt });
        }
}
