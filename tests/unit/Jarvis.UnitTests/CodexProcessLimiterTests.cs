using Jarvis.Agents;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CodexProcessLimiterTests
{
    [Fact]
    public async Task Caps_concurrent_waits_at_two()
    {
        using var limiter = new CodexProcessLimiter();
        await limiter.WaitAsync(CancellationToken.None);
        await limiter.WaitAsync(CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.WaitAsync(timeout.Token));

        limiter.Release();
        await limiter.WaitAsync(CancellationToken.None);
        limiter.Release();
        limiter.Release();
    }
}
