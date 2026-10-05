using Jarvis.Application.Decisions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Decisions;

internal sealed class DecisionConfiguration : IEntityTypeConfiguration<DecisionEntity>
{
    public void Configure(EntityTypeBuilder<DecisionEntity> builder)
    {
        builder.ToTable("decisions", table =>
            table.HasCheckConstraint("ck_decisions_probability", "probability BETWEEN 0.01 AND 0.99"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(DecisionRules.MaxTitleLength).IsRequired();
        builder.Property(x => x.Context).HasColumnName("context").HasMaxLength(DecisionRules.MaxContextLength);
        builder.Property(x => x.Prediction).HasColumnName("prediction").HasMaxLength(DecisionRules.MaxPredictionLength)
            .IsRequired();
        builder.Property(x => x.Probability).HasColumnName("probability");
        builder.Property(x => x.ReviewOn).HasColumnName("review_on").HasColumnType("date");
        builder.Property(x => x.Outcome).HasColumnName("outcome");
        builder.Property(x => x.OutcomeNote).HasColumnName("outcome_note").HasMaxLength(DecisionRules.MaxNoteLength);
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(x => x.ReminderId).HasColumnName("reminder_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.ReviewOn });
        builder.HasIndex(x => new { x.OwnerId, x.ResolvedAt });
    }
}
