using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Memory;

internal sealed class MemoryEntityConfiguration : IEntityTypeConfiguration<MemoryEntity>
{
    public void Configure(EntityTypeBuilder<MemoryEntity> builder)
    {
            builder.ToTable("memories");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40).IsRequired();
            builder.Property(x => x.Content).HasColumnName("content").IsRequired();
            builder.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1536)");
            builder.Property(x => x.EmbeddingModel).HasColumnName("embedding_model").HasMaxLength(200);
            builder.Property(x => x.GraphIndexedAt).HasColumnName("graph_indexed_at");
            builder.Property(x => x.Importance).HasColumnName("importance");
            builder.Property(x => x.Confidence).HasColumnName("confidence");
            builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(60);
            builder.Property(x => x.SourceId).HasColumnName("source_id");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.Property(x => x.ValidUntil).HasColumnName("valid_until");
            builder.Property(x => x.IsPinned).HasColumnName("is_pinned");
            builder.Property(x => x.SearchVector).HasColumnName("search_vector").HasColumnType("tsvector")
                .HasComputedColumnSql("to_tsvector('simple'::regconfig, content)", stored: true);
            builder.HasIndex(x => new { x.OwnerId, x.Kind });
            builder.HasIndex(x => x.SearchVector).HasMethod("gin");
            builder.HasIndex(x => x.Content).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        }
}
