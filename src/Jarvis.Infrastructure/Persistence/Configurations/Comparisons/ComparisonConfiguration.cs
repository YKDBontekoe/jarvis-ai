using Jarvis.Application.Comparisons;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Comparisons;

internal sealed class ComparisonConfiguration : IEntityTypeConfiguration<ComparisonEntity>
{
    public void Configure(EntityTypeBuilder<ComparisonEntity> builder)
    {
        builder.ToTable("comparisons");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(ComparisonRules.MaxTitleLength).IsRequired();
        builder.Property(x => x.ProjectId).HasColumnName("project_id");
        builder.Property(x => x.LibraryItemId).HasColumnName("library_item_id");
        builder.Property(x => x.DefinitionJson).HasColumnName("definition").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
    }
}
