using Jarvis.Workflows;
using Temporalio.Common;
using Temporalio.Worker;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class WorkflowReplayTests
{
    [Fact]
    public async Task Recorded_workflow_histories_remain_deterministic()
    {
        var directory = Environment.GetEnvironmentVariable("JARVIS_REPLAY_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "Histories");
        var histories = Directory.GetFiles(directory, "*.json");
        Assert.NotEmpty(histories);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var options = new WorkflowReplayerOptions();
        foreach (var type in typeof(ReminderWorkflow).Assembly.GetTypes()
                     .Where(type => type.GetCustomAttributes(typeof(Temporalio.Workflows.WorkflowAttribute), false).Length > 0))
            options.AddWorkflow(type);
        var replayer = new WorkflowReplayer(options);
        foreach (var path in histories)
            await replayer.ReplayWorkflowAsync(WorkflowHistory.FromJson(Path.GetFileNameWithoutExtension(path),
                await File.ReadAllTextAsync(path, timeout.Token)), cancellationToken: timeout.Token);
    }
}
