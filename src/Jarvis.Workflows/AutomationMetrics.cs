using Jarvis.Application.Automations;
using Jarvis.Application.Devices;
using Jarvis.Application.Integrations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;

namespace Jarvis.Workflows;

public sealed class AutomationMetrics(
    PublicJsonMetricReader json,
    IIntegrationCredentialStore credentials,
    IDeviceTelemetryStore telemetry,
    ICalendarFeed calendar) : IAutomationMetrics
{
    public async Task<double?> ReadJsonMetricAsync(Guid ownerId, string url, string jsonPath, string kind,
        string? credentialProvider, CancellationToken cancellationToken)
    {
        try
        {
            var value = kind == WatchKinds.AuthenticatedJson
                ? await json.ReadAsync(url, jsonPath,
                    await AuthorizationHeaderAsync(ownerId, credentialProvider, cancellationToken), cancellationToken)
                : await json.ReadAsync(url, jsonPath, cancellationToken);
            return value;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<double?> ReadBatteryPercentAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var snapshot = await telemetry.GetAsync(ownerId, cancellationToken);
        if (snapshot is null || snapshot.ReportedAt < DateTimeOffset.UtcNow.AddHours(-6)) return null;
        return snapshot.BatteryPercent;
    }

    public async Task<(double Latitude, double Longitude)?> ReadLocationAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var snapshot = await telemetry.GetAsync(ownerId, cancellationToken);
        if (snapshot?.Latitude is null || snapshot.Longitude is null) return null;
        return (snapshot.Latitude.Value, snapshot.Longitude.Value);
    }

    public async Task<double?> ReadMinutesUntilNextCalendarEventAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var events = await calendar.ListUpcomingAsync(ownerId, now, now.AddDays(2), cancellationToken);
        var next = events.OrderBy(x => x.StartAt).FirstOrDefault();
        if (next is null) return null;
        return Math.Max(0, (next.StartAt - now).TotalMinutes);
    }

    private async Task<string?> AuthorizationHeaderAsync(Guid ownerId, string? provider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        var secrets = await credentials.GetSecretsAsync(ownerId, provider, cancellationToken);
        var token = secrets?.GetValueOrDefault("token") ?? secrets?.GetValueOrDefault("access_token");
        if (string.IsNullOrWhiteSpace(token)) return null;
        return token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : "Bearer " + token;
    }
}
