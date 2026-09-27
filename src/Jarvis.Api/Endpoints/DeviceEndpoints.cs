using Jarvis.Application.Conversations;
using Jarvis.Application.Devices;
using Jarvis.Application.Settings;

namespace Jarvis.Api.Endpoints;

public sealed record DeviceSettingsDto(bool Location, bool Battery, bool Clipboard, bool OpenUrl, bool Notify,
    IReadOnlyList<DevicePresenceDto> Online);
public sealed record DevicePresenceDto(string Name, IReadOnlyList<string> Capabilities, DateTimeOffset LastSeenAt);
public sealed record DeviceInvokeResultRequest(string? Result, string? Error);

internal static class DeviceEndpoints
{
    public static RouteGroupBuilder MapDeviceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/settings/devices", async (IOwnerSettingsStore settings, IDeviceInvoker devices,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var saved = await settings.GetAsync<DeviceSettings>(currentUser.OwnerId, SettingsSections.Devices, ct)
                        ?? DeviceSettings.Default;
            return Results.Ok(ToDto(saved, devices.List(currentUser.OwnerId)));
        }).WithName("GetDeviceSettings");

        api.MapPut("/settings/devices", async (DeviceSettings request, IOwnerSettingsStore settings,
            IDeviceInvoker devices, ICurrentUser currentUser, CancellationToken ct) =>
        {
            await settings.SaveAsync(currentUser.OwnerId, SettingsSections.Devices, request, ct);
            return Results.Ok(ToDto(request, devices.List(currentUser.OwnerId)));
        }).WithName("SaveDeviceSettings");

        api.MapPost("/devices/invoke/{invokeId:guid}/result", (Guid invokeId, DeviceInvokeResultRequest request,
            IDeviceInvoker devices) =>
            devices.Complete(invokeId, request.Result, request.Error)
                ? Results.NoContent()
                : Results.NotFound()).WithName("CompleteDeviceInvoke");

        return api;
    }

    private static DeviceSettingsDto ToDto(DeviceSettings settings, IReadOnlyList<DevicePresence> online) =>
        new(settings.Location, settings.Battery, settings.Clipboard, settings.OpenUrl, settings.Notify,
            online.Select(device => new DevicePresenceDto(device.Name, device.Capabilities, device.LastSeenAt)).ToArray());
}
