using Jarvis.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Channels;

internal sealed class ChannelThreadEntityConfiguration : IEntityTypeConfiguration<ChannelThreadEntity>
{
    public void Configure(EntityTypeBuilder<ChannelThreadEntity> builder)
    {
            builder.ToTable("channel_threads");
            builder.HasKey(x => new { x.ConnectionId, x.Peer });
            builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
            builder.Property(x => x.Peer).HasColumnName("peer").HasMaxLength(80);
            builder.Property(x => x.ConversationId).HasColumnName("conversation_id");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasOne<ChannelConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        }
}
