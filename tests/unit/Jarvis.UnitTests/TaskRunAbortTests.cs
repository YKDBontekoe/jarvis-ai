using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class TaskRunAbortTests
{
    [Fact]
    public void Abort_cancels_the_registered_run()
    {
        var registry = new TaskRunAbort();
        using var abort = new CancellationTokenSource();
        var taskId = Guid.CreateVersion7();
        using var lease = registry.Register(taskId, abort);

        registry.Abort(taskId);

        Assert.True(abort.IsCancellationRequested);
    }

    [Fact]
    public void Abort_after_dispose_is_ignored()
    {
        var registry = new TaskRunAbort();
        using var abort = new CancellationTokenSource();
        var taskId = Guid.CreateVersion7();
        registry.Register(taskId, abort).Dispose();

        registry.Abort(taskId);

        Assert.False(abort.IsCancellationRequested);
    }

    [Fact]
    public void Register_cancels_the_previous_run_for_the_same_task()
    {
        var registry = new TaskRunAbort();
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        var taskId = Guid.CreateVersion7();
        using var firstLease = registry.Register(taskId, first);
        using var secondLease = registry.Register(taskId, second);

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);

        registry.Abort(taskId);

        Assert.True(second.IsCancellationRequested);
    }
}
