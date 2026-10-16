using Jarvis.Api.Devices;
using Jarvis.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class DeviceInvokerTests
{
    private readonly Guid _owner = Guid.CreateVersion7();

    private static SignalRDeviceInvoker Invoker(Action<object?[]> onSend)
    {
        var proxy = Fake<IClientProxy>.Create(("SendCoreAsync", args =>
        {
            onSend(args);
            return Task.CompletedTask;
        }));
        var clients = Fake<IHubClients>.Create(("Group", _ => proxy));
        return new SignalRDeviceInvoker(Fake<IHubContext<JarvisEventsHub>>.Create(("get_Clients", _ => clients)));
    }

    private static Guid InvokeIdOf(object?[] sendArgs)
    {
        var payload = ((object?[])sendArgs[1]!)[0]!;
        return (Guid)payload.GetType().GetProperty("invokeId")!.GetValue(payload)!;
    }

    [Fact]
    public async Task Only_the_owner_whose_device_was_asked_can_answer_the_call()
    {
        Guid? invokeId = null;
        var invoker = Invoker(args => invokeId = InvokeIdOf(args));
        invoker.Register(_owner, "conn-1", "Phone", [Jarvis.Application.Devices.DeviceCapabilities.Battery]);

        var pending = invoker.InvokeAsync(_owner, Jarvis.Application.Devices.DeviceCapabilities.Battery, null,
            TimeSpan.FromSeconds(30), CancellationToken.None);
        while (invokeId is null) await Task.Delay(5);

        Assert.False(invoker.Complete(Guid.CreateVersion7(), invokeId.Value, "Battery 1%", null));
        Assert.False(invoker.Complete(_owner, Guid.CreateVersion7(), "Battery 100%", null));
        Assert.False(pending.IsCompleted);
        Assert.True(invoker.Complete(_owner, invokeId.Value, "Battery 82%", null));
        Assert.Equal("Battery 82%", await pending);
        Assert.False(invoker.Complete(_owner, invokeId.Value, "again", null));
    }
}
