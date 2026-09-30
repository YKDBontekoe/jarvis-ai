using System.Text.Json;
using Jarvis.Api.Security;
using Jarvis.Application.Approvals;
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
            foreach (var (sender, text, externalId) in ParseEnvelopes(document.RootElement, connection.Account))
                await repository.EnqueueInboundAsync(connection.Id, sender, text, externalId, cancellationToken);
        }
    }

    /// <summary>
    /// Reads text messages. With <paramref name="selfAccount"/>, "Note to Self" messages typed on the primary phone
    /// (delivered to this linked device as sync transcripts) count as messages from the owner.
    /// </summary>
    internal static IEnumerable<(string Sender, string Text, string ExternalId)> ParseEnvelopes(JsonElement root,
        string? selfAccount = null)
    {
        if (root.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in root.EnumerateArray())
        {
            if (!item.TryGetProperty("envelope", out var envelope)) continue;
            var sender = envelope.TryGetProperty("sourceNumber", out var number) && number.ValueKind == JsonValueKind.String
                ? number.GetString()
                : envelope.TryGetProperty("source", out var source) ? source.GetString() : null;
            if (selfAccount is not null && envelope.TryGetProperty("syncMessage", out var sync) &&
                sync.TryGetProperty("sentMessage", out var sent) &&
                sent.TryGetProperty("message", out var selfMessage) && selfMessage.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(selfMessage.GetString()) &&
                (sent.TryGetProperty("destinationNumber", out var destination) ? destination.GetString() : null) is { } to &&
                ChannelAddresses.Normalize(to) == ChannelAddresses.Normalize(selfAccount))
            {
                var sentAt = sent.TryGetProperty("timestamp", out var sentTime) ? sentTime.ToString() : Guid.NewGuid().ToString("N");
                yield return (ChannelAddresses.Normalize(selfAccount), selfMessage.GetString()!,
                    $"{ChannelAddresses.Normalize(selfAccount)}:{sentAt}");
                continue;
            }
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
            var channels = services.GetRequiredService<IChannelRepository>();
            var notifications = (await services.GetRequiredService<INotificationRepository>()
                    .ListNotificationsAsync(connection.OwnerId, cancellationToken))
                .Where(item => item.CreatedAt > connection.NotificationsForwardedUntil)
                .OrderBy(item => item.CreatedAt)
                .ToArray();
            if (notifications.Length == 0) continue;
            var wanted = notifications.Where(item => ShouldForward(connection, item.Type)).ToArray();
            var recipient = connection.NotifyRecipient ?? connection.AllowedSenders.FirstOrDefault();
            if (recipient is not null && wanted.Length > 0)
            {
                if (CanMessage(connection, DateTimeOffset.UtcNow))
                {
                    var messenger = services.GetRequiredService<ChannelMessenger>();
                    foreach (var notification in wanted)
                    {
                        var text = await ComposeAsync(connection, recipient, notification, services, cancellationToken);
                        if (text is not null) await messenger.SendAsync(connection, recipient, text, cancellationToken);
                    }
                }
                else
                {
                    // Surface the gap on the channel instead of dropping notifications without a trace.
                    await channels.RecordOutboundAsync(connection.Id, recipient,
                        $"{wanted.Length} notification(s) were not sent to WhatsApp.", WindowClosedError,
                        cancellationToken);
                }
            }
            await channels.AdvanceNotificationWatermarkAsync(connection.Id, notifications[^1].CreatedAt,
                cancellationToken);
        }
    }

    internal const string WindowClosedError =
        "WhatsApp only allows messages within 24 hours of your last message to Jarvis. " +
        "Message Jarvis on WhatsApp to reopen it, or check the Jarvis app.";

    internal static bool ShouldForward(ChannelConnectionRecord connection, string type) =>
        ChannelNotificationCategories.IsEnabled(connection.NotificationCategories, type);

    private static async Task<string?> ComposeAsync(ChannelConnectionRecord connection, string recipient,
        NotificationRecord notification, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (notification.Type != "approval.required" || notification.SourceId is not { } approvalId)
            return Compose(connection.Kind, notification, decidableHere: null);

        var approval = await services.GetRequiredService<IToolApprovalStore>()
            .GetActionableAsync(approvalId, connection.OwnerId, cancellationToken);
        if (approval is null || approval.Status != "pending") return null; // Decided elsewhere already.
        var thread = await services.GetRequiredService<IChannelRepository>().GetThreadConversationAsync(
            connection.Id, ChannelAddresses.Normalize(recipient), cancellationToken);
        return Compose(connection.Kind, notification,
            decidableHere: approval.TaskId is null && thread == approval.ConversationId);
    }

    /// <summary>
    /// Builds the message. <paramref name="decidableHere"/> is only used for tool approvals: true when a YES/NO reply
    /// in this chat decides it, false when it can only be decided in the Jarvis app.
    /// </summary>
    internal static string Compose(string kind, NotificationRecord notification, bool? decidableHere)
    {
        var app = ChannelKinds.Label(kind);
        return notification.Type switch
        {
            "approval.required" when decidableHere == true =>
                $"🔐 **{notification.Title}**\n{notification.Body}\nReply YES to approve or NO to decline.",
            "approval.required" =>
                $"🔐 **{notification.Title}**\n{notification.Body}\n" +
                $"⚠️ This can't be approved from {app}. Open the Jarvis app to review and decide.",
            "automation.approval" =>
                $"🔐 **{notification.Title}**\n{notification.Body}\n" +
                $"⚠️ Automation approvals can't be decided from {app}. Open the Jarvis app to review and decide.",
            _ => $"🔔 **{notification.Title}**\n{notification.Body}"
        };
    }

    /// <summary>
    /// The WhatsApp Cloud API only allows free-form business messages within 24 hours of the user's last message.
    /// Linked devices (WhatsApp Web / Signal) have no such window.
    /// </summary>
    internal static bool CanMessage(ChannelConnectionRecord connection, DateTimeOffset now) =>
        connection.Kind != ChannelKinds.WhatsApp ||
        connection.LastInboundAt is { } lastInbound && now - lastInbound < WhatsAppServiceWindow;
}
