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
            builder.HasIndex(x => new { x.ConversationId, x.CreatedAt, x.Id });
        }
}
