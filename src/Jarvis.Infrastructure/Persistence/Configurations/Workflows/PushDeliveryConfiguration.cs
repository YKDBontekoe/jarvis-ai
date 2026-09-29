using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Workflows;

internal sealed class PushDeliveryConfiguration : IEntityTypeConfiguration<PushDelivery>
{
    public void Configure(EntityTypeBuilder<PushDelivery> builder)
    {
            builder.ToTable("push_deliveries");
            builder.HasKey(x => new { x.NotificationId, x.DeviceId });
            builder.Property(x => x.NotificationId).HasColumnName("notification_id");
            builder.Property(x => x.DeviceId).HasColumnName("device_id");
            builder.Property(x => x.Attempts).HasColumnName("attempts");
            builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
            builder.Property(x => x.LeaseUntil).HasColumnName("lease_until");
            builder.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
            builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
            builder.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<PushDevice>().WithMany().HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => new { x.DeliveredAt, x.NextAttemptAt, x.LeaseUntil });
        }
}
