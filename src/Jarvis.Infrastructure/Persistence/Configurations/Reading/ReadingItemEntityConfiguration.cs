using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Reading;

internal sealed class ReadingItemEntityConfiguration : IEntityTypeConfiguration<ReadingItemEntity>
{
    public void Configure(EntityTypeBuilder<ReadingItemEntity> builder)
    {
        builder.ToTable("reading_items", table =>
        {
            table.HasCheckConstraint("ck_reading_items_status", "status IN ('pending', 'ready', 'failed')");
            table.HasCheckConstraint("ck_reading_items_source", "source IN ('app', 'chat')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(2_000).IsRequired();
        builder.Property(x => x.UrlKey).HasColumnName("url_key").HasMaxLength(2_000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300);
        builder.Property(x => x.SiteName).HasColumnName("site_name").HasMaxLength(120);
        builder.Property(x => x.Excerpt).HasColumnName("excerpt").HasMaxLength(400);
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1_200);
        builder.Property(x => x.KeyPoints).HasColumnName("key_points").HasColumnType("text[]").IsRequired();
        builder.Property(x => x.WordCount).HasColumnName("word_count");
        builder.Property(x => x.ReadingMinutes).HasColumnName("reading_minutes");
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(200);
        builder.Property(x => x.FetchAttempts).HasColumnName("fetch_attempts");
        builder.Property(x => x.NextFetchAt).HasColumnName("next_fetch_at");
        builder.Property(x => x.FetchedAt).HasColumnName("fetched_at");
        builder.Property(x => x.ReadAt).HasColumnName("read_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.UrlKey }).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        builder.HasIndex(x => new { x.Status, x.NextFetchAt });
    }
}
