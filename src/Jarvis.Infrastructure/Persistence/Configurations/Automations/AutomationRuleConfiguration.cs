using Jarvis.Domain.Automations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Automations;

internal sealed class AutomationRuleConfiguration : IEntityTypeConfiguration<AutomationRule>
{
    public void Configure(EntityTypeBuilder<AutomationRule> entity)
    {
        entity.ToTable("automation_rules");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.OwnerId).HasColumnName("owner_id");
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.SchemaVersion).HasColumnName("schema_version");
        entity.Property(x => x.DefinitionJson).HasColumnName("definition_json").HasMaxLength(32_000).IsRequired();
        entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
        entity.Property(x => x.ScheduleWorkflowId).HasColumnName("schedule_workflow_id").HasMaxLength(300)
            .IsRequired();
        entity.Property(x => x.ScheduleDispatchedAt).HasColumnName("schedule_dispatched_at");
        entity.Property(x => x.LastRunAt).HasColumnName("last_run_at");
        entity.Property(x => x.NextRunAt).HasColumnName("next_run_at");
        entity.Property(x => x.CooldownUntil).HasColumnName("cooldown_until");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(x => x.ScheduleWorkflowId).IsUnique();
        entity.HasIndex(x => new { x.OwnerId, x.Status, x.UpdatedAt });
        entity.HasIndex(x => new { x.Status, x.ScheduleDispatchedAt });
    }
}
