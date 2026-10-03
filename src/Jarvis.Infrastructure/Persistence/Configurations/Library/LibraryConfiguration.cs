using Jarvis.Application.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Library;

internal sealed class LibraryItemConfiguration : IEntityTypeConfiguration<LibraryItemEntity>
{
    public void Configure(EntityTypeBuilder<LibraryItemEntity> builder)
    {
        builder.ToTable("library_items", table =>
        {
            table.HasCheckConstraint("ck_library_items_kind", "kind IN ('web', 'note', 'report')");
            table.HasCheckConstraint("ck_library_items_origin", "origin IN ('app', 'chat', 'research')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(12).IsRequired();
        builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(2_100);
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(LibraryRules.MaxTitleLength).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(LibraryRules.MaxSummaryLength)
            .IsRequired();
        builder.Property(x => x.KeyPoints).HasColumnName("key_points").HasColumnType("text[]").IsRequired();
        builder.Property(x => x.Tags).HasColumnName("tags").HasColumnType("text[]").IsRequired();
        builder.Property(x => x.Content).HasColumnName("content").HasColumnType("text").IsRequired();
        builder.Property(x => x.Origin).HasColumnName("origin").HasMaxLength(12).IsRequired();
        builder.Property(x => x.ProjectId).HasColumnName("project_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        builder.HasIndex(x => new { x.OwnerId, x.Url });
    }
}

internal sealed class FlashcardConfiguration : IEntityTypeConfiguration<FlashcardEntity>
{
    public void Configure(EntityTypeBuilder<FlashcardEntity> builder)
    {
        builder.ToTable("flashcards");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.ItemId).HasColumnName("item_id");
        builder.Property(x => x.Front).HasColumnName("front").HasMaxLength(LibraryRules.MaxCardTextLength).IsRequired();
        builder.Property(x => x.Back).HasColumnName("back").HasMaxLength(LibraryRules.MaxCardTextLength).IsRequired();
        builder.Property(x => x.Ease).HasColumnName("ease");
        builder.Property(x => x.IntervalDays).HasColumnName("interval_days");
        builder.Property(x => x.Repetitions).HasColumnName("repetitions");
        builder.Property(x => x.Lapses).HasColumnName("lapses");
        builder.Property(x => x.DueOn).HasColumnName("due_on");
        builder.Property(x => x.LastReviewedOn).HasColumnName("last_reviewed_on");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.OwnerId, x.DueOn });
        builder.HasIndex(x => x.ItemId);
    }
}
