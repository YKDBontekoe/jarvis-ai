namespace Jarvis.Domain.Workflows;

public static class WatchKinds
{
    public const string PublicJson = "public_json";
    public const string AuthenticatedJson = "authenticated_json";
    public const string DeviceBattery = "device_battery";
    public const string DeviceLocation = "device_location";
    public const string Calendar = "calendar";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        PublicJson, AuthenticatedJson, DeviceBattery, DeviceLocation, Calendar
    };

    public static bool IsJson(string? kind) =>
        kind is PublicJson or AuthenticatedJson or null or "";

    public static bool RequiresUrl(string? kind) => IsJson(kind);

    public static string Normalize(string? kind)
    {
        var value = string.IsNullOrWhiteSpace(kind) ? PublicJson : kind.Trim().ToLowerInvariant();
        return All.Contains(value) ? value : PublicJson;
    }
}
