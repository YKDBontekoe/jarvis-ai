using Jarvis.Domain.Automations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Automations;

internal sealed class AutomationRunConfiguration : IEntityTypeConfiguration<AutomationRun>
{
    public void Configure(EntityTypeBuilder<AutomationRun> entity)
    {
        entity.ToTable("automation_runs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.RuleId).HasColumnName("rule_id");
        entity.Property(x => x.OwnerId).HasColumnName("owner_id");
        entity.Property(x => x.WorkflowId).HasColumnName("workflow_id").HasMaxLength(300).IsRequired();
        entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        entity.Property(x => x.TriggerKind).HasColumnName("trigger_kind").HasMaxLength(40).IsRequired();
        entity.Property(x => x.TriggerReason).HasColumnName("trigger_reason").HasMaxLength(500).IsRequired();
        entity.Property(x => x.TestRun).HasColumnName("test_run");
        entity.Property(x => x.EventJson).HasColumnName("event_json").HasMaxLength(2_000);
        entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
        entity.Property(x => x.ActionResultsJson).HasColumnName("action_results_json").HasMaxLength(16_000)
            .IsRequired();
        entity.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(500);
        entity.Property(x => x.ApprovalId).HasColumnName("approval_id");
        entity.Property(x => x.StartedAt).HasColumnName("started_at");
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
        entity.HasIndex(x => x.WorkflowId).IsUnique();
        entity.HasIndex(x => new { x.RuleId, x.IdempotencyKey }).IsUnique();
        entity.HasIndex(x => new { x.OwnerId, x.StartedAt });
        entity.HasIndex(x => new { x.RuleId, x.StartedAt });
    }
}
