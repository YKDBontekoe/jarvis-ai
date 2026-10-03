using System.Collections.Concurrent;
using Jarvis.Application.Integrations;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Modes;

public sealed class ModeService(IOwnerSettingsStore settings, IDailyBriefingRepository briefings,
    ICalendarFeed? calendar = null, TimeProvider? timeProvider = null) : IModeService
{
    public const int MaxManualMinutes = 24 * 60;
    private static readonly TimeSpan CalendarCacheLifetime = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<Guid, (DateTimeOffset At, CalendarEventRecord? Current)> CalendarCache = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ModeState> GetStateAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var stored = await LoadAsync(ownerId, cancellationToken);
        return await BuildAsync(ownerId, stored, cancellationToken);
    }

    public async Task<ModeOperation<ModeState>> SetModeAsync(Guid ownerId, string? mode, int? minutes,
        CancellationToken cancellationToken)
    {
        var stored = await LoadAsync(ownerId, cancellationToken);
        var key = mode?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key) || key == "auto")
        {
            stored = stored with { Manual = null };
        }
        else
        {
            if (!ModeIds.IsValid(key))
                return ModeOperation<ModeState>.Invalid("mode", "Use one of: " + string.Join(", ", ModeIds.All) + ", or auto.");
            if (minutes is < 1 or > MaxManualMinutes)
                return ModeOperation<ModeState>.Invalid("minutes", $"Pick between 1 minute and {MaxManualMinutes / 60} hours.");
            var now = clock.GetUtcNow();
            stored = stored with { Manual = new ActiveModeState(key, now, minutes is { } m ? now.AddMinutes(m) : null) };
        }
        await settings.SaveAsync(ownerId, SettingsSections.Modes, stored, cancellationToken);
        return ModeOperation<ModeState>.Ok(await BuildAsync(ownerId, stored, cancellationToken));
    }

    public async Task<ModeOperation<ModeState>> SaveSettingsAsync(Guid ownerId, bool? auto, string? sleepStart,
        string? sleepEnd, CancellationToken cancellationToken)
    {
        if (sleepStart is not null && ModeInference.ParseTime(sleepStart) is null)
            return ModeOperation<ModeState>.Invalid("sleepStart", "Use the 24-hour format HH:mm.");
        if (sleepEnd is not null && ModeInference.ParseTime(sleepEnd) is null)
            return ModeOperation<ModeState>.Invalid("sleepEnd", "Use the 24-hour format HH:mm.");
        var stored = await LoadAsync(ownerId, cancellationToken);
        stored = stored with
        {
            Auto = auto ?? stored.Auto,
            SleepStart = sleepStart?.Trim() ?? stored.SleepStart,
            SleepEnd = sleepEnd?.Trim() ?? stored.SleepEnd
        };
        await settings.SaveAsync(ownerId, SettingsSections.Modes, stored, cancellationToken);
        return ModeOperation<ModeState>.Ok(await BuildAsync(ownerId, stored, cancellationToken));
    }

    public async Task<ModeOperation<ModeState>> SavePolicyAsync(Guid ownerId, string mode, string? notifications,
        string? tone, CancellationToken cancellationToken)
    {
        var key = mode.Trim().ToLowerInvariant();
        if (!ModeIds.IsValid(key)) return ModeOperation<ModeState>.Invalid("mode", "Unknown mode.");
        if (notifications is not null && !NotificationLevels.IsValid(notifications))
            return ModeOperation<ModeState>.Invalid("notifications", "Use all, important, or none.");
        var stored = await LoadAsync(ownerId, cancellationToken);
        var current = Effective(stored, key);
        var overrides = new Dictionary<string, ModePolicy>(stored.Overrides ?? [])
        {
            [key] = new ModePolicy(notifications ?? current.Notifications,
                tone is null ? current.Tone : ModeCatalog.CleanTone(tone))
        };
        stored = stored with { Overrides = overrides };
        await settings.SaveAsync(ownerId, SettingsSections.Modes, stored, cancellationToken);
        return ModeOperation<ModeState>.Ok(await BuildAsync(ownerId, stored, cancellationToken));
    }

    public async Task<ModeOperation<ModeState>> ResetPolicyAsync(Guid ownerId, string mode,
        CancellationToken cancellationToken)
    {
        var key = mode.Trim().ToLowerInvariant();
        if (!ModeIds.IsValid(key)) return ModeOperation<ModeState>.Invalid("mode", "Unknown mode.");
        var stored = await LoadAsync(ownerId, cancellationToken);
        var overrides = new Dictionary<string, ModePolicy>(stored.Overrides ?? []);
        overrides.Remove(key);
        stored = stored with { Overrides = overrides };
        await settings.SaveAsync(ownerId, SettingsSections.Modes, stored, cancellationToken);
        return ModeOperation<ModeState>.Ok(await BuildAsync(ownerId, stored, cancellationToken));
    }

    public async Task<bool> ShouldPushAsync(Guid ownerId, string notificationType, CancellationToken cancellationToken)
    {
        try
        {
            var state = await GetStateAsync(ownerId, cancellationToken);
            return NotificationLevels.Allows(state.Policy.Notifications, notificationType);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A broken settings row must never swallow notifications.
            return true;
        }
    }

    public static ModePolicy Effective(ModeSettings stored, string mode) =>
        stored.Overrides is { } overrides && overrides.TryGetValue(mode, out var custom)
            ? custom
            : ModeCatalog.Find(mode).Policy;

    private async Task<ModeSettings> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<ModeSettings>(ownerId, SettingsSections.Modes, cancellationToken) ?? ModeSettings.Default;

    private async Task<ModeState> BuildAsync(Guid ownerId, ModeSettings stored, CancellationToken cancellationToken)
    {
        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        var zone = LocalClock.TryFind(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        var now = clock.GetUtcNow();
        // Only the calendar costs a lookup, and only when automatic modes could use it.
        var current = stored.Auto && stored.Manual is null ? await CurrentEventAsync(ownerId, now, cancellationToken) : null;
        var decision = ModeInference.Decide(stored, new ModeSignals(now, zone, current));
        var modes = ModeCatalog.Defaults.Select(d =>
        {
            var policy = Effective(stored, d.Id);
            return new EffectiveMode(d with { Policy = policy }, stored.Overrides?.ContainsKey(d.Id) == true);
        }).ToArray();
        return new ModeState(decision, Effective(stored, decision.Mode), modes, stored.Auto, stored.SleepStart,
            stored.SleepEnd);
    }

    private async Task<CalendarEventRecord?> CurrentEventAsync(Guid ownerId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (calendar is null) return null;
        if (CalendarCache.TryGetValue(ownerId, out var cached) && now - cached.At < CalendarCacheLifetime)
            return cached.Current is { } running && ModeInference.IsMeeting(running, now) ? running : null;
        try
        {
            var events = await calendar.ListUpcomingAsync(ownerId, now - ModeInference.MaxMeeting, now.AddMinutes(1),
                cancellationToken);
            var current = events.Where(x => ModeInference.IsMeeting(x, now)).OrderBy(x => x.StartAt).FirstOrDefault();
            CalendarCache[ownerId] = (now, current);
            return current;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CalendarCache[ownerId] = (now, null);
            return null;
        }
    }
}
