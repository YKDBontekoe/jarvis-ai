using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Memory;

internal sealed class GraphRelationEntityConfiguration : IEntityTypeConfiguration<GraphRelationEntity>
{
    public void Configure(EntityTypeBuilder<GraphRelationEntity> builder)
    {
            builder.ToTable("graph_relations");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.SubjectId).HasColumnName("subject_id");
            builder.Property(x => x.Predicate).HasColumnName("predicate").HasMaxLength(60).IsRequired();
            builder.Property(x => x.ObjectId).HasColumnName("object_id");
            builder.Property(x => x.ObjectValue).HasColumnName("object_value").HasMaxLength(300);
            builder.Property(x => x.ValidFrom).HasColumnName("valid_from");
            builder.Property(x => x.ValidTo).HasColumnName("valid_to");
            builder.Property(x => x.Confidence).HasColumnName("confidence");
            builder.Property(x => x.SourceMemoryId).HasColumnName("source_memory_id");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.HasOne<GraphEntityEntity>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<GraphEntityEntity>().WithMany().HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<MemoryEntity>().WithMany().HasForeignKey(x => x.SourceMemoryId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => new { x.OwnerId, x.SubjectId, x.Predicate, x.ValidTo });
            builder.HasIndex(x => new { x.OwnerId, x.ObjectId });
        }
}
