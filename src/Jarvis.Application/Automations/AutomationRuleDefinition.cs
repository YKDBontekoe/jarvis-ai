using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Domain.Automations;

namespace Jarvis.Application.Automations;

public sealed record AutomationRuleDefinition(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("trigger")] AutomationTriggerDefinition Trigger,
    [property: JsonPropertyName("conditions")] IReadOnlyList<AutomationConditionDefinition>? Conditions,
    [property: JsonPropertyName("actions")] IReadOnlyList<AutomationActionDefinition> Actions,
    [property: JsonPropertyName("limits")] AutomationLimitsDefinition? Limits);

public sealed record AutomationLimitsDefinition(
    [property: JsonPropertyName("maxActionsPerRun")] int? MaxActionsPerRun,
    [property: JsonPropertyName("maxRunDurationMinutes")] int? MaxRunDurationMinutes,
    [property: JsonPropertyName("cooldownMinutes")] int? CooldownMinutes);

public abstract record AutomationTriggerDefinition
{
    [JsonPropertyName("kind")]
    public abstract string Kind { get; }
}

public sealed record ScheduleTriggerDefinition(
    [property: JsonPropertyName("localTime")] TimeOnly LocalTime,
    [property: JsonPropertyName("timeZoneId")] string TimeZoneId,
    [property: JsonPropertyName("weekdays")] int? Weekdays) : AutomationTriggerDefinition
{
    public override string Kind => AutomationTriggerKinds.Schedule;
}

public sealed record ManualTriggerDefinition : AutomationTriggerDefinition
{
    public override string Kind => AutomationTriggerKinds.Manual;
}

public sealed record ReminderDueTriggerDefinition(
    [property: JsonPropertyName("reminderId")] Guid? ReminderId) : AutomationTriggerDefinition
{
    public override string Kind => AutomationTriggerKinds.ReminderDue;
}

public sealed record PublicJsonThresholdTriggerDefinition(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("jsonPath")] string JsonPath,
    [property: JsonPropertyName("comparison")] string Comparison,
    [property: JsonPropertyName("threshold")] double Threshold,
    [property: JsonPropertyName("intervalMinutes")] int IntervalMinutes,
    [property: JsonPropertyName("credentialProvider")] string? CredentialProvider) : AutomationTriggerDefinition
{
    public override string Kind => AutomationTriggerKinds.PublicJsonThreshold;
}

public sealed record DeviceBatteryTriggerDefinition(
    [property: JsonPropertyName("comparison")] string Comparison,
    [property: JsonPropertyName("thresholdPercent")] double ThresholdPercent,
    [property: JsonPropertyName("intervalMinutes")] int IntervalMinutes) : AutomationTriggerDefinition
{
    public override string Kind => AutomationTriggerKinds.DeviceBattery;
}

public sealed record DeviceLocationTriggerDefinition(
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("radiusMeters")] double RadiusMeters,
    [property: JsonPropertyName("comparison")] string Comparison,
    [property: JsonPropertyName("intervalMinutes")] int IntervalMinutes) : AutomationTriggerDefinition
{
    public override string Kind => AutomationTriggerKinds.DeviceLocation;
}

public sealed record CalendarWindowTriggerDefinition(
    [property: JsonPropertyName("minutesBefore")] int MinutesBefore,
    [property: JsonPropertyName("intervalMinutes")] int IntervalMinutes) : AutomationTriggerDefinition
{
    public override string Kind => AutomationTriggerKinds.CalendarWindow;
}

/// <summary>
/// Fires when something happens inside Jarvis or arrives from outside, for example a WhatsApp message, an uploaded
/// file, a finished task, or a webhook call. <see cref="Contains"/> and <see cref="Source"/> narrow it down.
/// </summary>
public sealed record EventTriggerDefinition(
    [property: JsonPropertyName("eventKind")] string EventKind,
    [property: JsonPropertyName("contains")] string? Contains,
    [property: JsonPropertyName("source")] string? Source) : AutomationTriggerDefinition
{
    public override string Kind => AutomationTriggerKinds.Event;
}

public abstract record AutomationConditionDefinition
{
    [JsonPropertyName("kind")]
    public abstract string Kind { get; }
}

public sealed record TimeWindowConditionDefinition(
    [property: JsonPropertyName("startLocalTime")] TimeOnly StartLocalTime,
    [property: JsonPropertyName("endLocalTime")] TimeOnly EndLocalTime,
    [property: JsonPropertyName("timeZoneId")] string TimeZoneId) : AutomationConditionDefinition
{
    public override string Kind => "time_window";
}

public sealed record JsonThresholdConditionDefinition(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("jsonPath")] string JsonPath,
    [property: JsonPropertyName("comparison")] string Comparison,
    [property: JsonPropertyName("threshold")] double Threshold,
    [property: JsonPropertyName("credentialProvider")] string? CredentialProvider) : AutomationConditionDefinition
{
    public override string Kind => "public_json_threshold";
}

public abstract record AutomationActionDefinition
{
    [JsonPropertyName("kind")]
    public abstract string Kind { get; }

    /// <summary>Run this action only when the condition holds, so one automation can branch on the event.</summary>
    [JsonPropertyName("if")]
    public AutomationActionCondition? If { get; init; }
}

/// <summary>
/// Compares a field of the triggering event to a value. <c>Field</c> is event.title, event.detail, event.source
/// or event.kind; <c>Op</c> is contains, not_contains, equals or not_equals (ignoring case).
/// </summary>
public sealed record AutomationActionCondition(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("value")] string Value);

public sealed record NotificationActionDefinition(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body) : AutomationActionDefinition
{
    public override string Kind => AutomationActionKinds.Notification;
}

public sealed record TaskActionDefinition(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("prompt")] string Prompt) : AutomationActionDefinition
{
    public override string Kind => AutomationActionKinds.Task;
}

public sealed record ChannelMessageActionDefinition(
    [property: JsonPropertyName("connectionId")] Guid ConnectionId,
    [property: JsonPropertyName("recipient")] string Recipient,
    [property: JsonPropertyName("body")] string Body) : AutomationActionDefinition
{
    public override string Kind => AutomationActionKinds.ChannelMessage;
}

public sealed record AgentRunActionDefinition(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("prompt")] string Prompt) : AutomationActionDefinition
{
    public override string Kind => AutomationActionKinds.AgentRun;
}

/// <summary>Switches Jarvis to a context mode (focus, sleep, …), or back to automatic with "auto".</summary>
public sealed record SetModeActionDefinition(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("minutes")] int? Minutes) : AutomationActionDefinition
{
    public override string Kind => AutomationActionKinds.SetMode;
}

public static class AutomationDefinitionJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new AutomationTriggerJsonConverter(), new AutomationConditionJsonConverter(),
            new AutomationActionJsonConverter() }
    };

    public static string Serialize(AutomationRuleDefinition definition) =>
        JsonSerializer.Serialize(definition, Options);

    public static AutomationRuleDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<AutomationRuleDefinition>(json, Options)
        ?? throw new JsonException("Automation definition was empty.");

    public static AutomationRuleDefinition Parse(string json)
    {
        var definition = Deserialize(json);
        AutomationRuleValidator.Validate(definition);
        return definition;
    }
}

internal sealed class AutomationTriggerJsonConverter : JsonConverter<AutomationTriggerDefinition>
{
    public override AutomationTriggerDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var kind = document.RootElement.GetProperty("kind").GetString();
        return kind switch
        {
            AutomationTriggerKinds.Schedule => document.Deserialize<ScheduleTriggerDefinition>(options),
            AutomationTriggerKinds.Manual => new ManualTriggerDefinition(),
            AutomationTriggerKinds.ReminderDue => document.Deserialize<ReminderDueTriggerDefinition>(options),
            AutomationTriggerKinds.PublicJsonThreshold => document.Deserialize<PublicJsonThresholdTriggerDefinition>(options),
            AutomationTriggerKinds.DeviceBattery => document.Deserialize<DeviceBatteryTriggerDefinition>(options),
            AutomationTriggerKinds.DeviceLocation => document.Deserialize<DeviceLocationTriggerDefinition>(options),
            AutomationTriggerKinds.CalendarWindow => document.Deserialize<CalendarWindowTriggerDefinition>(options),
            AutomationTriggerKinds.Event => document.Deserialize<EventTriggerDefinition>(options),
            _ => throw new JsonException($"Unknown automation trigger kind '{kind}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, AutomationTriggerDefinition value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}

internal sealed class AutomationConditionJsonConverter : JsonConverter<AutomationConditionDefinition>
{
    public override AutomationConditionDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var kind = document.RootElement.GetProperty("kind").GetString();
        return kind switch
        {
            "time_window" => document.Deserialize<TimeWindowConditionDefinition>(options),
            "public_json_threshold" => document.Deserialize<JsonThresholdConditionDefinition>(options),
            _ => throw new JsonException($"Unknown automation condition kind '{kind}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, AutomationConditionDefinition value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}

internal sealed class AutomationActionJsonConverter : JsonConverter<AutomationActionDefinition>
{
    public override AutomationActionDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var kind = document.RootElement.GetProperty("kind").GetString();
        return kind switch
        {
            AutomationActionKinds.Notification => document.Deserialize<NotificationActionDefinition>(options),
            AutomationActionKinds.Task => document.Deserialize<TaskActionDefinition>(options),
            AutomationActionKinds.ChannelMessage => document.Deserialize<ChannelMessageActionDefinition>(options),
            AutomationActionKinds.AgentRun => document.Deserialize<AgentRunActionDefinition>(options),
            AutomationActionKinds.SetMode => document.Deserialize<SetModeActionDefinition>(options),
            _ => throw new JsonException($"Unknown automation action kind '{kind}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, AutomationActionDefinition value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
