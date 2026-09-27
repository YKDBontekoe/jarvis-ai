using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Jarvis.Application.Conversations;
using Jarvis.Application.Devices;
using Jarvis.Application.Settings;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Devices;

internal sealed class DeviceAgentTools(
    IDeviceInvoker devices,
    IOwnerSettingsStore settings,
    ICurrentUser currentUser)
{
    [Description("List phones and computers currently connected as Jarvis device nodes, with the capabilities they allow.")]
    public Task<string> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var list = devices.List(currentUser.OwnerId);
        if (list.Count == 0) return Task.FromResult("No device is connected right now. Open the Jarvis app on a phone or computer.");
        var builder = new StringBuilder();
        foreach (var device in list)
            builder.Append("- ").Append(device.Name).Append(": ").AppendLine(string.Join(", ", device.Capabilities));
        return Task.FromResult(builder.ToString());
    }

    [Description("Ask a connected device for its current location. Requires the user to allow location on that device.")]
    public Task<string> GetDeviceLocationAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(DeviceCapabilities.Location, null, TimeSpan.FromSeconds(25), cancellationToken);

    [Description("Read the current clipboard text from a connected device. Only use this when the user asks you to look at what they copied.")]
    public Task<string> ReadDeviceClipboardAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(DeviceCapabilities.Clipboard, null, TimeSpan.FromSeconds(15), cancellationToken);

    [Description("Open a URL on a connected device in the user's default browser.")]
    public Task<string> OpenUrlOnDeviceAsync(
        [Description("The http(s) URL to open.")] string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("https" or "http"))
            return Task.FromResult("Provide an http(s) URL to open.");
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { url = parsed.ToString() }));
        return InvokeAsync(DeviceCapabilities.OpenUrl, args.RootElement.Clone(), TimeSpan.FromSeconds(15),
            cancellationToken);
    }

    [Description("Show a short notification on a connected device.")]
    public Task<string> NotifyDeviceAsync(
        [Description("Notification title.")] string title,
        [Description("Notification body.")] string body,
        CancellationToken cancellationToken = default)
    {
        var safeTitle = (title ?? string.Empty).Trim();
        var safeBody = (body ?? string.Empty).Trim();
        if (safeTitle.Length is 0 or > 80) return Task.FromResult("Give the notification a title of 1 to 80 characters.");
        if (safeBody.Length > 280) return Task.FromResult("Keep the notification body under 280 characters.");
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new { title = safeTitle, body = safeBody }));
        return InvokeAsync(DeviceCapabilities.Notify, args.RootElement.Clone(), TimeSpan.FromSeconds(10),
            cancellationToken);
    }

    [Description("Read battery level from a connected device.")]
    public Task<string> GetDeviceBatteryAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(DeviceCapabilities.Battery, null, TimeSpan.FromSeconds(10), cancellationToken);

    private async Task<string> InvokeAsync(string capability, JsonElement? arguments, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var allowed = await settings.GetAsync<DeviceSettings>(currentUser.OwnerId, SettingsSections.Devices,
            cancellationToken) ?? DeviceSettings.Default;
        if (!allowed.Allows(capability))
            return $"The user turned off {capability} on their devices. They can enable it under Settings → Devices.";
        if (devices.List(currentUser.OwnerId).Count == 0)
            return "No device is connected right now. Open the Jarvis app.";
        return await devices.InvokeAsync(currentUser.OwnerId, capability, arguments, timeout, cancellationToken);
    }
}

internal sealed class DeviceToolContributor(
    IDeviceInvoker devices,
    IOwnerSettingsStore settings,
    ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new DeviceAgentTools(devices, settings, currentUser);
        yield return AIFunctionFactory.Create(tools.ListDevicesAsync);
        yield return AIFunctionFactory.Create(tools.GetDeviceBatteryAsync);
        yield return AIFunctionFactory.Create(tools.NotifyDeviceAsync);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(tools.GetDeviceLocationAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(tools.ReadDeviceClipboardAsync));
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(tools.OpenUrlOnDeviceAsync));
    }
}

internal sealed class DeviceContextContributor(IDeviceInvoker devices) : IAgentContextContributor
{
    public int Order => 50;

    public IEnumerable<AIContextProvider> CreateProviders(AgentBuildContext context) =>
        [new DeviceContextProvider(devices, context.OwnerId)];
}

internal sealed class DeviceContextProvider(IDeviceInvoker devices, Guid ownerId) : MessageAIContextProvider
{
    internal const string Prefix = "Connected devices";

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var list = devices.List(ownerId);
        var builder = new StringBuilder();
        builder.Append(Prefix).AppendLine(" (phones and computers running the Jarvis app):");
        if (list.Count == 0) builder.AppendLine("- none online");
        foreach (var device in list)
            builder.Append("- ").Append(device.Name).Append(" can ").AppendLine(string.Join(", ", device.Capabilities));
        builder.Append("Use device tools only when the user wants something on a physical device. Location, clipboard, and opening URLs need approval.");
        return new ValueTask<IEnumerable<ChatMessage>>([new ChatMessage(ChatRole.User, builder.ToString())]);
    }
}
