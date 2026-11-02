using Jarvis.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Conversations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
            builder.ToTable("messages");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.Role).HasMaxLength(20).IsRequired();
            builder.Property(x => x.Content).IsRequired();
            builder.Property(x => x.CitationsJson).HasColumnName("citations_json").HasColumnType("jsonb");
            builder.Property(x => x.AttachmentsJson).HasColumnName("attachments_json").HasColumnType("jsonb");
            builder.Property(x => x.ClientMessageId).HasColumnName("client_message_id").HasMaxLength(64);
            builder.Property(x => x.ClientPayloadHash).HasColumnName("client_payload_hash").HasMaxLength(64);
            builder.HasIndex(x => new { x.ConversationId, x.CreatedAt, x.Id });
            // One send, one message: the same client id can never be stored twice in a conversation.
            builder.HasIndex(x => new { x.ConversationId, x.ClientMessageId }).IsUnique()
                .HasFilter("client_message_id IS NOT NULL");
            // Searching what was said ("where did we talk about the boiler?") is an ILIKE on the content.
            builder.HasIndex(x => x.Content).HasMethod("gin").HasOperators("gin_trgm_ops");
        }
}
