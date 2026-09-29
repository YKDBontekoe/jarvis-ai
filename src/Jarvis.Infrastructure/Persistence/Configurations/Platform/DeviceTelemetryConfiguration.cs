using Jarvis.Domain.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jarvis.Infrastructure.Persistence.Configurations.Platform;

internal sealed class DeviceTelemetryConfiguration : IEntityTypeConfiguration<DeviceTelemetry>
{
    public void Configure(EntityTypeBuilder<DeviceTelemetry> builder)
    {
            builder.ToTable("device_telemetry");
            builder.HasKey(x => x.OwnerId);
            builder.Property(x => x.OwnerId).HasColumnName("owner_id").ValueGeneratedNever();
            builder.Property(x => x.Latitude).HasColumnName("latitude");
            builder.Property(x => x.Longitude).HasColumnName("longitude");
            builder.Property(x => x.AccuracyMeters).HasColumnName("accuracy_meters");
            builder.Property(x => x.BatteryPercent).HasColumnName("battery_percent");
            builder.Property(x => x.Charging).HasColumnName("charging");
            builder.Property(x => x.ReportedAt).HasColumnName("reported_at");
        }
}
