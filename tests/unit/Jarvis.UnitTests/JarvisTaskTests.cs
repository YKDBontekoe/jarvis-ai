using Jarvis.Domain.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class JarvisTaskTests
{
    [Fact]
    public void Failed_tasks_cannot_be_completed()
    {
        var task = new JarvisTask(Guid.CreateVersion7(), "Review", "Check the report");
        task.Fail("The tool call was declined.");
        task.Complete("Should not replace the failure.");
        Assert.Equal("failed", task.Status);
        Assert.Equal("The tool call was declined.", task.Summary);
    }

    [Fact]
    public void Needs_approval_tasks_can_be_completed()
    {
        var task = new JarvisTask(Guid.CreateVersion7(), "Draft", "Write the draft");
        task.MarkRunning();
        task.MarkNeedsApproval();
        task.Complete("Approved work finished.");
        Assert.Equal("completed", task.Status);
        Assert.Equal("Approved work finished.", task.Summary);
        task.Fail("Too late.");
        Assert.Equal("completed", task.Status);
    }

    [Fact]
    public void Completed_and_cancelled_tasks_cannot_be_failed()
    {
        var completed = new JarvisTask(Guid.CreateVersion7(), "One", "Do one");
        completed.Complete("Finished.");
        completed.Fail("Too late.");
        Assert.Equal("completed", completed.Status);

        var cancelled = new JarvisTask(Guid.CreateVersion7(), "Two", "Do two");
        cancelled.Cancel();
        cancelled.Fail("Too late.");
        Assert.Equal("cancelled", cancelled.Status);
    }
}
