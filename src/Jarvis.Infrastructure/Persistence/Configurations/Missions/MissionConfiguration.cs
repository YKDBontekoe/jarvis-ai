using Jarvis.Application.Missions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Missions;

internal sealed class MissionConfiguration : IEntityTypeConfiguration<MissionEntity>
{
    public void Configure(EntityTypeBuilder<MissionEntity> builder)
    {
        builder.ToTable("missions", table => table.HasCheckConstraint("ck_missions_status",
            "status IN ('ready', 'running', 'paused', 'completed', 'failed', 'cancelled')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(MissionRules.MaxTitleLength).IsRequired();
        builder.Property(x => x.Goal).HasColumnName("goal").HasMaxLength(MissionRules.MaxGoalLength).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(12).IsRequired();
        builder.Property(x => x.ProjectId).HasColumnName("project_id");
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(MissionRules.MaxResultLength);
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        builder.HasIndex(x => x.Status);
    }
}

internal sealed class MissionStepConfiguration : IEntityTypeConfiguration<MissionStepEntity>
{
    public void Configure(EntityTypeBuilder<MissionStepEntity> builder)
    {
        builder.ToTable("mission_steps", table => table.HasCheckConstraint("ck_mission_steps_status",
            "status IN ('pending', 'running', 'completed', 'failed', 'skipped', 'cancelled')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MissionId).HasColumnName("mission_id");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Ordinal).HasColumnName("ordinal");
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(MissionRules.MaxStepTitleLength).IsRequired();
        builder.Property(x => x.Instruction).HasColumnName("instruction")
            .HasMaxLength(MissionRules.MaxInstructionLength).IsRequired();
        builder.Property(x => x.DependsOn).HasColumnName("depends_on").HasColumnType("text[]").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(12).IsRequired();
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.Result).HasColumnName("result").HasMaxLength(MissionRules.MaxResultLength);
        builder.Property(x => x.Error).HasColumnName("error").HasMaxLength(500);
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.HasIndex(x => new { x.MissionId, x.Ordinal });
        builder.HasIndex(x => x.TaskId);
    }
}

internal sealed class MissionNoteConfiguration : IEntityTypeConfiguration<MissionNoteEntity>
{
    public void Configure(EntityTypeBuilder<MissionNoteEntity> builder)
    {
        builder.ToTable("mission_notes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MissionId).HasColumnName("mission_id");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(MissionRules.MaxNoteKeyLength).IsRequired();
        builder.Property(x => x.Value).HasColumnName("value").HasMaxLength(MissionRules.MaxNoteValueLength).IsRequired();
        builder.Property(x => x.StepKey).HasColumnName("step_key").HasMaxLength(20);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.MissionId, x.Key }).IsUnique();
    }
}
