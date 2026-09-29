using Jarvis.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
            builder.ToTable("audit_events");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.AgentRunId).HasColumnName("agent_run_id");
            builder.Property(x => x.Tool).HasMaxLength(120).IsRequired();
            builder.Property(x => x.Action).HasMaxLength(160).IsRequired();
            builder.Property(x => x.RiskClass).HasColumnName("risk_class").HasMaxLength(40).IsRequired();
            builder.Property(x => x.ApprovalId).HasColumnName("approval_id");
            builder.Property(x => x.Timestamp).HasColumnName("timestamp");
            builder.Property(x => x.Success).HasColumnName("success");
            builder.Property(x => x.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
            builder.HasIndex(x => new { x.OwnerId, x.Timestamp });
        }
}
