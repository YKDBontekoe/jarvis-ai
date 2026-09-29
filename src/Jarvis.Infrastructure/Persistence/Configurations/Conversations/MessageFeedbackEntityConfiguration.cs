using Jarvis.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Conversations;

internal sealed class MessageFeedbackEntityConfiguration : IEntityTypeConfiguration<MessageFeedbackEntity>
{
    public void Configure(EntityTypeBuilder<MessageFeedbackEntity> builder)
    {
            builder.ToTable("message_feedback");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
            builder.Property(x => x.MessageId).HasColumnName("message_id");
            builder.Property(x => x.Rating).HasColumnName("rating").HasMaxLength(10).IsRequired();
            builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(1_000);
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            builder.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => new { x.OwnerId, x.MessageId }).IsUnique();
            builder.HasIndex(x => new { x.OwnerId, x.ProcessedAt });
        }
}
