using System.Collections.Concurrent;
using System.Text.Json;
using Jarvis.Application.Devices;
using Jarvis.Api.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Devices;

public sealed class SignalRDeviceInvoker(IHubContext<JarvisEventsHub> hub) : IDeviceInvoker
{
    private readonly ConcurrentDictionary<string, (Guid OwnerId, DevicePresence Presence)> _connections = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<string>> _pending = new();

    public IReadOnlyList<DevicePresence> List(Guid ownerId) =>
        _connections.Values.Where(item => item.OwnerId == ownerId).Select(item => item.Presence).ToArray();

    public void Register(Guid ownerId, string connectionId, string name, IReadOnlyList<string> capabilities)
    {
        var allowed = capabilities.Where(DeviceCapabilities.IsValid).Distinct(StringComparer.Ordinal).ToArray();
        _connections[connectionId] = (ownerId, new DevicePresence(connectionId,
            string.IsNullOrWhiteSpace(name) ? "Device" : name.Trim()[..Math.Min(name.Trim().Length, 80)],
            allowed, DateTimeOffset.UtcNow));
    }

    public void Unregister(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }

    public async Task<string> InvokeAsync(Guid ownerId, string capability, JsonElement? arguments, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (List(ownerId).All(device => !device.Capabilities.Contains(capability, StringComparer.Ordinal)))
            return $"No connected device allows {capability}.";

        var invokeId = Guid.CreateVersion7();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[invokeId] = completion;
        try
        {
            await hub.Clients.Group(JarvisEventsHub.OwnerGroupName(ownerId)).SendAsync("device.invoke", new
            {
                invokeId,
                capability,
                arguments,
                timeoutSeconds = (int)timeout.TotalSeconds
            }, cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            await using var registration = timeoutCts.Token.Register(() =>
                completion.TrySetResult($"The device did not respond to {capability} in time."));
            return await completion.Task;
        }
        finally
        {
            _pending.TryRemove(invokeId, out _);
        }
    }

    public bool Complete(Guid invokeId, string? result, string? error)
    {
        if (!_pending.TryGetValue(invokeId, out var completion)) return false;
        var text = !string.IsNullOrWhiteSpace(error) ? error.Trim()
            : string.IsNullOrWhiteSpace(result) ? "The device returned an empty result."
            : result.Trim();
        if (text.Length > 4_000) text = text[..4_000];
        return completion.TrySetResult(text);
    }
}

public sealed class SignalRRealtimePublisher(IHubContext<JarvisEventsHub> hub) : Jarvis.Application.Realtime.IRealtimePublisher
{
    public Task PublishToConversationAsync(Guid conversationId, string eventName, object payload,
        CancellationToken cancellationToken) =>
        hub.Clients.Group(JarvisEventsHub.GroupName(conversationId)).SendAsync(eventName, payload, cancellationToken);

    public Task PublishToOwnerAsync(Guid ownerId, string eventName, object payload,
        CancellationToken cancellationToken) =>
        hub.Clients.Group(JarvisEventsHub.OwnerGroupName(ownerId)).SendAsync(eventName, payload, cancellationToken);
}
