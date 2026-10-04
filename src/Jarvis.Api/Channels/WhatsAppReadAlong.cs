using Jarvis.Application.Automations;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Jarvis.Api.Endpoints;
using Jarvis.Api.Security;
using Jarvis.Application.Audit;
using Jarvis.Application.Channels;
using Jarvis.Application.WhatsApp;
using Jarvis.Application.Workflows;

namespace Jarvis.Api.Channels;

/// <summary>
/// Sends from the owner's own linked WhatsApp through the bridge, only to chats they read along with. Callers
/// decide whether a send is allowed (the owner tapping Send, or an approved agent tool call); this class checks
/// ownership and records an id-only audit entry.
/// </summary>
public sealed class BridgeWhatsAppSender(WhatsAppBridgeClient bridge, IChannelRepository channels,
    IWhatsAppAssistantRepository chats, IAuditEventStore audit, TimeProvider clock,
    ILogger<BridgeWhatsAppSender> logger) : IWhatsAppSender
{
    public bool Available => bridge.Configured;

    public async Task<WhatsAppSendResult> SendAsync(Guid ownerId, Guid connectionId, string chatId, string text,
        CancellationToken cancellationToken)
    {
        if (!bridge.Configured) return new WhatsAppSendResult(false, null, "WhatsApp is not set up on this server.");
        var connection = await channels.GetAsync(ownerId, connectionId, cancellationToken);
        if (connection is not { Kind: ChannelKinds.WhatsAppLinked })
            return new WhatsAppSendResult(false, null, "That WhatsApp account is not linked.");
        if (!connection.Enabled) return new WhatsAppSendResult(false, null, "This WhatsApp account is paused.");
        var chat = await chats.GetChatAsync(ownerId, connectionId, chatId, cancellationToken);
        if (chat is not { ReadAlong: true })
            return new WhatsAppSendResult(false, null, "Turn on read along for this chat first.");

        string? externalId;
        try
        {
            externalId = await bridge.SendToChatAsync(connectionId, chatId, text, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Could not send a WhatsApp message for chat {ChatSettingsId}.", chat.Id);
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, ownerId, "whatsapp", "whatsapp.message_sent",
                "high", false, null, JsonSerializer.Serialize(new { resourceId = chat.Id }), cancellationToken);
            return new WhatsAppSendResult(false, null, "WhatsApp is not reachable right now. Try again in a moment.");
        }
        // The bridge also echoes the message back; both carry the same id, so it is stored once.
        if (externalId is not null)
            await chats.StoreObservedAsync(connectionId,
                [new ObservedWhatsAppMessage(WhatsAppReadAlongReceiver.ExternalId(externalId), chatId, true, null,
                    text, clock.GetUtcNow())], cancellationToken);
        await EndpointHelpers.TryAppendAuditAsync(audit, logger, ownerId, "whatsapp", "whatsapp.message_sent", "high",
            true, null, JsonSerializer.Serialize(new { resourceId = chat.Id }), cancellationToken);
        return new WhatsAppSendResult(true, externalId, null);
    }
}

/// <summary>
/// Keeps the bridge's watch list in step with the owner's read-along choices and stores the messages it forwards.
/// The bridge never forwards a chat that is not on the list, so turning a chat off stops reading at the source.
/// </summary>
public sealed class WhatsAppReadAlongReceiver(IServiceScopeFactory scopes, WhatsAppBridgeClient bridge,
    ILogger<WhatsAppReadAlongReceiver> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WatchRefresh = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<Guid, (string Key, DateTimeOffset At)> pushed = new();

    internal static string ExternalId(string bridgeId) => "wa:" + bridgeId;

    /// <summary>Pushes the current watch list for one connection now (after the owner changed a chat).</summary>
    public async Task SyncWatchAsync(Guid connectionId, IReadOnlyList<string> watched,
        CancellationToken cancellationToken)
    {
        if (!bridge.Configured) return;
        await bridge.SetWatchedAsync(connectionId, watched, cancellationToken);
        pushed[connectionId] = (string.Join(',', watched), DateTimeOffset.UtcNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!bridge.Configured) return;
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
                logger.LogWarning("WhatsApp read-along poll failed ({FailureType}); retrying.", exception.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReceiveOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var connections = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        var chats = scope.ServiceProvider.GetRequiredService<IWhatsAppAssistantRepository>();
        foreach (var connection in await connections.ListEnabledAsync(ChannelKinds.WhatsAppLinked, cancellationToken))
        {
            var watched = await chats.ListWatchedChatIdsAsync(connection.Id, cancellationToken);
            try
            {
                var key = string.Join(',', watched);
                if (!pushed.TryGetValue(connection.Id, out var last) || last.Key != key ||
                    DateTimeOffset.UtcNow - last.At > WatchRefresh)
                    await SyncWatchAsync(connection.Id, watched, cancellationToken);
                if (watched.Count == 0) continue;

                var messages = await bridge.PullObservedAsync(connection.Id, cancellationToken);
                if (messages.Count == 0) continue;
                var observed = new List<ObservedWhatsAppMessage>(messages.Count);
                foreach (var message in messages)
                    observed.Add(await ToObservedAsync(bridge, connection.Id, message, cancellationToken));
                if (await chats.StoreObservedAsync(connection.Id, observed, cancellationToken) > 0)
                    await PublishReceivedAsync(scope.ServiceProvider, chats, connection, observed, cancellationToken);
                await bridge.AckObservedAsync(connection.Id, messages.Select(message => message.Id),
                    cancellationToken);
            }
            catch (HttpRequestException)
            {
                pushed.TryRemove(connection.Id, out _); // Bridge restarting or session unknown; push again next tick.
            }
        }
    }

    // Automations hear about messages from other people. A redelivered batch starts nothing twice, because an
    // event's fingerprint is part of the run's idempotency key.
    private static async Task PublishReceivedAsync(IServiceProvider services, IWhatsAppAssistantRepository chats,
        ChannelConnectionRecord connection, IReadOnlyList<ObservedWhatsAppMessage> messages,
        CancellationToken cancellationToken)
    {
        var bus = services.GetService<Jarvis.Application.Automations.IAutomationEventBus>();
        if (bus is null) return;
        foreach (var message in messages.Where(x => !x.FromMe && !x.Historical).Take(20))
        {
            var chat = await chats.GetChatAsync(connection.OwnerId, connection.Id, message.ChatId, cancellationToken);
            await bus.TryPublishAsync(connection.OwnerId, new Jarvis.Application.Automations.AutomationEvent(
                Jarvis.Application.Automations.AutomationEventKinds.MessageReceived,
                chat?.DisplayName ?? WhatsAppChatIds.FallbackName(message.ChatId), message.Text, "whatsapp", null,
                message.SentAt), cancellationToken);
        }
    }

    internal static ObservedWhatsAppMessage ToObserved(BridgeObservedMessage message, byte[]? content = null)
    {
        var sender = string.IsNullOrWhiteSpace(message.Sender) ? null : message.Sender.Trim();
        if (sender is { Length: > 80 }) sender = sender[..80];
        var stored = WhatsAppMediaCodec.Prepare(message.Media, message.Quote, content);
        return new(ExternalId(message.Id), message.ChatId, message.FromMe, sender, message.Text,
            message.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp) : DateTimeOffset.UtcNow,
            WhatsAppChatIds.Normalize(message.SenderId), stored?.Json, stored?.Content, stored?.ContentType,
            message.Historical);
    }

    private static async Task<ObservedWhatsAppMessage> ToObservedAsync(WhatsAppBridgeClient bridge, Guid connectionId,
        BridgeObservedMessage message, CancellationToken cancellationToken)
    {
        byte[]? content = null;
        if (message.Media is { HasContent: true })
        {
            try
            {
                content = (await bridge.GetMediaAsync(connectionId, message.Id, cancellationToken))?.Bytes;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                content = null;
            }
        }
        return ToObserved(message, content);
    }
}

/// <summary>
/// Reads new messages in chats with auto reminders on and turns agreed appointments and promises into reminders.
/// It waits until a chat has been quiet for a moment so a burst of messages is read as one conversation.
/// </summary>
public sealed class WhatsAppReminderScanner(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<WhatsAppReminderScanner> logger) : BackgroundService
{
    internal static readonly TimeSpan Quiet = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);
    internal const string NotificationType = "whatsapp.reminder";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                IReadOnlyList<WhatsAppScanBatch> batches;
                await using (var scope = scopes.CreateAsyncScope())
                    batches = await scope.ServiceProvider.GetRequiredService<IWhatsAppAssistantRepository>()
                        .ClaimScanBatchesAsync(Quiet, 5, 15, stoppingToken);
                foreach (var batch in batches) await ScanAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "WhatsApp reminder scan failed; it will be retried.");
            }
        }
    }

    internal async Task ScanAsync(WhatsAppScanBatch batch, CancellationToken cancellationToken)
    {
        var ownerId = batch.Chat.OwnerId;
        await using var scope = scopes.CreateOwnerScope(ownerId);
        var services = scope.ServiceProvider;
        var reminders = services.GetRequiredService<IReminderService>();
        var existing = (await reminders.ListAsync(ownerId, cancellationToken))
            .Where(item => item.Status == "pending").OrderBy(item => item.DueAt).Select(item => item.Title).ToArray();
        var zoneId = (await services.GetRequiredService<IDailyBriefingRepository>().GetAsync(ownerId,
            cancellationToken))?.TimeZoneId ?? "UTC";
        var found = await services.GetRequiredService<IWhatsAppAssistant>().FindRemindersAsync(ownerId, batch,
            existing, clock.GetUtcNow(), zoneId, cancellationToken);

        var zone = LocalClock.TryFind(zoneId, out var resolved) ? resolved : TimeZoneInfo.Utc;
        foreach (var suggestion in found)
        {
            if (existing.Contains(suggestion.Title, StringComparer.OrdinalIgnoreCase)) continue;
            var reminder = await reminders.CreateAsync(ownerId,
                new CreateReminderRequest(suggestion.Title, suggestion.DueAt, null, 0, zone.Id), cancellationToken);
            var when = TimeZoneInfo.ConvertTime(reminder.DueAt, zone)
                .ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture);
            await services.GetRequiredService<INotificationRepository>().CreateAsync(ownerId, NotificationType,
                "Reminder from WhatsApp", $"{reminder.Title} · {when} · from your chat with {batch.Chat.DisplayName}",
                reminder.Id, cancellationToken);
            await EndpointHelpers.TryAppendAuditAsync(services.GetRequiredService<IAuditEventStore>(), logger,
                ownerId, "whatsapp", "whatsapp.reminder_created", "low", true, null,
                JsonSerializer.Serialize(new { resourceId = reminder.Id, chatSettingsId = batch.Chat.Id }),
                cancellationToken);
        }
        await services.GetRequiredService<IWhatsAppAssistantRepository>()
            .CompleteScanAsync(batch.Chat.Id, batch.ScannedThrough, cancellationToken);
    }
}
