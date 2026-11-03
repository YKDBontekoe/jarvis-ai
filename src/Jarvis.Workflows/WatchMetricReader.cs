using Jarvis.Application.Devices;
using Jarvis.Application.Integrations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;

namespace Jarvis.Workflows;

public sealed class CalendarFeed(IIntegrationCredentialStore credentials, CalendarFeedCache cache) : ICalendarFeed
{
    public async Task<IReadOnlyList<CalendarEventRecord>> ListUpcomingAsync(Guid ownerId, DateTimeOffset from,
        DateTimeOffset until, CancellationToken cancellationToken)
    {
        var secrets = await credentials.GetSecretsAsync(ownerId, IntegrationPackIds.Provider(IntegrationPackIds.Calendar),
            cancellationToken);
        if (secrets is null) return [];
        var url = secrets.GetValueOrDefault("ics_url");
        if (string.IsNullOrWhiteSpace(url)) return [];
        var token = secrets.GetValueOrDefault("token") ?? secrets.GetValueOrDefault("ics_token");
        var header = string.IsNullOrWhiteSpace(token) ? null
            : token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : "Bearer " + token;
        var ics = await cache.GetAsync(ownerId, url, header, async ct =>
        {
            await using var stream = await PublicJsonMetricReader.FetchPublicHttpsAsync(url,
                "text/calendar, text/plain, */*", header, ct);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }, cancellationToken);
        return IcsCalendarParser.Parse(ics, from, until);
    }
}

/// <summary>
/// Keeps each owner's downloaded calendar for a few minutes. Home, the briefing, the planner, modes, meetings and
/// watches all read the feed, often several times in one request; without this each read downloaded it again, so
/// Home waited on the calendar provider every time it opened. Concurrent reads share one download, and a failed
/// download is remembered briefly so a slow or broken feed is not retried on every request.
/// </summary>
public sealed class CalendarFeedCache(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(1);
    private const int MaxEntries = 1_000;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Entry> _entries = new();

    private sealed record Entry(Task<string> Download, DateTimeOffset StartedAt);

    public async Task<string> GetAsync(Guid ownerId, string url, string? authorization,
        Func<CancellationToken, Task<string>> download, CancellationToken cancellationToken)
    {
        // The key includes the URL and credential, so changing the calendar settings takes effect at once.
        var key = ownerId.ToString("N") + "\n" + url + "\n" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(authorization ?? "")));
        var now = _time.GetUtcNow();
        while (true)
        {
            if (_entries.TryGetValue(key, out var entry) && Fresh(entry, now))
                return await entry.Download.WaitAsync(cancellationToken);
            if (_entries.Count >= MaxEntries) Prune(now);
            // Not tied to one caller's cancellation: other callers may be waiting on the same download.
            var created = new Entry(download(CancellationToken.None), now);
            if (entry is null ? _entries.TryAdd(key, created) : _entries.TryUpdate(key, created, entry))
                return await created.Download.WaitAsync(cancellationToken);
        }
    }

    private bool Fresh(Entry entry, DateTimeOffset now)
    {
        if (!entry.Download.IsCompleted) return true;
        var lifetime = entry.Download.IsCompletedSuccessfully ? Lifetime : FailureLifetime;
        return now - entry.StartedAt < lifetime;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, entry) in _entries)
            if (!Fresh(entry, now)) _entries.TryRemove(key, out _);
    }
}

public sealed class WatchMetricReader(
    PublicJsonMetricReader json,
    IIntegrationCredentialStore credentials,
    IDeviceTelemetryStore telemetry,
    ICalendarFeed calendar)
{
    public async Task<double> ReadAsync(ConditionWatchRecord watch, CancellationToken cancellationToken)
    {
        return WatchKinds.Normalize(watch.Kind) switch
        {
            WatchKinds.AuthenticatedJson => await json.ReadAsync(watch.Url, watch.JsonPath,
                await AuthorizationHeaderAsync(watch.OwnerId, watch.CredentialProvider, cancellationToken),
                cancellationToken),
            WatchKinds.DeviceBattery => await BatteryAsync(watch.OwnerId, cancellationToken),
            WatchKinds.DeviceLocation => await DistanceAsync(watch, cancellationToken),
            WatchKinds.Calendar => await MinutesUntilEventAsync(watch.OwnerId, cancellationToken),
            _ => await json.ReadAsync(watch.Url, watch.JsonPath, cancellationToken)
        };
    }

    private async Task<string?> AuthorizationHeaderAsync(Guid ownerId, string? provider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new InvalidDataException("Authenticated watch is missing its credential provider.");
        var secrets = await credentials.GetSecretsAsync(ownerId, provider, cancellationToken);
        var token = secrets?.GetValueOrDefault("token") ?? secrets?.GetValueOrDefault("access_token");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidDataException("Authenticated watch has no stored token.");
        return token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : "Bearer " + token;
    }

    private async Task<double> BatteryAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var snapshot = await telemetry.GetAsync(ownerId, cancellationToken)
                       ?? throw new InvalidDataException("No device battery snapshot yet. Open the Jarvis app.");
        if (snapshot.ReportedAt < DateTimeOffset.UtcNow.AddHours(-6))
            throw new InvalidDataException("The last battery reading is too old. Open the Jarvis app.");
        return snapshot.BatteryPercent ?? throw new InvalidDataException("Battery is not available on the last snapshot.");
    }

    private async Task<double> DistanceAsync(ConditionWatchRecord watch, CancellationToken cancellationToken)
    {
        var snapshot = await telemetry.GetAsync(watch.OwnerId, cancellationToken)
                       ?? throw new InvalidDataException("No device location snapshot yet. Open the Jarvis app.");
        if (snapshot.Latitude is null || snapshot.Longitude is null || watch.Latitude is null || watch.Longitude is null)
            throw new InvalidDataException("Location is not available.");
        return GeoDistance.Meters(snapshot.Latitude.Value, snapshot.Longitude.Value, watch.Latitude.Value,
            watch.Longitude.Value);
    }

    private async Task<double> MinutesUntilEventAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var events = await calendar.ListUpcomingAsync(ownerId, now, now.AddDays(2), cancellationToken);
        var next = events.FirstOrDefault();
        if (next is null) return double.PositiveInfinity;
        return (next.StartAt - now).TotalMinutes;
    }
}
