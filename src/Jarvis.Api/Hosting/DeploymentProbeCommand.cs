using Jarvis.Infrastructure;
using Jarvis.Workflows;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Temporalio.Client;

namespace Jarvis.Api.Hosting;

internal static class DeploymentProbeCommand
{
    public static async Task<int> RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddJarvisInfrastructure(builder.Configuration);
            using var host = builder.Build();
            var health = await host.Services.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(timeout.Token);
            if (health.Status != HealthStatus.Healthy)
                throw new InvalidOperationException("Required dependency unavailable.");
            var client = TemporalClient.CreateLazy(new TemporalClientConnectOptions(
                builder.Configuration["Temporal:Address"] ?? "localhost:7233")
            {
                Namespace = builder.Configuration["Temporal:Namespace"] ?? "default"
            });
            var nonce = Guid.NewGuid().ToString("N");
            var handle = await client.StartWorkflowAsync((DeploymentProbeWorkflow workflow) => workflow.RunAsync(nonce),
                new WorkflowOptions($"jarvis:deployment-probe:{nonce}", TemporalReminderScheduler.TaskQueue)
                {
                    ExecutionTimeout = TimeSpan.FromSeconds(30),
                    Rpc = new RpcOptions { CancellationToken = timeout.Token }
                });
            var result = await handle.GetResultAsync(rpcOptions: new RpcOptions { CancellationToken = timeout.Token });
            if (result != nonce) throw new InvalidOperationException("Unexpected probe result.");
            Console.WriteLine("Database, object storage, and Temporal workflow execution verified.");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Deployment probe failed: dependency readiness or worker execution could not be verified.");
            return 1;
        }
    }
}
