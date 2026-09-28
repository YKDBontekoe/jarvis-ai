using Jarvis.Api.Conversations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class RemoteQueryHostTests
{
    [Fact]
    public void Closing_the_caller_does_not_cancel_the_query()
    {
        using var phoneClosed = new CancellationTokenSource();
        using var stopping = new CancellationTokenSource();
        var host = new RemoteQueryHost();
        var owner = Guid.CreateVersion7();
        var conversation = Guid.CreateVersion7();

        using var query = host.Begin(owner, conversation, stopping.Token);
        phoneClosed.Cancel();

        Assert.False(query.Token.IsCancellationRequested);
        Assert.True(host.IsResponding(conversation));
    }

    [Fact]
    public void Explicit_cancel_and_shutdown_stop_the_query()
    {
        using var stopping = new CancellationTokenSource();
        var host = new RemoteQueryHost();
        var owner = Guid.CreateVersion7();
        var conversation = Guid.CreateVersion7();
        using var query = host.Begin(owner, conversation, stopping.Token);

        Assert.False(host.TryCancel(Guid.CreateVersion7(), conversation));
        Assert.False(query.Token.IsCancellationRequested);

        Assert.True(host.TryCancel(owner, conversation));
        Assert.True(query.Token.IsCancellationRequested);

        using var other = host.Begin(owner, Guid.CreateVersion7(), stopping.Token);
        stopping.Cancel();
        Assert.True(other.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task The_query_stops_being_responding_when_it_finishes()
    {
        using var stopping = new CancellationTokenSource();
        var host = new RemoteQueryHost();
        var conversation = Guid.CreateVersion7();
        var query = host.Begin(Guid.CreateVersion7(), conversation, stopping.Token, TimeSpan.FromMilliseconds(80));
        Assert.True(host.IsResponding(conversation));

        var timeout = Task.Delay(TimeSpan.FromSeconds(2));
        var cancelled = await Task.WhenAny(timeout, WaitForCancelAsync(query.Token));
        Assert.True(query.Token.IsCancellationRequested);
        Assert.NotSame(timeout, cancelled);

        query.Dispose();
        Assert.False(host.IsResponding(conversation));
        Assert.False(host.TryCancel(Guid.CreateVersion7(), conversation));
    }

    private static Task WaitForCancelAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Task.CompletedTask;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => done.TrySetResult());
        return done.Task;
    }
}
