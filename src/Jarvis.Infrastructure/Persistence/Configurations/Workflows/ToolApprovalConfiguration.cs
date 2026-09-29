using Jarvis.Domain.Approvals;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Workflows;
using Jarvis.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class ToolApprovalConfiguration : IEntityTypeConfiguration<ToolApproval>
{
    public void Configure(EntityTypeBuilder<ToolApproval> builder)
    {
            builder.ToTable("tool_approvals");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.RequestId).HasMaxLength(200).IsRequired();
            builder.Property(x => x.ToolCallId).HasMaxLength(200).IsRequired();
            builder.Property(x => x.ToolName).HasMaxLength(300).IsRequired();
            builder.Property(x => x.ArgumentsJson).HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
            builder.Property(x => x.ResumeStatus).HasMaxLength(20).HasDefaultValue("not_started").IsRequired();
            builder.Property(x => x.ResumeStartedAt);
            builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<JarvisTask>().WithMany().HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.SetNull);
            builder.HasIndex(x => new { x.OwnerId, x.Status, x.CreatedAt });
            builder.HasIndex(x => new { x.OwnerId, x.RequestId, x.ToolCallId })
                .IsUnique().HasDatabaseName("ux_tool_approvals_idempotency");
        }
}
