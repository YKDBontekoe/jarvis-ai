namespace Jarvis.Application.Automations;

public interface IAutomationMetrics
{
    Task<double?> ReadJsonMetricAsync(Guid ownerId, string url, string jsonPath, string kind,
        string? credentialProvider, CancellationToken cancellationToken);

    Task<double?> ReadBatteryPercentAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<(double Latitude, double Longitude)?> ReadLocationAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<double?> ReadMinutesUntilNextCalendarEventAsync(Guid ownerId, CancellationToken cancellationToken);
}
