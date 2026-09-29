using Jarvis.Domain.Conversations;
using Jarvis.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Files;

internal sealed class ConversationFileAttachmentEntityConfiguration : IEntityTypeConfiguration<ConversationFileAttachmentEntity>
{
    public void Configure(EntityTypeBuilder<ConversationFileAttachmentEntity> entity)
    {
        entity.ToTable("conversation_file_attachments");
        entity.HasKey(x => new { x.ConversationId, x.FileId });
        entity.Property(x => x.OwnerId).HasColumnName("owner_id");
        entity.Property(x => x.AttachedAt).HasColumnName("attached_at");
        entity.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<StoredFileEntity>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConversationCollectionAttachmentEntityConfiguration
    : IEntityTypeConfiguration<ConversationCollectionAttachmentEntity>
{
    public void Configure(EntityTypeBuilder<ConversationCollectionAttachmentEntity> entity)
    {
        entity.ToTable("conversation_collection_attachments");
        entity.HasKey(x => new { x.ConversationId, x.CollectionId });
        entity.Property(x => x.OwnerId).HasColumnName("owner_id");
        entity.Property(x => x.AttachedAt).HasColumnName("attached_at");
        entity.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<DocumentCollection>().WithMany().HasForeignKey(x => x.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
