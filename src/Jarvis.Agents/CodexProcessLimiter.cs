namespace Jarvis.Agents;

/// <summary>
/// Caps concurrent Codex CLI processes across chat completions and coding tasks.
/// </summary>
public sealed class CodexProcessLimiter : IDisposable
{
    public const int MaxConcurrentProcesses = 2;

    private readonly SemaphoreSlim _slots = new(MaxConcurrentProcesses, MaxConcurrentProcesses);

    public Task WaitAsync(CancellationToken cancellationToken) => _slots.WaitAsync(cancellationToken);

    public void Release() => _slots.Release();

    public void Dispose() => _slots.Dispose();
}
