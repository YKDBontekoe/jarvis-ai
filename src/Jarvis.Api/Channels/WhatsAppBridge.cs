using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jarvis.Application.Channels;

namespace Jarvis.Api.Channels;

public sealed record BridgeSessionStatus(string State, string? Qr, string? Phone);

public sealed record BridgeInboundMessage(string Id, string From, string Text);

/// <summary>HTTP client for the Baileys-based WhatsApp bridge. Its session id is the channel connection id.</summary>
public sealed class WhatsAppBridgeClient(HttpClient http, ChannelOptions options)
{
    public bool Configured => !string.IsNullOrWhiteSpace(options.WhatsAppBridgeUrl);

    public async Task<BridgeSessionStatus> StartAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await SendAsync<BridgeSessionStatus>(HttpMethod.Put, sessionId, null, null, cancellationToken)
        ?? throw new HttpRequestException("The WhatsApp bridge returned no status.");

    public async Task<BridgeSessionStatus> GetStatusAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await SendAsync<BridgeSessionStatus>(HttpMethod.Get, sessionId, null, null, cancellationToken)
        ?? new BridgeSessionStatus("none", null, null);

    public async Task<IReadOnlyList<BridgeInboundMessage>> PullAsync(Guid sessionId, CancellationToken cancellationToken) =>
        (await SendAsync<BridgeMessages>(HttpMethod.Get, sessionId, "messages", null, cancellationToken))?.Messages ?? [];

    public Task AckAsync(Guid sessionId, IEnumerable<string> ids, CancellationToken cancellationToken) =>
        SendAsync<JsonElement?>(HttpMethod.Post, sessionId, "ack", new { ids }, cancellationToken);

    public Task SendTextAsync(Guid sessionId, string to, string text, CancellationToken cancellationToken) =>
        SendAsync<JsonElement?>(HttpMethod.Post, sessionId, "send", new { to, text }, cancellationToken);

    /// <summary>Logs the device out of WhatsApp and removes the stored session.</summary>
    public Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken) =>
        SendAsync<JsonElement?>(HttpMethod.Delete, sessionId, null, null, cancellationToken);

    private async Task<T?> SendAsync<T>(HttpMethod method, Guid sessionId, string? action, object? body,
        CancellationToken cancellationToken)
    {
        var baseUrl = options.WhatsAppBridgeUrl
                      ?? throw new InvalidOperationException("Channels:WhatsAppBridge:BaseUrl is not configured.");
        var url = $"{baseUrl.TrimEnd('/')}/sessions/{sessionId:D}" + (action is null ? "" : $"/{action}");
        using var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(options.WhatsAppBridgeToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.WhatsAppBridgeToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The WhatsApp bridge rejected the request ({(int)response.StatusCode}).");
        return await response.Content.ReadFromJsonAsync<T>(BridgeJson, cancellationToken);
    }

    private sealed record BridgeMessages(IReadOnlyList<BridgeInboundMessage> Messages);

    private static readonly JsonSerializerOptions BridgeJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>Sends WhatsApp messages through a QR-linked device via the Baileys bridge.</summary>
public sealed class WhatsAppLinkedTransport(WhatsAppBridgeClient bridge) : IChannelTransport
{
    public string Kind => ChannelKinds.WhatsAppLinked;
    public int MaxMessageLength => 4_000;

    public Task SendAsync(ChannelConnectionRecord connection, IReadOnlyDictionary<string, string>? secrets,
        string recipient, string text, CancellationToken cancellationToken) =>
        bridge.SendTextAsync(connection.Id, ChannelAddresses.Normalize(recipient), text, cancellationToken);
}

/// <summary>Pulls inbound messages from the bridge for every enabled linked WhatsApp connection.</summary>
public sealed class WhatsAppLinkedReceiver(IServiceScopeFactory scopes, WhatsAppBridgeClient bridge,
    ILogger<WhatsAppLinkedReceiver> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

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
                logger.LogDebug(exception, "WhatsApp bridge poll failed; retrying.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReceiveOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        foreach (var connection in await repository.ListEnabledAsync(ChannelKinds.WhatsAppLinked, cancellationToken))
        {
            IReadOnlyList<BridgeInboundMessage> messages;
            try
            {
                messages = await bridge.PullAsync(connection.Id, cancellationToken);
            }
            catch (HttpRequestException)
            {
                continue; // Unknown session or bridge restarting; try again on the next tick.
            }
            if (messages.Count == 0) continue;
            // Enqueue is idempotent per external id, so a failed ack only means a harmless re-delivery.
            foreach (var message in messages)
                await repository.EnqueueInboundAsync(connection.Id, message.From, message.Text, $"wa:{message.Id}",
                    cancellationToken);
            await bridge.AckAsync(connection.Id, messages.Select(message => message.Id), cancellationToken);
        }
    }
}
