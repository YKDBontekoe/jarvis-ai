namespace Jarvis.Agents;

/// <summary>
/// Caps concurrent Codex CLI processes across chat completions and coding tasks. Background work (memory extraction,
/// reranking, triage) can hold at most all but one slot, so an interactive turn never queues behind it.
/// </summary>
public sealed class CodexProcessLimiter : IDisposable
{
    public const int MaxConcurrentProcesses = 2;

    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim? _backgroundSlots;

    public CodexProcessLimiter(int maxConcurrentProcesses = MaxConcurrentProcesses)
    {
        if (maxConcurrentProcesses < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentProcesses));
        _slots = new SemaphoreSlim(maxConcurrentProcesses, maxConcurrentProcesses);
        // With a single slot there is nothing to reserve, so background work shares it with chat.
        if (maxConcurrentProcesses > 1)
            _backgroundSlots = new SemaphoreSlim(maxConcurrentProcesses - 1, maxConcurrentProcesses - 1);
    }

    public Task WaitAsync(CancellationToken cancellationToken) => WaitAsync(false, cancellationToken);

    /// <summary>Waits for a slot. Pass the same <paramref name="background"/> value to <see cref="Release(bool)"/>.</summary>
    public async Task WaitAsync(bool background, CancellationToken cancellationToken)
    {
        var lane = background ? _backgroundSlots : null;
        if (lane is not null) await lane.WaitAsync(cancellationToken);
        try
        {
            await _slots.WaitAsync(cancellationToken);
        }
        catch
        {
            lane?.Release();
            throw;
        }
    }

    public void Release() => Release(false);

    public void Release(bool background)
    {
        _slots.Release();
        if (background) _backgroundSlots?.Release();
    }

    public void Dispose()
    {
        _slots.Dispose();
        _backgroundSlots?.Dispose();
    }
}
