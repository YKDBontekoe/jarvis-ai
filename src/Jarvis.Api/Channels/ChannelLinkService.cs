using System.Collections.Concurrent;
using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Channels;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Api.Channels;

public static class ChannelLinkStates
{
    public const string Waiting = "waiting";
    public const string Linked = "linked";
    public const string Expired = "expired";
    public const string Failed = "failed";
}

/// <param name="QrImage">A <c>data:image/png;base64,…</c> URL the client shows for the user to scan.</param>
public sealed record ChannelLinkStatus(Guid LinkId, string Kind, string State, string? QrImage, string? Phone,
    Guid? ChannelId, string? Message);

/// <summary>The requested messenger cannot be linked on this server (bridge or signal-cli is not set up).</summary>
public sealed class ChannelLinkUnavailableException(string message) : Exception(message);

/// <summary>
/// One-tap linking for WhatsApp (Baileys bridge) and Signal (signal-cli): start an attempt, show the QR code, poll
/// until the phone scans it, then create the channel with the account's own number already allowlisted (so the
/// owner can chat with Jarvis from "Message yourself" right away).
/// </summary>
public sealed class ChannelLinkService(WhatsAppBridgeClient bridge, ChannelOptions options,
    IHttpClientFactory httpClients, IServiceScopeFactory scopes, ILogger<ChannelLinkService> logger)
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<Guid, Attempt> attempts = new();

    private sealed class Attempt(Guid id, Guid ownerId, string kind, Guid? existingChannelId,
        IReadOnlySet<string> baselineAccounts, string? signalQr, bool readAlong)
    {
        public Guid Id { get; } = id;
        public Guid OwnerId { get; } = ownerId;
        public string Kind { get; } = kind;
        public Guid? ExistingChannelId { get; } = existingChannelId;
        public IReadOnlySet<string> BaselineAccounts { get; } = baselineAccounts;
        public string? SignalQr { get; } = signalQr;
        public bool ReadAlong { get; } = readAlong;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public ChannelLinkStatus? Final { get; set; }
    }

    public bool WhatsAppAvailable => bridge.Configured;
    public bool SignalAvailable => !string.IsNullOrWhiteSpace(options.SignalBaseUrl);

    /// <summary>Starts linking. Pass <paramref name="channelId"/> to re-link an existing channel.</summary>
    public async Task<ChannelLinkStatus?> StartAsync(Guid ownerId, string kind, Guid? channelId,
        CancellationToken cancellationToken, bool readAlong = false)
    {
        if (kind is not (ChannelKinds.WhatsAppLinked or ChannelKinds.Signal))
            throw new ArgumentException("Only WhatsApp and Signal can be linked with a QR code.");
        if (readAlong && (kind != ChannelKinds.WhatsAppLinked || channelId is not null))
            throw new ArgumentException("Read along setup needs a new linked WhatsApp account.");
        Purge();

        Guid linkId;
        IReadOnlySet<string> baseline = new HashSet<string>();
        string? signalQr = null;
        if (channelId is { } existingId)
        {
            await using var scope = scopes.CreateAsyncScope();
            var existing = await scope.ServiceProvider.GetRequiredService<IChannelRepository>()
                .GetAsync(ownerId, existingId, cancellationToken);
            if (existing is null || existing.Kind != kind) return null;
        }

        if (kind == ChannelKinds.WhatsAppLinked)
        {
            if (!bridge.Configured)
                throw new ChannelLinkUnavailableException(
                    "WhatsApp linking is not set up on this server. Start the whatsapp-bridge service.");
            linkId = channelId ?? Guid.CreateVersion7();
            await bridge.StartAsync(linkId, cancellationToken);
        }
        else
        {
            if (!SignalAvailable)
                throw new ChannelLinkUnavailableException("Signal needs the signal-cli service on this server.");
            linkId = Guid.CreateVersion7();
            baseline = (await ListSignalAccountsAsync(cancellationToken)).ToHashSet();
            signalQr = await CreateSignalQrAsync(cancellationToken);
        }

        attempts[linkId] = new Attempt(linkId, ownerId, kind, channelId, baseline, signalQr, readAlong);
        return await GetAsync(ownerId, linkId, cancellationToken);
    }

    public async Task<ChannelLinkStatus?> GetAsync(Guid ownerId, Guid linkId, CancellationToken cancellationToken)
    {
        if (!attempts.TryGetValue(linkId, out var attempt) || attempt.OwnerId != ownerId) return null;
        await attempt.Gate.WaitAsync(cancellationToken);
        try
        {
            if (attempt.Final is { } final) return final;
            if (DateTimeOffset.UtcNow - attempt.StartedAt > Lifetime)
                return await FinishAsync(attempt, ChannelLinkStates.Expired, null, null, null,
                    "The code expired. Get a new one.", cleanup: true, cancellationToken);
            return attempt.Kind == ChannelKinds.WhatsAppLinked
                ? await PollWhatsAppAsync(attempt, cancellationToken)
                : await PollSignalAsync(attempt, cancellationToken);
        }
        finally
        {
            attempt.Gate.Release();
        }
    }

    private async Task<ChannelLinkStatus> PollWhatsAppAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        var status = await bridge.GetStatusAsync(attempt.Id, cancellationToken);
        switch (status.State)
        {
            case "open":
                if (string.IsNullOrWhiteSpace(status.Phone))
                    return await FinishAsync(attempt, ChannelLinkStates.Failed, null, null, null,
                        "WhatsApp linked, but the phone number could not be read. Try again.", cleanup: true,
                        cancellationToken);
                return await CompleteAsync(attempt, ChannelAddresses.Normalize(status.Phone), cancellationToken);
            case "logged_out":
            case "none":
                // The bridge forgot this session (phone unlinked it, or its data was wiped): start over with a new QR.
                status = await bridge.StartAsync(attempt.Id, cancellationToken);
                break;
        }
        return new ChannelLinkStatus(attempt.Id, attempt.Kind, ChannelLinkStates.Waiting,
            status.State == "qr" ? status.Qr : null, null, null, null);
    }

    private async Task<ChannelLinkStatus> PollSignalAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        var accounts = await ListSignalAccountsAsync(cancellationToken);
        if (accounts.FirstOrDefault(account => !attempt.BaselineAccounts.Contains(account)) is { } linked)
            return await CompleteAsync(attempt, linked, cancellationToken);
        return new ChannelLinkStatus(attempt.Id, attempt.Kind, ChannelLinkStates.Waiting, attempt.SignalQr, null,
            null, null);
    }

    private async Task<ChannelLinkStatus> CompleteAsync(Attempt attempt, string phone,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
        if (attempt.ExistingChannelId is { } existingId)
            return await FinishAsync(attempt, ChannelLinkStates.Linked, phone, existingId, null, null,
                cleanup: false, cancellationToken);

        var request = attempt.ReadAlong
            ? new SaveChannelRequest(attempt.Kind, "My WhatsApp", phone, true, [], false, null, null)
            : new SaveChannelRequest(attempt.Kind, ChannelKinds.Label(attempt.Kind), phone, true, [phone],
                true, phone, null);
        try
        {
            // The bridge session id doubles as the connection id, so sending and receiving need no extra mapping.
            var created = await repository.CreateAsync(attempt.OwnerId, request, cancellationToken,
                attempt.Kind == ChannelKinds.WhatsAppLinked ? attempt.Id : null);
            await Endpoints.EndpointHelpers.TryAppendAuditAsync(
                scope.ServiceProvider.GetRequiredService<IAuditEventStore>(), logger, attempt.OwnerId, "channels",
                "channel.connected", "high", true, null,
                JsonSerializer.Serialize(new { resourceId = created.Id, created.Kind, linked = true }),
                cancellationToken);
            return await FinishAsync(attempt, ChannelLinkStates.Linked, phone, created.Id, null, null,
                cleanup: false, cancellationToken);
        }
        catch (DbUpdateException)
        {
            return await FinishAsync(attempt, ChannelLinkStates.Failed, phone, null, null,
                "That account is already connected.", cleanup: attempt.Kind == ChannelKinds.WhatsAppLinked,
                cancellationToken);
        }
    }

    private async Task<ChannelLinkStatus> FinishAsync(Attempt attempt, string state, string? phone, Guid? channelId,
        string? qr, string? message, bool cleanup, CancellationToken cancellationToken)
    {
        if (cleanup && attempt.Kind == ChannelKinds.WhatsAppLinked && attempt.ExistingChannelId is null)
        {
            try { await bridge.DeleteAsync(attempt.Id, cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(exception, "Could not remove an abandoned WhatsApp link session.");
            }
        }
        var final = new ChannelLinkStatus(attempt.Id, attempt.Kind, state, qr, phone, channelId, message);
        attempt.Final = final;
        return final;
    }

    private void Purge()
    {
        foreach (var (id, attempt) in attempts)
            if (DateTimeOffset.UtcNow - attempt.StartedAt > Lifetime * 2) attempts.TryRemove(id, out _);
    }

    private async Task<IReadOnlyList<string>> ListSignalAccountsAsync(CancellationToken cancellationToken)
    {
        var accounts = await httpClients.CreateClient("signal")
            .GetFromJsonAsync<string[]>($"{options.SignalBaseUrl!.TrimEnd('/')}/v1/accounts", cancellationToken) ?? [];
        return accounts.Select(ChannelAddresses.Normalize).Where(account => account.Length > 0).ToArray();
    }

    private async Task<string> CreateSignalQrAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClients.CreateClient("signal")
            .GetAsync($"{options.SignalBaseUrl!.TrimEnd('/')}/v1/qrcodelink?device_name=Jarvis", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("signal-cli could not create a link code.");
        return "data:image/png;base64," +
               Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }
}
