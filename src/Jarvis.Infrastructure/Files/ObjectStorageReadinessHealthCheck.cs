using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Jarvis.Infrastructure.Files;

public sealed class ObjectStorageReadinessHealthCheck(IAmazonS3 storage, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.GetBucketLocationAsync(new GetBucketLocationRequest
            {
                BucketName = configuration["ObjectStorage:Bucket"] ?? "jarvis-files"
            }, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Object storage unavailable.");
        }
    }
}
