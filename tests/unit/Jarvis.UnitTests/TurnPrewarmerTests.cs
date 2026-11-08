using Jarvis.Api.Conversations;
using Jarvis.Api.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>The app may ask to warm up on every burst of typing; only the first ask in a short window does work.</summary>
public sealed class TurnPrewarmerTests
{
    [Fact]
    public void Repeated_requests_from_one_owner_are_ignored_for_a_while_but_other_owners_are_not()
    {
        var services = new ServiceCollection().AddScoped<OwnerExecutionContext>().BuildServiceProvider();
        var prewarmer = new TurnPrewarmer(services.GetRequiredService<IServiceScopeFactory>(), new NoChatClient(),
            new Lifetime(), NullLogger<TurnPrewarmer>.Instance);
        var owner = Guid.NewGuid();

        Assert.True(prewarmer.Request(owner));
        Assert.False(prewarmer.Request(owner));
        Assert.True(prewarmer.Request(Guid.NewGuid()));
    }

    private sealed class NoChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
