using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Game;

internal sealed class GameBetConfiguration : IEntityTypeConfiguration<GameBetEntity>
{
    public void Configure(EntityTypeBuilder<GameBetEntity> builder)
    {
        builder.ToTable("game_bets", table =>
            table.HasCheckConstraint("ck_game_bets_confidence", "confidence BETWEEN 0 AND 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SubjectKey).HasColumnName("subject_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.Claim).HasColumnName("claim").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Evidence).HasColumnName("evidence").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Confidence).HasColumnName("confidence");
        builder.Property(x => x.Points).HasColumnName("points");
        builder.Property(x => x.StartsOn).HasColumnName("starts_on").HasColumnType("date");
        builder.Property(x => x.EndsOn).HasColumnName("ends_on").HasColumnType("date");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        // One open bet per subject.
        builder.HasIndex(x => new { x.OwnerId, x.SubjectKey }).IsUnique().HasFilter("status = 'open'");
    }
}

internal sealed class GameQuestConfiguration : IEntityTypeConfiguration<GameQuestEntity>
{
    public void Configure(EntityTypeBuilder<GameQuestEntity> builder)
    {
        builder.ToTable("game_quests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.WeekStart).HasColumnName("week_start").HasColumnType("date");
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SubjectKey).HasColumnName("subject_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Target).HasColumnName("target");
        builder.Property(x => x.Points).HasColumnName("points");
        builder.Property(x => x.IsBoss).HasColumnName("is_boss");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        // A week's quests are written once; the same subject never twice in one week.
        builder.HasIndex(x => new { x.OwnerId, x.WeekStart, x.SubjectKey }).IsUnique();
    }
}
