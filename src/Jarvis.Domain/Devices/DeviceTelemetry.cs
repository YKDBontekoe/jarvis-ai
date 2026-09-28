namespace Jarvis.Domain.Devices;

/// <summary>Latest location and battery snapshot posted by a connected Jarvis app.</summary>
public sealed class DeviceTelemetry
{
    public Guid OwnerId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public int? BatteryPercent { get; set; }
    public bool? Charging { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
}
