using Jarvis.Application.Routines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Routines;

internal sealed class RoutineSuggestionConfiguration : IEntityTypeConfiguration<RoutineSuggestionEntity>
{
    public void Configure(EntityTypeBuilder<RoutineSuggestionEntity> builder)
    {
        builder.ToTable("routine_suggestions", table =>
            table.HasCheckConstraint("ck_routine_suggestions_status",
                $"status IN ('{RoutineStatuses.Pending}', '{RoutineStatuses.Accepted}', '{RoutineStatuses.Dismissed}')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Evidence).HasColumnName("evidence").HasMaxLength(600).IsRequired();
        builder.Property(x => x.Confidence).HasColumnName("confidence");
        builder.Property(x => x.DefinitionJson).HasColumnName("definition_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.AutomationId).HasColumnName("automation_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.Fingerprint }).IsUnique()
            .HasDatabaseName("ux_routine_suggestions_owner_fingerprint");
        builder.HasIndex(x => new { x.OwnerId, x.Status });
    }
}
