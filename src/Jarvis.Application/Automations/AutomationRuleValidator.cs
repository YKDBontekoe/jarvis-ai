using System.Text.RegularExpressions;
using Jarvis.Domain.Automations;

namespace Jarvis.Application.Automations;

public static partial class AutomationRuleValidator
{
    public static void Validate(AutomationRuleDefinition definition)
    {
        if (definition.SchemaVersion != AutomationSchema.CurrentVersion)
            throw new ArgumentException($"Unsupported automation schema version {definition.SchemaVersion}.");
        if (definition.Actions.Count is < 1 or > AutomationSchema.MaxActionsPerRule)
            throw new ArgumentException($"Automations need 1 to {AutomationSchema.MaxActionsPerRule} actions.");
        var conditions = definition.Conditions ?? [];
        if (conditions.Count > AutomationSchema.MaxConditionsPerRule)
            throw new ArgumentException($"Automations support at most {AutomationSchema.MaxConditionsPerRule} conditions.");

        ValidateTrigger(definition.Trigger);
        foreach (var condition in conditions) ValidateCondition(condition);
        foreach (var action in definition.Actions) ValidateAction(action);

        var limits = definition.Limits;
        if (limits?.MaxActionsPerRun is < 1 or > AutomationSchema.MaxActionsPerRule)
            throw new ArgumentException("maxActionsPerRun is out of range.");
        if (limits?.MaxRunDurationMinutes is < 1 or > 120)
            throw new ArgumentException("maxRunDurationMinutes must be between 1 and 120.");
        if (limits?.CooldownMinutes is < 0 or > 24 * 60)
            throw new ArgumentException("cooldownMinutes is out of range.");
    }

    public static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new ArgumentException("Automation name must contain 1 to 200 characters.");
    }

    private static void ValidateTrigger(AutomationTriggerDefinition trigger)
    {
        switch (trigger)
        {
            case ScheduleTriggerDefinition schedule:
                _ = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
                if (schedule.Weekdays is < 0 or > 127)
                    throw new ArgumentException("weekdays must be a valid bitmask.");
                break;
            case PublicJsonThresholdTriggerDefinition json:
                ValidateJsonWatch(json.Url, json.JsonPath, json.Comparison, json.Threshold, json.IntervalMinutes,
                    json.CredentialProvider);
                break;
            case DeviceBatteryTriggerDefinition battery:
                ValidateInterval(battery.IntervalMinutes);
                ValidateComparison(battery.Comparison);
                if (!double.IsFinite(battery.ThresholdPercent) || battery.ThresholdPercent is < 0 or > 100)
                    throw new ArgumentException("Battery threshold must be between 0 and 100.");
                break;
            case DeviceLocationTriggerDefinition location:
                ValidateInterval(location.IntervalMinutes);
                ValidateComparison(location.Comparison);
                if (location.Latitude is < -90 or > 90 || location.Longitude is < -180 or > 180)
                    throw new ArgumentException("Location coordinates are out of range.");
                if (location.RadiusMeters is < 25 or > 50_000)
                    throw new ArgumentException("Location radius must be between 25 and 50,000 meters.");
                break;
            case CalendarWindowTriggerDefinition calendar:
                ValidateInterval(calendar.IntervalMinutes);
                if (calendar.MinutesBefore is < 1 or > 24 * 60)
                    throw new ArgumentException("minutesBefore is out of range.");
                break;
            case ManualTriggerDefinition:
            case ReminderDueTriggerDefinition:
                break;
            default:
                throw new ArgumentException($"Unknown trigger kind '{trigger.Kind}'.");
        }
    }

    private static void ValidateCondition(AutomationConditionDefinition condition)
    {
        switch (condition)
        {
            case TimeWindowConditionDefinition window:
                _ = TimeZoneInfo.FindSystemTimeZoneById(window.TimeZoneId);
                break;
            case JsonThresholdConditionDefinition json:
                ValidateJsonWatch(json.Url, json.JsonPath, json.Comparison, json.Threshold, 15, json.CredentialProvider);
                break;
            default:
                throw new ArgumentException($"Unknown condition kind '{condition.Kind}'.");
        }
    }

    private static void ValidateAction(AutomationActionDefinition action)
    {
        switch (action)
        {
            case NotificationActionDefinition notification:
                ValidateShortText(notification.Title, 200, "Notification title");
                ValidateShortText(notification.Body, 2_000, "Notification body");
                break;
            case TaskActionDefinition task:
                ValidateShortText(task.Title, 200, "Task title");
                ValidateShortText(task.Prompt, 32_000, "Task prompt");
                break;
            case ChannelMessageActionDefinition channel:
                if (channel.ConnectionId == Guid.Empty)
                    throw new ArgumentException("channel_message needs a connectionId.");
                ValidateShortText(channel.Recipient, 120, "Recipient");
                ValidateShortText(channel.Body, 4_000, "Message body");
                break;
            case AgentRunActionDefinition agent:
                ValidateShortText(agent.Title, 200, "Agent run title");
                ValidateShortText(agent.Prompt, 32_000, "Agent run prompt");
                break;
            default:
                throw new ArgumentException($"Unknown action kind '{action.Kind}'.");
        }
    }

    internal static void ValidateJsonWatch(string url, string jsonPath, string comparison, double threshold,
        int intervalMinutes, string? credentialProvider)
    {
        ValidateInterval(intervalMinutes);
        ValidateComparison(comparison);
        if (!double.IsFinite(threshold))
            throw new ArgumentException("Threshold must be a finite number.");
        ValidatePublicHttpsUrl(url);
        ValidateJsonPath(jsonPath);
        if (!string.IsNullOrWhiteSpace(credentialProvider) && credentialProvider.Length > 80)
            throw new ArgumentException("credentialProvider is too long.");
    }

    private static void ValidateInterval(int intervalMinutes)
    {
        if (intervalMinutes is < 5 or > 1440)
            throw new ArgumentException("Check interval must be between 5 minutes and 24 hours.");
    }

    private static void ValidateComparison(string comparison)
    {
        if (comparison is not ("below" or "above"))
            throw new ArgumentException("Comparison must be 'below' or 'above'.");
    }

    private static void ValidateShortText(string? value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max)
            throw new ArgumentException($"{label} must contain 1 to {max} characters.");
    }

    private static void ValidateJsonPath(string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || jsonPath.Length > 512 ||
            jsonPath.Split('.').Any(segment => segment.Length is < 1 or > 64 ||
                segment.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))))
            throw new ArgumentException("JSON path must contain simple object-property names separated by dots.");
    }

    internal static string ValidatePublicHttpsUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || uri.Port != 443 ||
            System.Net.IPAddress.TryParse(uri.Host, out _) || !uri.Host.Contains('.', StringComparison.Ordinal))
            throw new ArgumentException("URL must be a credential-free HTTPS URL on a public DNS hostname.");

        var host = uri.IdnHost.TrimEnd('.');
        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Local and private hostnames are not allowed.");

        if (uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Uri.UnescapeDataString(part.Split('=', 2)[0]))
            .Any(IsSensitiveQueryName))
            throw new ArgumentException("URLs cannot contain API-key, token, password, or credential query parameters.");

        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
    }

    private static bool IsSensitiveQueryName(string name) =>
        SensitiveQueryName().IsMatch(name);

    [GeneratedRegex("(?i)(api[_-]?key|token|secret|password|credential|auth|signature)")]
    private static partial Regex SensitiveQueryName();
}
