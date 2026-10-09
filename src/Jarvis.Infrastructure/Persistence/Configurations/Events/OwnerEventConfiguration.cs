using Jarvis.Application.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Events;

internal sealed class OwnerEventConfiguration : IEntityTypeConfiguration<OwnerEventEntity>
{
    public void Configure(EntityTypeBuilder<OwnerEventEntity> builder)
    {
        builder.ToTable("owner_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(60).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(JarvisEventLimits.MaxSummaryLength)
            .IsRequired();
        builder.Property(x => x.SubjectType).HasColumnName("subject_type").HasMaxLength(40);
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.DataJson).HasColumnName("data").HasColumnType("jsonb");
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
        builder.Property(x => x.Origin).HasColumnName("origin").HasMaxLength(20).IsRequired();
        builder.Property(x => x.CausedByTaskId).HasColumnName("caused_by_task_id");
        builder.Property(x => x.At).HasColumnName("at");
        builder.HasIndex(x => new { x.OwnerId, x.At });
        builder.HasIndex(x => new { x.OwnerId, x.SubjectType, x.SubjectId });
    }
}

internal sealed class EntityLinkConfiguration : IEntityTypeConfiguration<EntityLinkEntity>
{
    public void Configure(EntityTypeBuilder<EntityLinkEntity> builder)
    {
        builder.ToTable("entity_links");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.FromType).HasColumnName("from_type").HasMaxLength(40).IsRequired();
        builder.Property(x => x.FromId).HasColumnName("from_id");
        builder.Property(x => x.ToType).HasColumnName("to_type").HasMaxLength(40).IsRequired();
        builder.Property(x => x.ToId).HasColumnName("to_id");
        builder.Property(x => x.Relation).HasColumnName("relation").HasMaxLength(LinkRelations.MaxLength).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => new { x.OwnerId, x.FromType, x.FromId, x.ToType, x.ToId, x.Relation }).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.ToType, x.ToId });
    }
}
