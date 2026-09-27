using System.Text.Json;
using Jarvis.Api.Security;
using Jarvis.Application.Channels;
using Jarvis.Application.Workflows;

namespace Jarvis.Api.Channels;

/// <summary>Processes durably stored inbound channel messages one at a time, in arrival order.</summary>
public sealed class ChannelInboundProcessor(IServiceScopeFactory scopes, ILogger<ChannelInboundProcessor> logger)
    : BackgroundService
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                IReadOnlyList<InboundChannelMessage> batch;
                await using (var scope = scopes.CreateAsyncScope())
                    batch = await scope.ServiceProvider.GetRequiredService<IChannelRepository>()
                        .ClaimPendingInboundAsync(10, stoppingToken);
                foreach (var message in batch)
                {
                    processed++;
                    await ProcessAsync(message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Channel inbound processing failed; it will be retried.");
            }
            if (processed == 0) await Task.Delay(Idle, stoppingToken);
        }
    }

    private async Task ProcessAsync(InboundChannelMessage message, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateOwnerScope(message.Connection.OwnerId);
        var repository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        try
        {
            await scope.ServiceProvider.GetRequiredService<ChannelMessageRouter>().HandleAsync(message, cancellationToken);
            await repository.CompleteInboundAsync(message.Id, null, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not answer channel message {MessageId}.", message.Id);
            await repository.CompleteInboundAsync(message.Id,
                exception.Message.Length <= 500 ? exception.Message : exception.Message[..500], CancellationToken.None);
        }
    }
}

/// <summary>Polls signal-cli for new messages on every enabled Signal connection.</summary>
public sealed class SignalReceiver(IServiceScopeFactory scopes, IHttpClientFactory httpClients, ChannelOptions options,
    ILogger<SignalReceiver> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.SignalBaseUrl)) return;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await ReceiveOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Signal receive poll failed; retrying.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReceiveOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var http = httpClients.CreateClient("signal");
        foreach (var connection in await repository.ListEnabledAsync(ChannelKinds.Signal, cancellationToken))
        {
            using var response = await http.GetAsync(
                $"{options.SignalBaseUrl!.TrimEnd('/')}/v1/receive/{Uri.EscapeDataString(connection.Account)}?timeout=1",
                cancellationToken);
            if (!response.IsSuccessStatusCode) continue;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            foreach (var (sender, text, externalId) in ParseEnvelopes(document.RootElement))
                await repository.EnqueueInboundAsync(connection.Id, sender, text, externalId, cancellationToken);
        }
    }

    internal static IEnumerable<(string Sender, string Text, string ExternalId)> ParseEnvelopes(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in root.EnumerateArray())
        {
            if (!item.TryGetProperty("envelope", out var envelope)) continue;
            var sender = envelope.TryGetProperty("sourceNumber", out var number) && number.ValueKind == JsonValueKind.String
                ? number.GetString()
                : envelope.TryGetProperty("source", out var source) ? source.GetString() : null;
            if (!envelope.TryGetProperty("dataMessage", out var data) ||
                !data.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(sender))
                continue;
            var timestamp = envelope.TryGetProperty("timestamp", out var time) ? time.ToString() : Guid.NewGuid().ToString("N");
            yield return (sender!, message.GetString()!, $"{sender}:{timestamp}");
        }
    }
}

/// <summary>Forwards reminders, task results, briefings, and heartbeat check-ins to connected channels.</summary>
public sealed class ChannelNotificationForwarder(IServiceScopeFactory scopes, ILogger<ChannelNotificationForwarder> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan WhatsAppServiceWindow = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await ForwardOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Channel notification forwarding failed; retrying.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ForwardOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ChannelConnectionRecord> connections;
        await using (var scope = scopes.CreateAsyncScope())
            connections = (await scope.ServiceProvider.GetRequiredService<IChannelRepository>()
                .ListEnabledAsync(null, cancellationToken)).Where(connection => connection.ForwardNotifications).ToArray();
        foreach (var connection in connections)
        {
            await using var scope = scopes.CreateOwnerScope(connection.OwnerId);
            var services = scope.ServiceProvider;
            var notifications = (await services.GetRequiredService<INotificationRepository>()
                    .ListNotificationsAsync(connection.OwnerId, cancellationToken))
                .Where(item => item.CreatedAt > connection.NotificationsForwardedUntil)
                .OrderBy(item => item.CreatedAt)
                .ToArray();
            if (notifications.Length == 0) continue;
            var recipient = connection.NotifyRecipient ?? connection.AllowedSenders.FirstOrDefault();
            if (recipient is not null && CanMessage(connection, DateTimeOffset.UtcNow))
            {
                var messenger = services.GetRequiredService<ChannelMessenger>();
                foreach (var notification in notifications.Where(item => ShouldForward(item.Type)))
                    await messenger.SendAsync(connection, recipient, $"🔔 **{notification.Title}**\n{notification.Body}",
                        cancellationToken);
            }
            await services.GetRequiredService<IChannelRepository>()
                .AdvanceNotificationWatermarkAsync(connection.Id, notifications[^1].CreatedAt, cancellationToken);
        }
    }

    internal static bool ShouldForward(string type) => type != "approval.required";

    /// <summary>WhatsApp only allows free-form business messages within 24 hours of the user's last message.</summary>
    internal static bool CanMessage(ChannelConnectionRecord connection, DateTimeOffset now) =>
        connection.Kind != ChannelKinds.WhatsApp ||
        connection.LastInboundAt is { } lastInbound && now - lastInbound < WhatsAppServiceWindow;
}
