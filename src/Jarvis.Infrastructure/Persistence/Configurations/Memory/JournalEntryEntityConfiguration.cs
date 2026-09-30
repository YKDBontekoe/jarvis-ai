using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Memory;

internal sealed class JournalEntryEntityConfiguration : IEntityTypeConfiguration<JournalEntryEntity>
{
    public void Configure(EntityTypeBuilder<JournalEntryEntity> builder)
    {
        builder.ToTable("journal_entries", table =>
        {
            table.HasCheckConstraint("ck_journal_entries_rating", "rating IS NULL OR rating BETWEEN 1 AND 10");
            table.HasCheckConstraint("ck_journal_entries_mood", "mood IS NULL OR mood BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_journal_entries_energy", "energy IS NULL OR energy BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_journal_entries_stress", "stress IS NULL OR stress BETWEEN 1 AND 5");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.EntryDate).HasColumnName("entry_date");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.Highlights).HasColumnName("highlights");
        builder.Property(x => x.Gratitude).HasColumnName("gratitude");
        builder.Property(x => x.Rating).HasColumnName("rating");
        builder.Property(x => x.Mood).HasColumnName("mood");
        builder.Property(x => x.Energy).HasColumnName("energy");
        builder.Property(x => x.Stress).HasColumnName("stress");
        builder.Property(x => x.Tags).HasColumnName("tags").HasColumnType("text[]").IsRequired();
        builder.Property(x => x.MemoryId).HasColumnName("memory_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.EntryDate });
    }
}
