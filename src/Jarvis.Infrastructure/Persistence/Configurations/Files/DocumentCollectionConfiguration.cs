using Jarvis.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Files;

internal sealed class DocumentCollectionConfiguration : IEntityTypeConfiguration<DocumentCollection>
{
    public void Configure(EntityTypeBuilder<DocumentCollection> builder)
    {
        builder.ToTable("document_collections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(DocumentCollection.MaxNameLength).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description")
            .HasMaxLength(DocumentCollection.MaxDescriptionLength);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
    }
}

internal sealed class DocumentCollectionFileEntityConfiguration : IEntityTypeConfiguration<DocumentCollectionFileEntity>
{
    public void Configure(EntityTypeBuilder<DocumentCollectionFileEntity> builder)
    {
        builder.ToTable("document_collection_files");
        builder.HasKey(x => new { x.CollectionId, x.FileId });
        builder.Property(x => x.CollectionId).HasColumnName("collection_id");
        builder.Property(x => x.FileId).HasColumnName("file_id");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.HasOne<DocumentCollection>().WithMany().HasForeignKey(x => x.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StoredFileEntity>().WithMany().HasForeignKey(x => x.FileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OwnerId, x.FileId });
    }
}
