using System.Text.Json;

namespace Jarvis.Application.Devices;

public static class DeviceCapabilities
{
    public const string Location = "location";
    public const string Battery = "battery";
    public const string Clipboard = "clipboard";
    public const string OpenUrl = "open_url";
    public const string Notify = "notify";

    public static readonly IReadOnlyList<string> All =
        [Location, Battery, Clipboard, OpenUrl, Notify];

    public static bool IsValid(string? capability) =>
        capability is Location or Battery or Clipboard or OpenUrl or Notify;
}

public sealed record DeviceSettings(
    bool Location = false,
    bool Battery = true,
    bool Clipboard = false,
    bool OpenUrl = true,
    bool Notify = true)
{
    public static DeviceSettings Default { get; } = new();

    public bool Allows(string capability) => capability switch
    {
        DeviceCapabilities.Location => Location,
        DeviceCapabilities.Battery => Battery,
        DeviceCapabilities.Clipboard => Clipboard,
        DeviceCapabilities.OpenUrl => OpenUrl,
        DeviceCapabilities.Notify => Notify,
        _ => false
    };
}

public sealed record DevicePresence(string ConnectionId, string Name, IReadOnlyList<string> Capabilities,
    DateTimeOffset LastSeenAt);

public sealed record DeviceInvokeRequest(Guid InvokeId, string Capability, JsonElement? Arguments, TimeSpan Timeout);

public sealed record DeviceTelemetryRecord(Guid OwnerId, double? Latitude, double? Longitude, double? AccuracyMeters,
    int? BatteryPercent, bool? Charging, DateTimeOffset ReportedAt);

public sealed record SaveDeviceTelemetryRequest(double? Latitude, double? Longitude, double? AccuracyMeters,
    int? BatteryPercent, bool? Charging);

public interface IDeviceTelemetryStore
{
    Task<DeviceTelemetryRecord> SaveAsync(Guid ownerId, SaveDeviceTelemetryRequest request,
        CancellationToken cancellationToken);
    Task<DeviceTelemetryRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken);
}

public interface IDeviceInvoker
{
    IReadOnlyList<DevicePresence> List(Guid ownerId);
    void Register(Guid ownerId, string connectionId, string name, IReadOnlyList<string> capabilities);
    void Unregister(string connectionId);
    Task<string> InvokeAsync(Guid ownerId, string capability, JsonElement? arguments, TimeSpan timeout,
        CancellationToken cancellationToken);
    bool Complete(Guid invokeId, string? result, string? error);
}

public sealed class NoOpDeviceInvoker : IDeviceInvoker
{
    public IReadOnlyList<DevicePresence> List(Guid ownerId) => [];
    public void Register(Guid ownerId, string connectionId, string name, IReadOnlyList<string> capabilities) { }
    public void Unregister(string connectionId) { }
    public Task<string> InvokeAsync(Guid ownerId, string capability, JsonElement? arguments, TimeSpan timeout,
        CancellationToken cancellationToken) =>
        Task.FromResult("No device is connected to handle this request.");
    public bool Complete(Guid invokeId, string? result, string? error) => false;
}
