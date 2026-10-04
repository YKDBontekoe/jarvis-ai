using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.WhatsApp;

internal sealed class WhatsAppChatConfiguration : IEntityTypeConfiguration<WhatsAppChatEntity>
{
    public void Configure(EntityTypeBuilder<WhatsAppChatEntity> builder)
    {
        builder.ToTable("whatsapp_chats");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.ChatId).HasColumnName("chat_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.IsGroup).HasColumnName("is_group");
        builder.Property(x => x.ReadAlong).HasColumnName("read_along");
        builder.Property(x => x.AutoReminders).HasColumnName("auto_reminders");
        builder.Property(x => x.LastMessageAt).HasColumnName("last_message_at");
        builder.Property(x => x.LastReceivedAt).HasColumnName("last_received_at");
        builder.Property(x => x.ReadThrough).HasColumnName("read_through");
        builder.Property(x => x.ScannedThrough).HasColumnName("scanned_through");
        builder.Property(x => x.ScanLeaseUntil).HasColumnName("scan_lease_until");
        builder.Property(x => x.AskConversationId).HasColumnName("ask_conversation_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne<ChannelConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ConnectionId, x.ChatId }).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.LastMessageAt });
    }
}

internal sealed class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessageEntity>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessageEntity> builder)
    {
        builder.ToTable("whatsapp_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.ChatId).HasColumnName("chat_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.FromMe).HasColumnName("from_me");
        builder.Property(x => x.Sender).HasColumnName("sender").HasMaxLength(80);
        builder.Property(x => x.SenderId).HasColumnName("sender_id").HasMaxLength(100);
        builder.Property(x => x.Text).HasColumnName("text").IsRequired();
        builder.Property(x => x.MediaJson).HasColumnName("media").HasColumnType("jsonb");
        builder.Property(x => x.SentAt).HasColumnName("sent_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasOne<ChannelConnectionEntity>().WithMany().HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ConnectionId, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.ConnectionId, x.ChatId, x.SentAt });
        builder.HasIndex(x => new { x.OwnerId, x.SentAt });
    }
}

internal sealed class WhatsAppMessageMediaConfiguration : IEntityTypeConfiguration<WhatsAppMessageMediaEntity>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessageMediaEntity> builder)
    {
        builder.ToTable("whatsapp_message_media");
        builder.HasKey(x => x.MessageId);
        builder.Property(x => x.MessageId).HasColumnName("message_id").ValueGeneratedNever();
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Content).HasColumnName("content").HasColumnType("bytea").IsRequired();
        builder.HasOne<WhatsAppMessageEntity>().WithMany().HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.OwnerId);
    }
}
