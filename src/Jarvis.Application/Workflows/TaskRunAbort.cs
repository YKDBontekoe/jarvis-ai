using System.Collections.Concurrent;

namespace Jarvis.Application.Workflows;

public interface ITaskRunAbort
{
    IDisposable Register(Guid taskId, CancellationTokenSource abort);
    void Abort(Guid taskId);
}

public sealed class TaskRunAbort : ITaskRunAbort
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runs = new();

    public IDisposable Register(Guid taskId, CancellationTokenSource abort)
    {
        _runs[taskId] = abort;
        return new Lease(_runs, taskId, abort);
    }

    public void Abort(Guid taskId)
    {
        if (!_runs.TryGetValue(taskId, out var abort)) return;
        try
        {
            abort.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private sealed class Lease(
        ConcurrentDictionary<Guid, CancellationTokenSource> runs,
        Guid taskId,
        CancellationTokenSource abort) : IDisposable
    {
        public void Dispose() =>
            runs.TryRemove(KeyValuePair.Create(taskId, abort));
    }
}
