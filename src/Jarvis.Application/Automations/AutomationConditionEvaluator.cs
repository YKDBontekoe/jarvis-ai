using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;

namespace Jarvis.Application.Automations;

public sealed class AutomationConditionEvaluator(IAutomationMetrics metrics, TimeProvider timeProvider)
{
    public async Task<bool> EvaluateAllAsync(Guid ownerId, IReadOnlyList<AutomationConditionDefinition>? conditions,
        CancellationToken cancellationToken)
    {
        if (conditions is null || conditions.Count == 0) return true;
        foreach (var condition in conditions)
        {
            if (!await EvaluateAsync(ownerId, condition, cancellationToken)) return false;
        }
        return true;
    }

    public async Task<bool> EvaluateAsync(Guid ownerId, AutomationConditionDefinition condition,
        CancellationToken cancellationToken)
    {
        switch (condition)
        {
            case TimeWindowConditionDefinition window:
                return AutomationScheduleClock.IsWithinTimeWindow(window, timeProvider.GetUtcNow());
            case JsonThresholdConditionDefinition json:
                return await EvaluateJsonAsync(ownerId, json, cancellationToken);
            default:
                return false;
        }
    }

    public async Task<bool> EvaluatePollingTriggerAsync(Guid ownerId, AutomationTriggerDefinition trigger,
        CancellationToken cancellationToken)
    {
        switch (trigger)
        {
            case PublicJsonThresholdTriggerDefinition json:
                return await EvaluateJsonValueAsync(ownerId, json.Url, json.JsonPath, json.Comparison,
                    json.Threshold, json.CredentialProvider, cancellationToken);
            case DeviceBatteryTriggerDefinition battery:
            {
                var value = await metrics.ReadBatteryPercentAsync(ownerId, cancellationToken);
                if (value is null) return false;
                return MatchesComparison(battery.Comparison, value.Value, battery.ThresholdPercent);
            }
            case DeviceLocationTriggerDefinition location:
            {
                var snapshot = await metrics.ReadLocationAsync(ownerId, cancellationToken);
                if (snapshot is not (var lat, var lon)) return false;
                var distance = GeoDistance.Meters(lat, lon, location.Latitude, location.Longitude);
                return location.Comparison switch
                {
                    "below" => distance <= location.RadiusMeters,
                    "above" => distance > location.RadiusMeters,
                    _ => false
                };
            }
            case CalendarWindowTriggerDefinition calendar:
            {
                var minutes = await metrics.ReadMinutesUntilNextCalendarEventAsync(ownerId, cancellationToken);
                if (minutes is null) return false;
                return minutes.Value <= calendar.MinutesBefore;
            }
            default:
                return true;
        }
    }

    private async Task<bool> EvaluateJsonAsync(Guid ownerId, JsonThresholdConditionDefinition json,
        CancellationToken cancellationToken)
    {
        return await EvaluateJsonValueAsync(ownerId, json.Url, json.JsonPath, json.Comparison, json.Threshold,
            json.CredentialProvider, cancellationToken);
    }

    private async Task<bool> EvaluateJsonValueAsync(Guid ownerId, string url, string jsonPath, string comparison,
        double threshold, string? credentialProvider, CancellationToken cancellationToken)
    {
        var kind = string.IsNullOrWhiteSpace(credentialProvider)
            ? WatchKinds.PublicJson
            : WatchKinds.AuthenticatedJson;
        var value = await metrics.ReadJsonMetricAsync(ownerId, url, jsonPath, kind, credentialProvider,
            cancellationToken);
        if (value is null) return false;
        return MatchesComparison(comparison, value.Value, threshold);
    }

    private static bool MatchesComparison(string comparison, double value, double threshold) => comparison switch
    {
        "below" => value <= threshold,
        "above" => value >= threshold,
        _ => false
    };

}
