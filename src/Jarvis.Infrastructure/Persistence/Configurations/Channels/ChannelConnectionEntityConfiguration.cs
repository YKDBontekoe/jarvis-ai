using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Channels;

internal sealed class ChannelConnectionEntityConfiguration : IEntityTypeConfiguration<ChannelConnectionEntity>
{
    public void Configure(EntityTypeBuilder<ChannelConnectionEntity> builder)
    {
            builder.ToTable("channel_connections");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.OwnerId).HasColumnName("owner_id");
            builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
            builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
            builder.Property(x => x.Account).HasColumnName("account").HasMaxLength(80).IsRequired();
            builder.Property(x => x.Enabled).HasColumnName("enabled");
            builder.Property(x => x.AllowedSendersJson).HasColumnName("allowed_senders").HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.ForwardNotifications).HasColumnName("forward_notifications");
            builder.Property(x => x.NotificationCategoriesJson).HasColumnName("notification_categories").HasColumnType("jsonb");
            builder.Property(x => x.NotifyRecipient).HasColumnName("notify_recipient").HasMaxLength(40);
            builder.Property(x => x.WebhookKey).HasColumnName("webhook_key").HasMaxLength(64).IsRequired();
            builder.Property(x => x.LastInboundAt).HasColumnName("last_inbound_at");
            builder.Property(x => x.LastOutboundAt).HasColumnName("last_outbound_at");
            builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
            builder.Property(x => x.NotificationsForwardedUntil).HasColumnName("notifications_forwarded_until");
            builder.Property(x => x.CreatedAt).HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            builder.HasIndex(x => x.WebhookKey).IsUnique();
            builder.HasIndex(x => new { x.Kind, x.Account }).IsUnique();
            builder.HasIndex(x => x.OwnerId);
        }
}
