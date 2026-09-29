namespace Jarvis.Domain.Automations;

public static class AutomationSchema
{
    public const int CurrentVersion = 1;
    public const int MaxActionsPerRule = 8;
    public const int MaxConditionsPerRule = 8;
    public const int DefaultMaxActionsPerRun = 5;
    public const int DefaultMaxRunDurationMinutes = 15;
    public const int DefaultCooldownMinutes = 5;
    public const int MaxConcurrentRunsPerOwner = 3;
    public const int MaxRunHistoryPerRule = 200;
}

public static class AutomationTriggerKinds
{
    public const string Schedule = "schedule";
    public const string Manual = "manual";
    public const string ReminderDue = "reminder_due";
    public const string CalendarWindow = "calendar_window";
    public const string DeviceBattery = "device_battery";
    public const string DeviceLocation = "device_location";
    public const string PublicJsonThreshold = "public_json_threshold";

    public static bool IsPolling(string kind) => kind is DeviceBattery or DeviceLocation
        or PublicJsonThreshold or CalendarWindow;
}

public static class AutomationActionKinds
{
    public const string Notification = "notification";
    public const string Task = "task";
    public const string ChannelMessage = "channel_message";
    public const string AgentRun = "agent_run";
}
