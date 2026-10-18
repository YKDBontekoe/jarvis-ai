using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Preferences;

/// <summary>Where an owner's time zone came from, so a fallback is never mistaken for a choice.</summary>
public static class ZoneSources
{
    /// <summary>The owner picked it in preferences.</summary>
    public const string Chosen = "chosen";

    /// <summary>Borrowed from the morning brief's saved zone; the owner never picked a home zone.</summary>
    public const string Brief = "brief";

    /// <summary>Nothing was chosen anywhere, so UTC is used. This is not the owner's choice.</summary>
    public const string Default = "default";

    /// <summary>A schedule or action carries its own zone.</summary>
    public const string Schedule = "schedule";
}

/// <summary>Owner settings section <see cref="Section"/>: only what the owner explicitly chose.</summary>
public sealed record HomeZoneSettings(string? TimeZoneId = null)
{
    public const string Section = "preferences.home";
}

public sealed record OwnerZone(TimeZoneInfo Zone, string Id, string Source)
{
    public bool Chosen => Source == ZoneSources.Chosen;
}

public sealed record ZoneChoice(string Id, string Source);

public static class ZonePolicy
{
    /// <summary>
    /// One precedence for every feature: the zone a single action names, else the zone a saved schedule carries,
    /// else the owner's home zone (chosen, then borrowed from the brief, then UTC as an admitted default). A
    /// device's zone is only ever offered to the owner; it never silently overrides anything here.
    /// </summary>
    public static ZoneChoice Resolve(string? actionZone, string? scheduleZone, OwnerZone home)
    {
        if (LocalClock.TryFind(actionZone, out _) && !string.IsNullOrWhiteSpace(actionZone))
            return new ZoneChoice(actionZone.Trim(), ZoneSources.Schedule);
        if (LocalClock.TryFind(scheduleZone, out _) && !string.IsNullOrWhiteSpace(scheduleZone))
            return new ZoneChoice(scheduleZone.Trim(), ZoneSources.Schedule);
        return new ZoneChoice(home.Id, home.Source);
    }

    public static string? Validate(string? id)
    {
        var text = id?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 100) return null;
        if (text.Equals("UTC", StringComparison.OrdinalIgnoreCase)) return "UTC";
        return LocalClock.TryFind(text, out var zone) ? zone.Id : null;
    }
}

/// <summary>The one place that answers "what is this owner's home zone?".</summary>
public static class OwnerZoneResolver
{
    /// <summary>The home zone's id for features that only need the name: chosen, else the brief's, else UTC.</summary>
    public static async Task<string> ZoneIdAsync(IOwnerSettingsStore? settings, IDailyBriefingRepository briefings,
        Guid ownerId, CancellationToken cancellationToken) =>
        (await ResolveAsync(settings, briefings, ownerId, cancellationToken)).Id;

    public static async Task<OwnerZone> ResolveAsync(IOwnerSettingsStore? settings, IDailyBriefingRepository briefings,
        Guid ownerId, CancellationToken cancellationToken)
    {
        var chosen = settings is null
            ? null
            : (await settings.GetAsync<HomeZoneSettings>(ownerId, HomeZoneSettings.Section, cancellationToken))?.TimeZoneId;
        if (ZonePolicy.Validate(chosen) is { } chosenId && LocalClock.TryFind(chosenId, out var chosenZone))
            return new OwnerZone(chosenZone, chosenId, ZoneSources.Chosen);

        var briefZone = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        if (!string.IsNullOrWhiteSpace(briefZone) && ZonePolicy.Validate(briefZone) is { } briefId &&
            !briefId.Equals("UTC", StringComparison.Ordinal) && LocalClock.TryFind(briefId, out var found))
            return new OwnerZone(found, briefId, ZoneSources.Brief);
        return new OwnerZone(TimeZoneInfo.Utc, "UTC", ZoneSources.Default);
    }
}

public sealed record ZoneEffect(string Area, string Text);

public sealed record ZonePreview(string Current, string CurrentSource, string Proposed, string NowThere,
    IReadOnlyList<ZoneEffect> Effects);

public sealed record PreferencesView(string HomeTimeZone, string Source, bool Chosen, string LocalNow,
    string? Explanation);

public sealed record PreferenceOperation(bool Succeeded, PreferencesView? View = null, string? Field = null,
    string? Message = null);

public interface IOwnerPreferenceService
{
    Task<PreferencesView> GetAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Saves the home zone; null or empty goes back to "not chosen". Nothing already scheduled moves.</summary>
    Task<PreferenceOperation> SetHomeZoneAsync(Guid ownerId, string? timeZoneId, CancellationToken cancellationToken);

    /// <summary>What a change would do, without making it.</summary>
    Task<ZonePreview?> PreviewAsync(Guid ownerId, string? timeZoneId, CancellationToken cancellationToken);
}

public sealed class OwnerPreferenceService(
    IOwnerSettingsStore settings,
    IDailyBriefingRepository briefings,
    IReminderRepository reminders,
    TimeProvider? timeProvider = null) : IOwnerPreferenceService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<PreferencesView> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var home = await OwnerZoneResolver.ResolveAsync(settings, briefings, ownerId, cancellationToken);
        return ToView(home);
    }

    public async Task<PreferenceOperation> SetHomeZoneAsync(Guid ownerId, string? timeZoneId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            await settings.SaveAsync(ownerId, HomeZoneSettings.Section, new HomeZoneSettings(), cancellationToken);
            return new PreferenceOperation(true, await GetAsync(ownerId, cancellationToken));
        }
        if (ZonePolicy.Validate(timeZoneId) is not { } id)
            return new PreferenceOperation(false, null, "timeZoneId",
                "That is not a time zone Jarvis knows. Use a name like Europe/Amsterdam.");
        await settings.SaveAsync(ownerId, HomeZoneSettings.Section, new HomeZoneSettings(id), cancellationToken);
        return new PreferenceOperation(true, await GetAsync(ownerId, cancellationToken));
    }

    public async Task<ZonePreview?> PreviewAsync(Guid ownerId, string? timeZoneId, CancellationToken cancellationToken)
    {
        if (ZonePolicy.Validate(timeZoneId) is not { } id || !LocalClock.TryFind(id, out var proposed)) return null;
        var current = await OwnerZoneResolver.ResolveAsync(settings, briefings, ownerId, cancellationToken);
        var now = _clock.GetUtcNow();
        var effects = new List<ZoneEffect>
        {
            new("Your day", $"Today, \"this week\" and check-in times will follow {id}. The day starts and ends there.")
        };
        var brief = await briefings.GetAsync(ownerId, cancellationToken);
        if (brief is { Enabled: true })
            effects.Add(new ZoneEffect("Morning brief",
                $"Stays at {brief.LocalTime:HH:mm} in {brief.TimeZoneId}. It does not move unless you change it in the brief settings."));
        var recurring = (await reminders.ListRemindersAsync(ownerId, cancellationToken))
            .Where(r => r.Status == "scheduled" && r.Recurrence != "none").ToArray();
        if (recurring.Length > 0)
            effects.Add(new ZoneEffect("Repeating reminders",
                $"{recurring.Length} keep their own time zone and fire at the same local time as before."));
        effects.Add(new ZoneEffect("Reminders and tasks already scheduled",
            "Nothing already scheduled moves. A reminder with its own zone always uses that zone."));
        var there = TimeZoneInfo.ConvertTime(now, proposed);
        return new ZonePreview(current.Id, current.Source, id, there.ToString("ddd d MMM HH:mm"), effects);
    }

    private PreferencesView ToView(OwnerZone home)
    {
        var local = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), home.Zone);
        var explanation = home.Source switch
        {
            ZoneSources.Brief => "You never chose one, so Jarvis borrows the zone from your morning brief.",
            ZoneSources.Default => "You have not chosen one, so Jarvis uses UTC. Pick yours so days start where you are.",
            _ => null
        };
        return new PreferencesView(home.Id, home.Source, home.Chosen, local.ToString("ddd d MMM HH:mm"), explanation);
    }
}
