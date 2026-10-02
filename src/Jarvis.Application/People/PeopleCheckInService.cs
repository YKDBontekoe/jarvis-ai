using Jarvis.Application.Workflows;
using Jarvis.Domain.People;

namespace Jarvis.Application.People;

/// <summary>
/// The daily people check-in: at <see cref="LocalCheckInTime"/> in the owner's time zone it sends a birthday
/// notification for everyone whose birthday is today and one keep-in-touch nudge for the people the owner is due
/// to contact. Each birthday notifies once a year; a nudge repeats at most weekly until contact is logged.
/// </summary>
public sealed class PeopleCheckInService(
    IPeopleRepository people,
    INotificationRepository notifications,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : IPeopleCheckInService
{
    public static readonly TimeOnly LocalCheckInTime = new(9, 0);
    public const int NudgeRepeatDays = 7;
    public const string BirthdayType = "people.birthday";
    public const string CheckInType = "people.checkin";

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<PeopleCheckInResult> RunAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var watched = (await people.ListAsync(ownerId, cancellationToken)).Where(x => x.NeedsCheckIns).ToArray();
        if (watched.Length == 0) return new PeopleCheckInResult(false, now, 0, 0);

        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        var zone = LocalClock.TryFind(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        var today = PeopleCalendar.LocalDate(now, zone);
        var fireToday = LocalClock.Resolve(today.ToDateTime(LocalCheckInTime), zone);
        if (now < fireToday) return new PeopleCheckInResult(true, fireToday, 0, 0);

        var birthdays = BirthdaysToday(watched, today);
        foreach (var person in birthdays)
        {
            var age = PeopleCalendar.AgeOn(person, today);
            await notifications.CreateAsync(ownerId, BirthdayType, $"It's {person.Name}'s birthday today",
                age is { } turns
                    ? $"{person.Name} turns {turns} today. A message or a call will make their day."
                    : $"Wish {person.Name} a happy birthday. A message or a call will make their day.",
                person.Id, cancellationToken);
        }

        var birthdayIds = birthdays.Select(x => x.Id).ToHashSet();
        var nudges = DueForNudge(watched.Where(x => !birthdayIds.Contains(x.Id)), today, zone);
        if (nudges.Count > 0)
        {
            var (title, body) = DescribeNudge(nudges, today, zone);
            await notifications.CreateAsync(ownerId, CheckInType, title, body,
                nudges.Count == 1 ? nudges[0].Id : null, cancellationToken);
        }

        if (birthdays.Count > 0 || nudges.Count > 0)
            await people.MarkNotifiedAsync(ownerId, birthdayIds, today.Year, nudges.Select(x => x.Id).ToArray(),
                now, cancellationToken);

        var next = LocalClock.Resolve(today.AddDays(1).ToDateTime(LocalCheckInTime), zone);
        return new PeopleCheckInResult(true, next, birthdays.Count, nudges.Count);
    }

    public static IReadOnlyList<Person> BirthdaysToday(IEnumerable<Person> all, DateOnly today) =>
        all.Where(x => PeopleCalendar.NextBirthday(x, today) == today && x.BirthdayNotifiedYear != today.Year)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    /// <summary>
    /// People whose check-in is due and who were not nudged in the last week, or were nudged before the owner last
    /// logged contact. Longest overdue first.
    /// </summary>
    public static IReadOnlyList<Person> DueForNudge(IEnumerable<Person> all, DateOnly today, TimeZoneInfo zone) =>
        all.Where(x => PeopleCalendar.IsContactDue(x, today, zone))
            .Where(x => x.CheckInNudgedAt is not { } nudged ||
                        PeopleCalendar.LocalDate(nudged, zone).AddDays(NudgeRepeatDays) <= today ||
                        (x.LastContactedAt is { } contacted && contacted > nudged))
            .OrderBy(x => PeopleCalendar.ContactDueOn(x, zone))
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public static (string Title, string Body) DescribeNudge(IReadOnlyList<Person> due, DateOnly today,
        TimeZoneInfo zone)
    {
        if (due.Count == 1)
        {
            var person = due[0];
            var cadence = PeopleCalendar.DescribeCadence(person.ContactEveryDays!.Value);
            var since = PeopleCalendar.DaysSinceContact(person, today, zone);
            return ($"Time to check in with {person.Name}",
                since is { } days
                    ? $"You last talked {PeopleCalendar.DescribeDaysAgo(days)}. You wanted to stay in touch {cadence}."
                    : $"You wanted to stay in touch {cadence}. Mark it in Jarvis once you've talked.");
        }

        var title = due.Count == 2
            ? $"Time to check in with {due[0].Name} and {due[1].Name}"
            : $"Time to check in with {due[0].Name} and {due.Count - 1} others";
        var body = string.Join(", ", due.Take(5).Select(person =>
            PeopleCalendar.DaysSinceContact(person, today, zone) is { } days
                ? $"{person.Name} ({PeopleCalendar.DescribeDaysAgo(days)})"
                : person.Name));
        if (due.Count > 5) body += $" and {due.Count - 5} more";
        return (title, "Last talked: " + body + ".");
    }
}
