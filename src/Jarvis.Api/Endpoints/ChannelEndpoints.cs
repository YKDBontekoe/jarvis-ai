using System.Net;
using System.Text.Json;
using Jarvis.Api.Channels;
using Jarvis.Application.Audit;
using Jarvis.Application.Channels;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Application.Security;
using Microsoft.EntityFrameworkCore;

using Jarvis.Api.Errors;

namespace Jarvis.Api.Endpoints;

public sealed record ChannelDto(Guid Id, string Kind, string DisplayName, string Account, bool Enabled,
    IReadOnlyList<string> AllowedSenders, bool ForwardNotifications, string? NotifyRecipient,
    IReadOnlyList<string> ConfiguredSecrets, string? WebhookUrl, DateTimeOffset? LastInboundAt,
    DateTimeOffset? LastOutboundAt, string? LastError, DateTimeOffset CreatedAt);
public sealed record ChannelTestRequest(string? Recipient);
public sealed record SignalStatusDto(bool Configured, IReadOnlyList<string> Accounts);
public sealed record ChannelLinkRequest(string? Kind, Guid? ChannelId);
public sealed record ChannelProvidersDto(bool WhatsAppLink, bool Signal, bool WhatsAppCloud);

internal static class ChannelEndpoints
{
    public static RouteGroupBuilder MapChannelEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        var channels = api.MapGroup("/channels");

        channels.MapGet("", async (IChannelRepository repository, IIntegrationCredentialStore credentials,
            ChannelOptions options, HttpRequest http, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var list = await repository.ListAsync(currentUser.OwnerId, ct);
            var dtos = new List<ChannelDto>();
            foreach (var connection in list)
                dtos.Add(await ToDtoAsync(connection, credentials, options, http, ct));
            return Results.Ok(dtos);
        }).WithName("ListChannels");

        channels.MapPost("", async (SaveChannelRequest request, IChannelRepository repository,
            IIntegrationCredentialStore credentials, IAuditEventStore audit, ChannelOptions options, HttpRequest http,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            SaveChannelRequest normalized;
            try { normalized = ChannelValidation.Normalize(request, creating: true); }
            catch (ArgumentException exception) { return EndpointHelpers.Invalid("channel", exception.Message); }
            if (normalized.Kind == ChannelKinds.WhatsAppLinked)
                return EndpointHelpers.Invalid("channel", "Link WhatsApp by scanning a QR code (POST /channels/link).");
            if (normalized.Kind == ChannelKinds.Signal && string.IsNullOrWhiteSpace(options.SignalBaseUrl))
                return EndpointHelpers.Invalid("channel",
                    "Signal needs the signal-cli service. Set Channels:Signal:BaseUrl on the server first.");
            ChannelConnectionRecord created;
            try { created = await repository.CreateAsync(currentUser.OwnerId, normalized, ct); }
            catch (DbUpdateException) { return Results.Conflict(new { message = "That account is already connected." }); }
            await SaveSecretsAsync(credentials, currentUser.OwnerId, created.Id, normalized.Secrets, ct);
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "channels",
                "channel.connected", "high", true, null,
                JsonSerializer.Serialize(new { resourceId = created.Id, created.Kind }), ct);
            return Results.Created($"/api/v1/channels/{created.Id}",
                await ToDtoAsync(created, credentials, options, http, ct));
        }).WithName("CreateChannel");

        channels.MapPut("/{id:guid}", async (Guid id, SaveChannelRequest request, IChannelRepository repository,
            IIntegrationCredentialStore credentials, ChannelOptions options, HttpRequest http,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var existing = await repository.GetAsync(currentUser.OwnerId, id, ct);
            if (existing is null) return Results.NotFound();
            SaveChannelRequest normalized;
            try { normalized = ChannelValidation.Normalize(request with { Kind = existing.Kind }, creating: false); }
            catch (ArgumentException exception) { return EndpointHelpers.Invalid("channel", exception.Message); }
            ChannelConnectionRecord? updated;
            try { updated = await repository.UpdateAsync(currentUser.OwnerId, id, normalized, ct); }
            catch (DbUpdateException) { return Results.Conflict(new { message = "That account is already connected." }); }
            await SaveSecretsAsync(credentials, currentUser.OwnerId, id, normalized.Secrets, ct);
            return updated is null ? Results.NotFound() : Results.Ok(await ToDtoAsync(updated, credentials, options, http, ct));
        }).WithName("UpdateChannel");

        channels.MapDelete("/{id:guid}", async (Guid id, IChannelRepository repository,
            IIntegrationCredentialStore credentials, IAuditEventStore audit, WhatsAppBridgeClient bridge,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var existing = await repository.GetAsync(currentUser.OwnerId, id, ct);
            if (existing is null || !await repository.DeleteAsync(currentUser.OwnerId, id, ct)) return Results.NotFound();
            await credentials.DeleteAsync(currentUser.OwnerId, ChannelMessenger.SecretsProvider(id), ct);
            if (existing.Kind == ChannelKinds.WhatsAppLinked && bridge.Configured)
            {
                // Unlinks this device from the phone's "Linked devices" list; a dead bridge must not block removal.
                try { await bridge.DeleteAsync(id, ct); }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    logger.LogWarning(exception, "Could not unlink WhatsApp session {ConnectionId}.", id);
                }
            }
            await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "channels",
                "channel.disconnected", "moderate", true, null, JsonSerializer.Serialize(new { resourceId = id }), ct);
            return Results.NoContent();
        }).WithName("DeleteChannel");

        channels.MapPost("/{id:guid}/test", async (Guid id, ChannelTestRequest request, IChannelRepository repository,
            ChannelMessenger messenger, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var connection = await repository.GetAsync(currentUser.OwnerId, id, ct);
            if (connection is null) return Results.NotFound();
            var recipient = request.Recipient is { Length: > 0 } requested
                ? ChannelAddresses.Normalize(requested)
                : connection.NotifyRecipient ?? connection.AllowedSenders.First();
            if (!ChannelAddresses.IsAllowed(connection, recipient))
                return EndpointHelpers.Invalid("recipient", "Send tests only to an allowed phone number.");
            var sent = await messenger.SendAsync(connection, recipient,
                "👋 Hi from **Jarvis**! This channel is connected. Reply here to chat, or send /help.", ct);
            var refreshed = await repository.GetAsync(currentUser.OwnerId, id, ct);
            return Results.Ok(new { sent, error = sent ? null : refreshed?.LastError });
        }).WithName("TestChannel");

        channels.MapGet("/{id:guid}/messages", async (Guid id, IChannelRepository repository,
                ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok(await repository.ListMessagesAsync(currentUser.OwnerId, id, 50, ct)))
            .WithName("ListChannelMessages");

        channels.MapGet("/{id:guid}/threads", async (Guid id, IChannelRepository repository,
                ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok(await repository.ListThreadsAsync(currentUser.OwnerId, id, ct)))
            .WithName("ListChannelThreads");

        channels.MapGet("/{id:guid}/threads/{peer}/messages", async (Guid id, string peer,
                IChannelRepository repository, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok(await repository.ListThreadMessagesAsync(currentUser.OwnerId, id,
                    WebUtility.UrlDecode(peer), 100, ct)))
            .WithName("ListChannelThreadMessages");

        channels.MapGet("/providers", (ChannelLinkService links) =>
            Results.Ok(new ChannelProvidersDto(links.WhatsAppAvailable, links.SignalAvailable, true)))
            .WithName("GetChannelProviders");

        channels.MapPost("/link", async (ChannelLinkRequest request, ChannelLinkService links,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var kind = request.Kind?.Trim().ToLowerInvariant();
            if (kind == ChannelKinds.WhatsApp) kind = ChannelKinds.WhatsAppLinked;
            if (kind is not (ChannelKinds.WhatsAppLinked or ChannelKinds.Signal))
                return EndpointHelpers.Invalid("kind", "Choose whatsapp or signal.");
            try
            {
                var status = await links.StartAsync(currentUser.OwnerId, kind, request.ChannelId, ct);
                return status is null ? Results.NotFound() : Results.Ok(status);
            }
            catch (ChannelLinkUnavailableException exception)
            {
                return ApiProblemResults.DependencyUnavailable(exception.Message);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                return ApiProblemResults.BadGateway($"The {ChannelKinds.Label(kind)} link service is not reachable.");
            }
        }).WithName("StartChannelLink");

        channels.MapGet("/link/{linkId:guid}", async (Guid linkId, ChannelLinkService links,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            try
            {
                var status = await links.GetAsync(currentUser.OwnerId, linkId, ct);
                return status is null ? Results.NotFound() : Results.Ok(status);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                return ApiProblemResults.BadGateway("The link service is not reachable.");
            }
        }).WithName("GetChannelLink");

        channels.MapGet("/signal/status", async (ChannelOptions options, IHttpClientFactory httpClients,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(options.SignalBaseUrl)) return Results.Ok(new SignalStatusDto(false, []));
            try
            {
                var accounts = await httpClients.CreateClient("signal")
                    .GetFromJsonAsync<string[]>($"{options.SignalBaseUrl.TrimEnd('/')}/v1/accounts", ct) ?? [];
                return Results.Ok(new SignalStatusDto(true, accounts));
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                return ApiProblemResults.BadGateway("The signal-cli service is not reachable.");
            }
        }).WithName("GetSignalStatus");

        channels.MapGet("/signal/link", async (ChannelOptions options, IHttpClientFactory httpClients,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(options.SignalBaseUrl))
                return ApiProblemResults.DependencyUnavailable("Signal is not configured on this server.");
            using var response = await httpClients.CreateClient("signal")
                .GetAsync($"{options.SignalBaseUrl.TrimEnd('/')}/v1/qrcodelink?device_name=Jarvis", ct);
            if (!response.IsSuccessStatusCode)
                return ApiProblemResults.BadGateway("signal-cli could not create a link code.");
            return Results.File(await response.Content.ReadAsByteArrayAsync(ct), "image/png");
        }).WithName("GetSignalLinkCode");

        channels.MapGet("/whatsapp/{key}/webhook", async (string key, HttpRequest request,
            IChannelRepository repository, IIntegrationCredentialStore credentials, CancellationToken ct) =>
        {
            var connection = await repository.FindByWebhookKeyAsync(key, ct);
            if (connection is not { Kind: ChannelKinds.WhatsApp }) return Results.NotFound();
            var secrets = await credentials.GetSecretsAsync(connection.OwnerId,
                ChannelMessenger.SecretsProvider(connection.Id), ct);
            var expected = secrets?.GetValueOrDefault("verify_token");
            if (request.Query["hub.mode"] != "subscribe" ||
                !SecretComparer.FixedTimeEquals(expected, request.Query["hub.verify_token"].ToString()))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Text(request.Query["hub.challenge"].ToString(), "text/plain");
        }).WithName("VerifyWhatsAppWebhook").AllowAnonymous();

        channels.MapPost("/whatsapp/{key}/webhook", async (string key, HttpRequest request,
            IChannelRepository repository, IIntegrationCredentialStore credentials, CancellationToken ct) =>
        {
            var connection = await repository.FindByWebhookKeyAsync(key, ct);
            if (connection is not { Kind: ChannelKinds.WhatsApp }) return Results.NotFound();
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length > 1_000_000) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var secrets = await credentials.GetSecretsAsync(connection.OwnerId,
                ChannelMessenger.SecretsProvider(connection.Id), ct);
            if (!WhatsAppWebhook.HasValidSignature(request.Headers["X-Hub-Signature-256"], buffer.ToArray(),
                    secrets?.GetValueOrDefault("app_secret")))
                return Results.Unauthorized();
            if (!connection.Enabled) return Results.Ok();
            using var document = JsonDocument.Parse(buffer.ToArray());
            foreach (var (sender, text, externalId) in WhatsAppWebhook.ParseMessages(document.RootElement,
                         connection.Account))
                await repository.EnqueueInboundAsync(connection.Id, sender, text, externalId, ct);
            return Results.Ok();
        }).WithName("ReceiveWhatsAppWebhook").AllowAnonymous().DisableAntiforgery();

        return api;
    }

    private static async Task SaveSecretsAsync(IIntegrationCredentialStore credentials, Guid ownerId, Guid connectionId,
        IReadOnlyDictionary<string, string>? secrets, CancellationToken ct)
    {
        foreach (var (name, value) in secrets ?? new Dictionary<string, string>())
            await credentials.SaveSecretAsync(ownerId, ChannelMessenger.SecretsProvider(connectionId), name, value, ct);
    }

    private static async Task<ChannelDto> ToDtoAsync(ChannelConnectionRecord connection,
        IIntegrationCredentialStore credentials, ChannelOptions options, HttpRequest http, CancellationToken ct)
    {
        var status = await credentials.GetStatusAsync(connection.OwnerId,
            ChannelMessenger.SecretsProvider(connection.Id), ct);
        var baseUrl = options.PublicBaseUrl?.TrimEnd('/') ?? $"{http.Scheme}://{http.Host}";
        return new ChannelDto(connection.Id, connection.Kind, connection.DisplayName, connection.Account,
            connection.Enabled, connection.AllowedSenders, connection.ForwardNotifications, connection.NotifyRecipient,
            status?.SecretNames ?? [],
            connection.Kind == ChannelKinds.WhatsApp
                ? $"{baseUrl}/api/v1/channels/whatsapp/{connection.WebhookKey}/webhook"
                : null,
            connection.LastInboundAt, connection.LastOutboundAt, connection.LastError, connection.CreatedAt);
    }
}
