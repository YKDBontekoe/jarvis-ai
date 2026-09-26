using System.Net.Http.Headers;
using System.Net.Http.Json;
using Google.Apis.Auth.OAuth2;
using Jarvis.Domain.Workflows;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Api.Notifications;

public sealed class NotificationPushWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<NotificationPushWorker> logger) : BackgroundService
{
    private const int BatchSize = 40;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const string MessagingScope = "https://www.googleapis.com/auth/firebase.messaging";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var credentialPath = configuration["Push:GoogleServiceAccountFile"];
        var projectId = configuration["Push:FirebaseProjectId"];
        if (string.IsNullOrWhiteSpace(credentialPath) || string.IsNullOrWhiteSpace(projectId))
        {
            logger.LogInformation("Push delivery is disabled; configure Push:GoogleServiceAccountFile and Push:FirebaseProjectId to enable FCM/APNs.");
            return;
        }

        GoogleCredential credential;
        try
        {
            credential = CredentialFactory.FromFile<ServiceAccountCredential>(credentialPath)
                .ToGoogleCredential().CreateScoped(MessagingScope);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Push delivery is disabled because the Firebase service account could not be loaded.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = await DeliverBatchAsync(credential, projectId, stoppingToken);
                if (count == 0) await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Push delivery batch failed; it will be retried.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> DeliverBatchAsync(GoogleCredential credential, string projectId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JarvisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.PushDeliveries.AsNoTracking()
            .Where(x => x.DeliveredAt == null && x.Attempts < 10 &&
                        x.NextAttemptAt <= now && (x.LeaseUntil == null || x.LeaseUntil < now))
            .OrderBy(x => x.NextAttemptAt)
            .Select(x => new { x.NotificationId, x.DeviceId })
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var handled = 0;
        foreach (var candidate in candidates)
        {
            var leaseUntil = DateTimeOffset.UtcNow.AddMinutes(2);
            var claimed = await db.PushDeliveries
                .Where(x => x.NotificationId == candidate.NotificationId && x.DeviceId == candidate.DeviceId &&
                            x.DeliveredAt == null && x.Attempts < 10 && x.NextAttemptAt <= DateTimeOffset.UtcNow &&
                            (x.LeaseUntil == null || x.LeaseUntil < DateTimeOffset.UtcNow))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                    .SetProperty(x => x.LeaseUntil, leaseUntil), cancellationToken);
            if (claimed == 0) continue;
            handled++;

            var delivery = await db.PushDeliveries.AsNoTracking().SingleAsync(x =>
                x.NotificationId == candidate.NotificationId && x.DeviceId == candidate.DeviceId, cancellationToken);
            var notification = await db.Notifications.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == candidate.NotificationId, cancellationToken);
            var device = await db.PushDevices.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == candidate.DeviceId, cancellationToken);
            if (notification is null || device is null || device.OwnerId != notification.OwnerId)
            {
                await db.PushDeliveries.Where(x => x.NotificationId == candidate.NotificationId &&
                                                   x.DeviceId == candidate.DeviceId)
                    .ExecuteDeleteAsync(cancellationToken);
                continue;
            }

            try
            {
                var accessToken = await credential.UnderlyingCredential
                    .GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
                using var response = await SendAsync(projectId, device.Token, notification, accessToken,
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    await db.PushDeliveries.Where(x => x.NotificationId == candidate.NotificationId &&
                                                       x.DeviceId == candidate.DeviceId)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(x => x.DeliveredAt, DateTimeOffset.UtcNow)
                            .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null)
                            .SetProperty(x => x.LastError, (string?)null), cancellationToken);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var invalidToken = response.StatusCode == System.Net.HttpStatusCode.NotFound &&
                                   body.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase);
                if (invalidToken)
                {
                    await db.PushDevices.Where(x => x.Id == device.Id)
                        .ExecuteDeleteAsync(cancellationToken);
                    logger.LogInformation("Removed an expired push token for owner {OwnerId}.", device.OwnerId);
                    continue;
                }
                await RecordFailureAsync(db, candidate.NotificationId, candidate.DeviceId,
                    delivery.Attempts, $"FCM returned {(int)response.StatusCode}.", cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("Push delivery failed for notification {NotificationId}: {ErrorType}.",
                    candidate.NotificationId, exception.GetType().Name);
                await RecordFailureAsync(db, candidate.NotificationId, candidate.DeviceId,
                    delivery.Attempts, exception.GetType().Name, cancellationToken);
            }
        }
        return handled;
    }

    private async Task<HttpResponseMessage> SendAsync(string projectId, string token,
        Notification notification, string accessToken, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("firebase-messaging");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(projectId)}/messages:send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                token,
                notification = new { title = notification.Title, body = notification.Body },
                data = new Dictionary<string, string>
                {
                    ["notificationId"] = notification.Id.ToString("D"),
                    ["type"] = notification.Type,
                    ["sourceId"] = notification.SourceId?.ToString("D") ?? string.Empty
                },
                android = new { priority = "HIGH", notification = new { channel_id = "jarvis_notifications" } },
                apns = new { headers = new Dictionary<string, string> { ["apns-priority"] = "10" },
                    payload = new { aps = new { sound = "default" } } }
            }
        });
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task RecordFailureAsync(JarvisDbContext db, Guid notificationId, Guid deviceId,
        int attempts, string error, CancellationToken cancellationToken)
    {
        var failed = await db.PushDeliveries.SingleAsync(x =>
            x.NotificationId == notificationId && x.DeviceId == deviceId, cancellationToken);
        failed.Fail(DateTimeOffset.UtcNow, error, permanent: attempts >= 10);
        await db.SaveChangesAsync(cancellationToken);
    }
}
