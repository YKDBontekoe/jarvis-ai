using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Temporalio.Api.Enums.V1;
using Temporalio.Api.TaskQueue.V1;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Client;

namespace Jarvis.Workflows;

public sealed class TemporalReadinessHealthCheck(IConfiguration configuration) : IHealthCheck
{
    private readonly TemporalClient _client = TemporalClient.CreateLazy(new TemporalClientConnectOptions(
        configuration["Temporal:Address"] ?? "localhost:7233")
    {
        Namespace = configuration["Temporal:Namespace"] ?? "default"
    });

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.Connection.WorkflowService.DescribeTaskQueueAsync(new DescribeTaskQueueRequest
            {
                Namespace = _client.Options.Namespace,
                TaskQueue = new TaskQueue { Name = TemporalReminderScheduler.TaskQueue },
                TaskQueueType = TaskQueueType.Workflow
            }, new RpcOptions { CancellationToken = cancellationToken, Timeout = TimeSpan.FromSeconds(5) });
            return result.Pollers.Any(poller => poller.LastAccessTime.ToDateTime() > DateTime.UtcNow.AddMinutes(-2))
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("No recent Temporal worker poller.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Temporal unavailable.");
        }
    }
}
