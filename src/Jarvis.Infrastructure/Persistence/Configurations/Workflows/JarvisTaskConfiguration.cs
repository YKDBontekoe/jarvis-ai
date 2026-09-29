using Jarvis.Domain.Conversations;
using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class JarvisTaskConfiguration : IEntityTypeConfiguration<JarvisTask>
{
    public void Configure(EntityTypeBuilder<JarvisTask> builder)
    {
            builder.ToTable("tasks");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
            builder.Property(x => x.Prompt).HasColumnName("prompt").HasMaxLength(32_000).IsRequired();
            builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
            builder.Property(x => x.WorkflowId).HasColumnName("workflow_id").HasMaxLength(300).IsRequired();
            builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
            builder.Property(x => x.UserMessageId).HasColumnName("user_message_id");
            builder.Property(x => x.ResultMessageId).HasColumnName("result_message_id");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
            builder.Property(x => x.StartedAt).HasColumnName("started_at");
            builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
            builder.Property(x => x.Summary).HasMaxLength(8_000);
            builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => x.WorkflowId).IsUnique();
            builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
            builder.HasIndex(x => new { x.OwnerId, x.ConversationId, x.Status });
            builder.HasIndex(x => new { x.Status, x.ScheduleDispatchedAt });
        }
}
