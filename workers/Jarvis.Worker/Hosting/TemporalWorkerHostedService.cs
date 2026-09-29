using Jarvis.Workflows;
using Temporalio.Client;

namespace Jarvis.Worker.Hosting;

internal sealed class TemporalWorkerHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<TemporalWorkerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var temporalAddress = TemporalAddress.Normalize(configuration["Temporal:Address"]);
        logger.LogInformation("Connecting Temporal worker to {TemporalAddress}", temporalAddress);
        var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(temporalAddress));
        using var worker = TemporalWorkerRegistration.CreateWorker(client, services);
        await worker.ExecuteAsync(stoppingToken);
    }
}
