using System.Net.Http.Headers;
using System.Net.Http.Json;
using Google.Apis.Auth.OAuth2;
using Jarvis.Application.Workflows;

namespace Jarvis.Api.Notifications;

public sealed class NotificationPushWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<NotificationPushWorker> logger) : BackgroundService
{
    private const int BatchSize = 40;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
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
        var queue = scope.ServiceProvider.GetRequiredService<IPushDeliveryQueue>();
        var candidates = await queue.ListDueAsync(BatchSize, cancellationToken);

        var handled = 0;
        foreach (var candidate in candidates)
        {
            var claim = await queue.TryClaimAsync(candidate, LeaseDuration, cancellationToken);
            if (!claim.Claimed) continue;
            handled++;
            if (claim.Work is not { } work) continue;

            try
            {
                var accessToken = await credential.UnderlyingCredential
                    .GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
                using var response = await SendAsync(projectId, work, accessToken, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    await queue.MarkDeliveredAsync(candidate, cancellationToken);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsInvalidTokenResponse(body))
                {
                    await queue.RemoveDeviceAsync(candidate.DeviceId, cancellationToken);
                    logger.LogInformation("Removed an expired push token for owner {OwnerId}.", work.OwnerId);
                    continue;
                }
                await queue.RecordFailureAsync(candidate, work.Attempts,
                    $"FCM returned {(int)response.StatusCode}.", cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("Push delivery failed for notification {NotificationId}: {ErrorType}.",
                    candidate.NotificationId, exception.GetType().Name);
                await queue.RecordFailureAsync(candidate, work.Attempts, exception.GetType().Name, cancellationToken);
            }
        }
        return handled;
    }

    internal static bool IsInvalidTokenResponse(string body) =>
        body.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("SENDER_ID_MISMATCH", StringComparison.OrdinalIgnoreCase);

    private async Task<HttpResponseMessage> SendAsync(string projectId, PushDeliveryWork work, string accessToken,
        CancellationToken cancellationToken)
    {
        var notification = work.Notification;
        var client = httpClientFactory.CreateClient("firebase-messaging");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(projectId)}/messages:send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                token = work.DeviceToken,
                notification = new { title = notification.Title, body = notification.Body },
                data = work.Data,
                android = new { priority = "HIGH", notification = new { channel_id = "jarvis_notifications" } },
                apns = new { headers = new Dictionary<string, string> { ["apns-priority"] = "10" },
                    payload = new { aps = BuildApsPayload(notification.Type) } }
            }
        });
        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// The iOS category picks the buttons the app registered for it: Done and Snooze on reminders, Open on
    /// approvals. The payload carries ids only, never tokens.
    /// </summary>
    internal static Dictionary<string, string> BuildApsPayload(string type)
    {
        var aps = new Dictionary<string, string> { ["sound"] = "default" };
        if (NotificationQuickActions.CategoryFor(type) is { } category) aps["category"] = category;
        return aps;
    }
}
