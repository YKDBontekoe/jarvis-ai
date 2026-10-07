using Jarvis.Application.Improvements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Learning;

internal sealed class ImprovementProposalConfiguration : IEntityTypeConfiguration<ImprovementProposalEntity>
{
    public void Configure(EntityTypeBuilder<ImprovementProposalEntity> builder)
    {
        builder.ToTable("improvement_proposals", table =>
        {
            table.HasCheckConstraint("ck_improvement_proposals_status",
                $"status IN ('{ImprovementStatuses.Pending}', '{ImprovementStatuses.Accepted}', " +
                $"'{ImprovementStatuses.Applied}', '{ImprovementStatuses.Dismissed}', '{ImprovementStatuses.Undone}')");
            table.HasCheckConstraint("ck_improvement_proposals_kind",
                $"kind IN ('{ImprovementKinds.Memory}', '{ImprovementKinds.Skill}', '{ImprovementKinds.Review}')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Evidence).HasColumnName("evidence").HasMaxLength(600).IsRequired();
        builder.Property(x => x.Confidence).HasColumnName("confidence");
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ResultingRef).HasColumnName("resulting_ref");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.Fingerprint }).IsUnique()
            .HasDatabaseName("ux_improvement_proposals_owner_fingerprint");
        builder.HasIndex(x => new { x.OwnerId, x.Status });
    }
}
