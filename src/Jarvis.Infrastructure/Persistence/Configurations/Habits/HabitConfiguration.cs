using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Habits;

internal sealed class HabitConfiguration : IEntityTypeConfiguration<HabitEntity>
{
    public void Configure(EntityTypeBuilder<HabitEntity> builder)
    {
        builder.ToTable("habits", table =>
        {
            table.HasCheckConstraint("ck_habits_cadence", "cadence IN ('daily', 'weekly')");
            table.HasCheckConstraint("ck_habits_target_per_week", "target_per_week BETWEEN 1 AND 7");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(60).IsRequired();
        builder.Property(x => x.Icon).HasColumnName("icon").HasMaxLength(16);
        builder.Property(x => x.Cadence).HasColumnName("cadence").HasMaxLength(10).IsRequired();
        builder.Property(x => x.TargetPerWeek).HasColumnName("target_per_week");
        builder.Property(x => x.ArchivedAt).HasColumnName("archived_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.ArchivedAt });
        builder.HasMany(x => x.CheckIns).WithOne().HasForeignKey(x => x.HabitId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class HabitCheckInConfiguration : IEntityTypeConfiguration<HabitCheckInEntity>
{
    public void Configure(EntityTypeBuilder<HabitCheckInEntity> builder)
    {
        builder.ToTable("habit_check_ins");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.HabitId).HasColumnName("habit_id");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Date).HasColumnName("check_in_date");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(10).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.HabitId, x.Date }).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.Date });
    }
}
