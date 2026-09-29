using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Files;

internal sealed class FileContentChunkEntityConfiguration : IEntityTypeConfiguration<FileContentChunkEntity>
{
    public void Configure(EntityTypeBuilder<FileContentChunkEntity> builder)
    {
            builder.ToTable("file_content_chunks");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.FileId).HasColumnName("file_id");
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
            builder.Property(x => x.Content).HasColumnName("content").IsRequired();
            builder.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1536)");
            builder.Property(x => x.SearchText).HasColumnName("search_text").HasColumnType("tsvector")
                .HasComputedColumnSql("to_tsvector('simple'::regconfig, content)", stored: true);
            builder.HasOne<StoredFileEntity>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => new { x.OwnerId, x.FileId, x.ChunkIndex }).IsUnique();
            builder.HasIndex(x => x.SearchText).HasMethod("gin");
            builder.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        }
}
