using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Jarvis.Infrastructure.Persistence;

public sealed class DatabaseReadinessHealthCheck(JarvisDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database unavailable.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Connection exceptions may contain credentials; do not attach them to public health results.
            return HealthCheckResult.Unhealthy("Database unavailable.");
        }
    }
}
