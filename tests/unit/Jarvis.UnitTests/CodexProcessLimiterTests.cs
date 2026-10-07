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

    [Fact]
    public async Task Background_work_leaves_the_last_slot_for_chat()
    {
        using var limiter = new CodexProcessLimiter(2);
        await limiter.WaitAsync(background: true, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.WaitAsync(background: true, timeout.Token));

        await limiter.WaitAsync(background: false, CancellationToken.None);
        limiter.Release(background: false);
        limiter.Release(background: true);
    }

    [Fact]
    public async Task Cancelled_background_wait_does_not_leak_its_lane()
    {
        using var limiter = new CodexProcessLimiter(2);
        await limiter.WaitAsync(background: false, CancellationToken.None);
        await limiter.WaitAsync(background: false, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.WaitAsync(background: true, timeout.Token));
        limiter.Release(background: false);

        using var next = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await limiter.WaitAsync(background: true, next.Token);
        limiter.Release(background: true);
        limiter.Release(background: false);
    }

    [Fact]
    public async Task A_single_slot_is_shared_by_background_and_chat()
    {
        using var limiter = new CodexProcessLimiter(1);
        await limiter.WaitAsync(background: true, CancellationToken.None);
        limiter.Release(background: true);
        await limiter.WaitAsync(background: false, CancellationToken.None);
        limiter.Release(background: false);
    }
}
