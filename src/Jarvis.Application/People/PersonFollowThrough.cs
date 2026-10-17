using Jarvis.Application.Inbox;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;
using Jarvis.Domain.People;

namespace Jarvis.Application.People;

public static class NudgeModes
{
    public const string On = "on";
    public const string Snoozed = "snoozed";
    public const string Muted = "muted";
}

/// <summary>How one person's suggestions are tuned. <see cref="Until"/> only matters while snoozed.</summary>
public sealed record PersonNudgePref(string Mode = NudgeModes.On, DateOnly? Until = null, int NotUsefulCount = 0);

/// <summary>Per-person nudge choices, kept in owner settings under <see cref="Section"/> (no table).</summary>
public sealed record PersonNudgeSettings(Dictionary<string, PersonNudgePref>? People = null)
{
    public const string Section = "people.nudges";

    public PersonNudgePref For(Guid personId) =>
        People is not null && People.TryGetValue(personId.ToString(), out var pref) ? pref : new PersonNudgePref();

    /// <summary>True when a nudge for this person should stay silent today.</summary>
    public bool IsQuiet(Guid personId, DateOnly today) => IsQuiet(For(personId), today);

    public static bool IsQuiet(PersonNudgePref pref, DateOnly today) => pref.Mode switch
    {
        NudgeModes.Muted => true,
        NudgeModes.Snoozed => pref.Until is { } until && until > today,
        _ => false
    };
}

public sealed record FollowThroughCadence(
    int? EveryDays,
    string Status,
    DateOnly? DueOn,
    int? DaysSinceContact,
    string Reason);

public sealed record FollowThroughBirthday(DateOnly Next, int DaysUntil, int? Turns);

public sealed record FollowThroughPromise(
    Guid Id,
    string Direction,
    string Description,
    DateOnly? DueOn,
    int DaysOverdue,
    string Ref);

public sealed record FollowThroughNudge(string Mode, DateOnly? Until, int NotUsefulCount, bool Quiet);

/// <summary>
/// One view of a person: birthday, keep-in-touch cadence and the accepted promises between the owner and them,
/// plus whether a nudge is worth sending now and why. <see cref="HasData"/> is false when there is nothing to base
/// a suggestion on, so the screen can say that instead of showing an empty card.
/// </summary>
public sealed record PersonFollowThrough(
    Guid PersonId,
    string Name,
    bool HasData,
    FollowThroughCadence Cadence,
    FollowThroughBirthday? Birthday,
    IReadOnlyList<FollowThroughPromise> Promises,
    FollowThroughNudge Nudge,
    IReadOnlyList<string> Reasons);

public static class PersonFollowThroughBuilder
{
    public const int MaxPromises = 8;

    /// <summary>
    /// Promises count for a person only when the counterparty is the same name once case and accents are ignored.
    /// A first name or nickname is never matched silently; the owner links a promise by writing the name.
    /// </summary>
    public static bool IsCounterparty(Person person, Commitment commitment) =>
        PeopleRules.NameKey(person.Name) is { Length: > 0 } key && PeopleRules.NameKey(commitment.Counterparty) == key;

    public static PersonFollowThrough Build(Person person, DateOnly today, TimeZoneInfo zone,
        IEnumerable<Commitment> commitments, PersonNudgePref pref)
    {
        var promises = commitments
            .Where(item => item is { Suggested: false, Status: CommitmentStatuses.Open } &&
                           item.OwnerId == person.OwnerId && IsCounterparty(person, item))
            .OrderBy(item => item.DueOn ?? DateOnly.MaxValue)
            .ThenBy(item => item.CreatedAt)
            .Take(MaxPromises)
            .Select(item => new FollowThroughPromise(item.Id, item.Direction, item.Description, item.DueOn,
                item.DueOn is { } due ? Math.Max(0, today.DayNumber - due.DayNumber) : 0,
                $"commitment:{item.Id}"))
            .ToArray();

        var cadence = Cadence(person, today, zone);
        var birthday = PeopleCalendar.NextBirthday(person, today) is { } next
            ? new FollowThroughBirthday(next, next.DayNumber - today.DayNumber, PeopleCalendar.AgeOn(person, next))
            : null;
        var quiet = PersonNudgeSettings.IsQuiet(pref, today);

        var reasons = new List<string>();
        if (cadence.Status == "due") reasons.Add(cadence.Reason);
        if (birthday is { DaysUntil: <= 7 })
            reasons.Add(birthday.DaysUntil == 0
                ? $"It is {person.Name}'s birthday today."
                : $"{person.Name}'s birthday is in {birthday.DaysUntil} day{(birthday.DaysUntil == 1 ? "" : "s")}.");
        foreach (var promise in promises.Where(p => p.DaysOverdue > 0).Take(2))
            reasons.Add(promise.Direction == CommitmentDirections.IOwe
                ? $"You owe {person.Name}: {promise.Description} ({promise.DaysOverdue} days late)."
                : $"{person.Name} owes you: {promise.Description} ({promise.DaysOverdue} days late).");

        return new PersonFollowThrough(person.Id, person.Name,
            cadence.EveryDays is not null || birthday is not null || promises.Length > 0 ||
            cadence.DaysSinceContact is not null,
            cadence, birthday, promises, new FollowThroughNudge(pref.Mode, pref.Until, pref.NotUsefulCount, quiet),
            reasons);
    }

    private static FollowThroughCadence Cadence(Person person, DateOnly today, TimeZoneInfo zone)
    {
        var since = PeopleCalendar.DaysSinceContact(person, today, zone);
        if (person.ContactEveryDays is not { } every)
            return new FollowThroughCadence(null, "none", null, since,
                since is { } days
                    ? $"No keep-in-touch rhythm is set. You last talked {PeopleCalendar.DescribeDaysAgo(days)}."
                    : "No keep-in-touch rhythm is set and no contact is recorded, so there is nothing to base a nudge on.");
        var due = PeopleCalendar.ContactDueOn(person, zone)!.Value;
        var cadence = PeopleCalendar.DescribeCadence(every);
        if (due <= today)
            return new FollowThroughCadence(every, "due", due, since,
                since is { } late
                    ? $"You last talked {PeopleCalendar.DescribeDaysAgo(late)} and wanted to stay in touch {cadence}."
                    : $"You wanted to stay in touch {cadence} and no contact is recorded yet.");
        return new FollowThroughCadence(every, "ok", due, since,
            since is { } recent
                ? $"You talked {PeopleCalendar.DescribeDaysAgo(recent)}, so staying in touch {cadence} is covered until {due:d MMM}."
                : $"Staying in touch {cadence} starts counting from when {person.Name} was added; next check-in {due:d MMM}.");
    }
}

public interface IPersonFollowThroughService
{
    /// <summary>Null when the person does not exist for this owner.</summary>
    Task<PersonFollowThrough?> GetAsync(Guid ownerId, Guid personId, CancellationToken cancellationToken);

    /// <summary>Turn suggestions on, snooze them for 1–365 days, or mute them. Null when the person is not found.</summary>
    Task<PeopleOperation<PersonFollowThrough>> SetNudgeAsync(Guid ownerId, Guid personId, string mode,
        int? snoozeDays, CancellationToken cancellationToken);

    /// <summary>"Not useful": a month of quiet and a count, undone by turning suggestions on again.</summary>
    Task<PeopleOperation<PersonFollowThrough>> NotUsefulAsync(Guid ownerId, Guid personId,
        CancellationToken cancellationToken);
}

public sealed class PersonFollowThroughService(
    IPeopleRepository people,
    ICommitmentService commitments,
    IOwnerSettingsStore settings,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : IPersonFollowThroughService
{
    public const int NotUsefulQuietDays = 30;

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<PersonFollowThrough?> GetAsync(Guid ownerId, Guid personId, CancellationToken cancellationToken)
    {
        var person = await people.GetAsync(personId, ownerId, cancellationToken);
        if (person is null) return null;
        var (today, zone) = await TodayAsync(ownerId, cancellationToken);
        var prefs = await settings.GetAsync<PersonNudgeSettings>(ownerId, PersonNudgeSettings.Section,
            cancellationToken) ?? new PersonNudgeSettings();
        var open = await commitments.ListAsync(ownerId, null, CommitmentStatuses.Open, false, cancellationToken);
        return PersonFollowThroughBuilder.Build(person, today, zone, open, prefs.For(personId));
    }

    public async Task<PeopleOperation<PersonFollowThrough>> SetNudgeAsync(Guid ownerId, Guid personId, string mode,
        int? snoozeDays, CancellationToken cancellationToken)
    {
        if (mode is not (NudgeModes.On or NudgeModes.Snoozed or NudgeModes.Muted))
            return PeopleOperation<PersonFollowThrough>.Invalid("mode", "Choose on, snoozed or muted.");
        if (mode == NudgeModes.Snoozed && snoozeDays is not (>= 1 and <= 365))
            return PeopleOperation<PersonFollowThrough>.Invalid("snoozeDays", "Snooze for 1 to 365 days.");
        var person = await people.GetAsync(personId, ownerId, cancellationToken);
        if (person is null) return PeopleOperation<PersonFollowThrough>.NotFound();
        var (today, _) = await TodayAsync(ownerId, cancellationToken);
        var current = await LoadAsync(ownerId, cancellationToken);
        var pref = current.For(personId) with
        {
            Mode = mode,
            Until = mode == NudgeModes.Snoozed ? today.AddDays(snoozeDays!.Value) : null
        };
        await SaveAsync(ownerId, current, personId, pref, cancellationToken);
        return PeopleOperation<PersonFollowThrough>.Ok((await GetAsync(ownerId, personId, cancellationToken))!);
    }

    public async Task<PeopleOperation<PersonFollowThrough>> NotUsefulAsync(Guid ownerId, Guid personId,
        CancellationToken cancellationToken)
    {
        var person = await people.GetAsync(personId, ownerId, cancellationToken);
        if (person is null) return PeopleOperation<PersonFollowThrough>.NotFound();
        var (today, _) = await TodayAsync(ownerId, cancellationToken);
        var current = await LoadAsync(ownerId, cancellationToken);
        var before = current.For(personId);
        // Repeated "not useful" quiets for longer, but never beyond a year, and a mute stays a mute.
        var days = Math.Min(365, NotUsefulQuietDays * (before.NotUsefulCount + 1));
        var pref = before.Mode == NudgeModes.Muted
            ? before with { NotUsefulCount = before.NotUsefulCount + 1 }
            : new PersonNudgePref(NudgeModes.Snoozed, today.AddDays(days), before.NotUsefulCount + 1);
        await SaveAsync(ownerId, current, personId, pref, cancellationToken);
        return PeopleOperation<PersonFollowThrough>.Ok((await GetAsync(ownerId, personId, cancellationToken))!);
    }

    private async Task<PersonNudgeSettings> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<PersonNudgeSettings>(ownerId, PersonNudgeSettings.Section, cancellationToken) ??
        new PersonNudgeSettings();

    private Task SaveAsync(Guid ownerId, PersonNudgeSettings current, Guid personId, PersonNudgePref pref,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, PersonNudgePref>(current.People ?? []);
        // Back on with nothing to remember is the default; drop the row so the section stays small.
        if (pref is { Mode: NudgeModes.On, NotUsefulCount: 0 }) map.Remove(personId.ToString());
        else map[personId.ToString()] = pref;
        return settings.SaveAsync(ownerId, PersonNudgeSettings.Section, new PersonNudgeSettings(map),
            cancellationToken);
    }

    private async Task<(DateOnly Today, TimeZoneInfo Zone)> TodayAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        var zone = LocalClock.TryFind(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        return (PeopleCalendar.LocalDate(clock.GetUtcNow(), zone), zone);
    }
}
