using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Memory;

internal sealed class GraphEntityEntityConfiguration : IEntityTypeConfiguration<GraphEntityEntity>
{
    public void Configure(EntityTypeBuilder<GraphEntityEntity> builder)
    {
            builder.ToTable("graph_entities");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(120).IsRequired();
            builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(30).IsRequired();
            builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500);
            builder.Property(x => x.AliasesJson).HasColumnName("aliases").HasMaxLength(2_000).IsRequired();
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasIndex(x => new { x.OwnerId, x.Key }).IsUnique();
            builder.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
        }
}
