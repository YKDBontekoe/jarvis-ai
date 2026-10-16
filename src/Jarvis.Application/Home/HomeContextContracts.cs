using System.Globalization;
using Jarvis.Application.People.Radar;
using Jarvis.Application.Routines;
using Jarvis.Domain.People;

namespace Jarvis.Application.Home;

/// <summary>
/// What Home leads with right now, picked from the owner's own data: one line about the day, the "Right now" card,
/// the people the owner keeps in touch with, things Jarvis noticed, and tomorrow or today's recap.
/// </summary>
public sealed record HomeContextDto(
    string DayLine,
    HomeHeroDto? Hero,
    IReadOnlyList<HomePersonDto> People,
    IReadOnlyList<HomeInsightDto> Noticed,
    IReadOnlyList<HomeAgendaItemDto> Tomorrow,
    HomeDayRecapDto DayRecap);

/// <summary>The one card at the top of Home. <see cref="Facts"/> are what the "why" sheet lists.</summary>
public sealed record HomeHeroDto(
    string Id,
    string Kind,
    string Title,
    string? Detail,
    DateTimeOffset? StartsAt,
    string Reason,
    IReadOnlyList<string> Facts,
    Guid? ReminderId = null);

/// <summary>A person linked to a WhatsApp chat, with how the two of them usually keep in touch.</summary>
public sealed record HomePersonDto(
    Guid PersonId,
    string Name,
    string? Relationship,
    Guid ConnectionId,
    string ChatId,
    double? UsualReplyMinutes,
    double? WaitingHours,
    bool Quiet,
    double BaselinePerWeek,
    DateTimeOffset? LastMessageAt,
    string? Birthday);

/// <summary>Something Jarvis noticed, with the evidence for it.</summary>
public sealed record HomeInsightDto(
    string Id,
    string Kind,
    string Title,
    string Evidence,
    double? Confidence,
    string ActionLabel,
    Guid? SourceId,
    Guid? PersonId = null);

public sealed record HomeAgendaItemDto(string Title, DateTimeOffset? At, string Kind, Guid? Id = null);

public sealed record HomeDayRecapDto(int RemindersDone, int TasksDone, IReadOnlyList<string> Done);

/// <summary>One thing on the owner's day the rules look at.</summary>
public sealed record HomeAgendaFact(string Title, DateTimeOffset At, string Kind, Guid? Id = null);

/// <summary>Everything the pure Home rules read; the service fills it.</summary>
public sealed record HomeContextFacts(
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    IReadOnlyList<HomeAgendaFact> Agenda,
    HomeBriefingNoteDto? Brief,
    int? BatteryPercent,
    bool? Charging,
    DateTimeOffset? DeviceReportedAt);

/// <summary>Cards the owner put away: one card for a week, or a whole kind for good.</summary>
public sealed record HomeContextDismissals(
    IReadOnlyDictionary<string, DateTimeOffset>? Until = null,
    IReadOnlyList<string>? MutedKinds = null)
{
    public const string Section = "home.context";
    public static readonly TimeSpan HideFor = TimeSpan.FromDays(7);

    public bool Hides(string id, string kind, DateTimeOffset now) =>
        (MutedKinds?.Contains(kind) ?? false) ||
        (Until is not null && Until.TryGetValue(id, out var until) && until > now);

    public HomeContextDismissals With(string id, string kind, bool mute, DateTimeOffset now)
    {
        var until = (Until ?? new Dictionary<string, DateTimeOffset>())
            .Where(pair => pair.Value > now)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        until[id] = now + HideFor;
        var muted = (MutedKinds ?? []).ToList();
        if (mute && !muted.Contains(kind)) muted.Add(kind);
        return new HomeContextDismissals(until, muted);
    }
}

public sealed record HomeContextDismissRequest(string? Id, string? Kind, bool Mute = false);

public interface IHomeContextService
{
    Task<HomeContextDto> GetAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Puts a card away; false when the id or kind is not one Home shows.</summary>
    Task<bool> DismissAsync(Guid ownerId, HomeContextDismissRequest request, CancellationToken cancellationToken);
}

public static class HomeHeroKinds
{
    public const string Brief = "brief";
    public const string Soon = "soon";
    public const string Battery = "battery";
    public const string WindDown = "wind_down";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Brief, Soon, Battery, WindDown };
}

public static class HomeInsightKinds
{
    public const string Routine = "routine";
    public const string Drift = "drift";
    public const string Birthday = "birthday";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Routine, Drift, Birthday };
}

/// <summary>Picks the one thing worth the top of Home. Pure, so every rule is tested with a fixed clock.</summary>
public static class HeroSelector
{
    public static readonly TimeSpan SoonWindow = TimeSpan.FromMinutes(60);
    public const int LowBattery = 20;
    public const int BriefUntilHour = 11;
    public const int WindDownHour = 21;

    public static HomeHeroDto? Pick(HomeContextFacts facts, HomeContextDismissals? dismissed = null)
    {
        var local = TimeZoneInfo.ConvertTime(facts.Now, facts.Zone);
        foreach (var hero in Candidates(facts, local))
        {
            if (dismissed?.Hides(hero.Id, hero.Kind, facts.Now) == true) continue;
            return hero;
        }

        return null;
    }

    private static IEnumerable<HomeHeroDto> Candidates(HomeContextFacts facts, DateTimeOffset local)
    {
        var next = facts.Agenda
            .Where(item => item.At >= facts.Now - TimeSpan.FromMinutes(5))
            .OrderBy(item => item.At)
            .FirstOrDefault();
        if (next is not null && next.At - facts.Now <= SoonWindow)
        {
            var minutes = Math.Max(0, (int)Math.Ceiling((next.At - facts.Now).TotalMinutes));
            yield return new HomeHeroDto(
                $"soon:{next.Id?.ToString() ?? next.Title}:{next.At:yyyyMMddHHmm}",
                HomeHeroKinds.Soon,
                next.Title,
                next.Kind == "event" ? "On your calendar" : "Reminder",
                next.At,
                minutes == 0 ? "It is starting now" : $"It starts in {minutes} min",
                [
                    $"{(next.Kind == "event" ? "Calendar event" : "Reminder")} at {Clock(next.At, facts.Zone)}",
                    "Shown up to an hour before it starts"
                ],
                next.Kind == "reminder" ? next.Id : null);
        }

        if (facts is { BatteryPercent: { } battery, Charging: not true } && battery < LowBattery &&
            facts.DeviceReportedAt is { } reported && facts.Now - reported < TimeSpan.FromHours(2))
        {
            var later = facts.Agenda.Count(item => item.At > facts.Now && SameDay(item.At, facts.Now, facts.Zone));
            yield return new HomeHeroDto(
                $"battery:{local:yyyyMMdd}",
                HomeHeroKinds.Battery,
                "Charge your phone",
                later > 0 ? $"{battery}% left and {later} more {(later == 1 ? "plan" : "plans")} today" : $"{battery}% left",
                null,
                "Your phone reported a low battery",
                [$"Battery at {battery}%, not charging", $"Reported at {Clock(reported, facts.Zone)}"]);
        }

        if (facts.Brief is { Read: false } brief && local.Hour < BriefUntilHour &&
            SameDay(brief.CreatedAt, facts.Now, facts.Zone))
        {
            yield return new HomeHeroDto(
                $"brief:{brief.Id}",
                HomeHeroKinds.Brief,
                "Your morning brief is ready",
                FirstLine(brief.Body),
                null,
                "It arrived this morning and you have not read it yet",
                [$"Delivered at {Clock(brief.CreatedAt, facts.Zone)}"]);
        }

        if (local.Hour >= WindDownHour)
        {
            var tomorrow = Tomorrow(facts).ToList();
            yield return new HomeHeroDto(
                $"wind_down:{local:yyyyMMdd}",
                HomeHeroKinds.WindDown,
                tomorrow.Count == 0 ? "Nothing planned tomorrow" : $"Tomorrow: {tomorrow[0].Title}",
                tomorrow.Count switch
                {
                    0 => "A free day ahead",
                    1 => tomorrow[0].At is { } at ? $"At {Clock(at, facts.Zone)}" : null,
                    _ => $"Then {string.Join(", ", tomorrow.Skip(1).Select(item => item.Title))}"
                },
                tomorrow.FirstOrDefault()?.At,
                "It is late, so Home looks ahead to tomorrow",
                ["Shown from 21:00"]);
        }
    }

    /// <summary>The first items of the owner's next local day, soonest first.</summary>
    public static IEnumerable<HomeAgendaItemDto> Tomorrow(HomeContextFacts facts, int max = 3)
    {
        var local = TimeZoneInfo.ConvertTime(facts.Now, facts.Zone);
        var tomorrow = local.Date.AddDays(1);
        return facts.Agenda
            .Where(item => TimeZoneInfo.ConvertTime(item.At, facts.Zone).Date == tomorrow)
            .OrderBy(item => item.At)
            .Take(max)
            .Select(item => new HomeAgendaItemDto(item.Title, item.At, item.Kind, item.Id));
    }

    internal static bool SameDay(DateTimeOffset a, DateTimeOffset b, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(a, zone).Date == TimeZoneInfo.ConvertTime(b, zone).Date;

    internal static string Clock(DateTimeOffset value, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(value, zone).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string? FirstLine(string body)
    {
        var line = body.Split('\n').Select(part => part.Trim().TrimStart('#', '-', '*', ' ').Replace("**", ""))
            .FirstOrDefault(part => part.Length > 0);
        if (line is null) return null;
        return line.Length <= 120 ? line : line[..119].TrimEnd() + "…";
    }
}

/// <summary>One plain sentence about the owner's day, built from facts only.</summary>
public static class DayLine
{
    public static string Compose(HomeContextFacts facts)
    {
        var local = TimeZoneInfo.ConvertTime(facts.Now, facts.Zone);
        var today = facts.Agenda
            .Where(item => item.At >= facts.Now && HeroSelector.SameDay(item.At, facts.Now, facts.Zone))
            .OrderBy(item => item.At)
            .ToList();
        if (local.Hour >= 18)
        {
            var tomorrow = HeroSelector.Tomorrow(facts, 1).FirstOrDefault();
            if (today.Count > 0)
                return $"{Count(today.Count)} left tonight, starting with {today[0].Title} at {HeroSelector.Clock(today[0].At, facts.Zone)}.";
            return tomorrow is { At: { } at }
                ? $"A quiet evening. Tomorrow starts with {tomorrow.Title} at {HeroSelector.Clock(at, facts.Zone)}."
                : "A quiet evening, and nothing planned for tomorrow yet.";
        }

        if (today.Count == 0) return "Nothing on your plate today. A good day to get ahead.";
        if (today.Count == 1)
            return $"Just one thing today: {today[0].Title} at {HeroSelector.Clock(today[0].At, facts.Zone)}.";
        // Find the longest free stretch between plans, so the line can say when there is room.
        var gaps = today.Zip(today.Skip(1), (a, b) => (From: a, To: b, Span: b.At - a.At))
            .Where(gap => gap.Span >= TimeSpan.FromHours(2))
            .OrderByDescending(gap => gap.Span)
            .ToList();
        var first = HeroSelector.Clock(today[0].At, facts.Zone);
        if (gaps.Count > 0)
            return $"{Count(today.Count)} today, from {first}. You are free between " +
                   $"{HeroSelector.Clock(gaps[0].From.At, facts.Zone)} and {HeroSelector.Clock(gaps[0].To.At, facts.Zone)}.";
        return $"{Count(today.Count)} today, starting at {first} with {today[0].Title}.";
    }

    private static string Count(int count) => count switch
    {
        1 => "One thing",
        2 => "Two things",
        3 => "Three things",
        _ => $"{count} things"
    };
}

/// <summary>Turns routine suggestions, radar drift and birthdays into the "Jarvis noticed" cards.</summary>
public static class InsightRules
{
    public const int MaxInsights = 4;
    public const double MinRoutineConfidence = .5;

    public static IReadOnlyList<HomeInsightDto> Build(
        IReadOnlyList<RoutineSuggestionView> routines,
        IReadOnlyList<HomePersonDto> people,
        IReadOnlyList<Person> everyone,
        DateTimeOffset now,
        TimeZoneInfo zone,
        HomeContextDismissals? dismissed = null)
    {
        var result = new List<HomeInsightDto>();
        foreach (var routine in routines.Where(item => item.Confidence >= MinRoutineConfidence).Take(2))
        {
            result.Add(new HomeInsightDto($"routine:{routine.Id}", HomeInsightKinds.Routine, routine.Title,
                routine.Evidence, routine.Confidence, "Set it up", routine.Id));
        }

        // A person in several chats is one contact gone quiet. The id carries no date: putting the card away hides
        // it for the whole week, instead of it coming back as "one more day" the next morning.
        foreach (var person in people.Where(item => item.Quiet).GroupBy(item => item.PersonId)
                     .Select(group => group.First()).OrderByDescending(item => item.BaselinePerWeek).Take(2))
        {
            var since = person.LastMessageAt is { } last ? Since(now - last) : "a while";
            var usual = person.BaselinePerWeek switch
            {
                >= 5 => "you usually talk daily",
                >= 1 => "you usually talk weekly",
                _ => "you used to talk more"
            };
            result.Add(new HomeInsightDto($"drift:{person.PersonId}", HomeInsightKinds.Drift,
                $"{since} since you talked to {person.Name}", Capitalize(usual), null, "Say hi", null, person.PersonId));
        }

        var local = TimeZoneInfo.ConvertTime(now, zone).Date;
        foreach (var person in everyone.Where(item => item.HasBirthday))
        {
            var days = DaysUntilBirthday(person.BirthdayMonth!.Value, person.BirthdayDay!.Value, local);
            if (days is < 0 or > 2) continue;
            var age = person.BirthYear is { } year ? local.AddDays(days).Year - year : (int?)null;
            var when = days switch { 0 => "today", 1 => "tomorrow", _ => "in two days" };
            result.Add(new HomeInsightDto($"birthday:{person.Id}:{local.Year}", HomeInsightKinds.Birthday,
                age is > 0 ? $"{person.Name} turns {age} {when}" : $"{person.Name}'s birthday is {when}",
                "From your People list", null, "Plan a message", null, person.Id));
        }

        return result
            .Where(item => dismissed?.Hides(item.Id, item.Kind, now) != true)
            .Take(MaxInsights)
            .ToArray();
    }

    /// <summary>Days from <paramref name="today"/> to the next birthday on that month and day.</summary>
    public static int DaysUntilBirthday(int month, int day, DateTime today)
    {
        DateTime Next(int year)
        {
            var safeDay = Math.Min(day, DateTime.DaysInMonth(year, month));
            return new DateTime(year, month, safeDay);
        }

        var next = Next(today.Year);
        if (next < today.Date) next = Next(today.Year + 1);
        return (next - today.Date).Days;
    }

    private static string Since(TimeSpan gap)
    {
        if (gap.TotalDays >= 14) return $"{(int)(gap.TotalDays / 7)} weeks";
        if (gap.TotalDays >= 2) return $"{(int)gap.TotalDays} days";
        return "A day";
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

/// <summary>Builds <see cref="HomePersonDto"/> rows from the radar and the links people have to chats.</summary>
public static class HomePeople
{
    public static IReadOnlyList<HomePersonDto> Build(IReadOnlyList<Person> people,
        IReadOnlyList<PersonChannelLink> links, IReadOnlyList<RadarReport> reports)
    {
        var byId = people.ToDictionary(person => person.Id);
        var radar = reports.ToDictionary(report => report.PersonId);
        var result = new List<HomePersonDto>();
        foreach (var link in links)
        {
            if (!byId.TryGetValue(link.PersonId, out var person)) continue;
            radar.TryGetValue(person.Id, out var report);
            result.Add(new HomePersonDto(
                person.Id,
                person.Name,
                string.IsNullOrWhiteSpace(person.Relationship) ? null : person.Relationship.Trim(),
                link.ConnectionId,
                link.ChatId,
                report?.BaselineReplyMinutes ?? report?.MyReplyMinutes,
                report is { UnansweredInbound: > 0 } ? report.UnansweredHours : null,
                report?.Signals.Any(signal => signal.Kind == RadarSignalKinds.Quiet) ?? false,
                report?.BaselinePerWeek ?? 0,
                report?.LastMessageAt,
                person.HasBirthday ? $"{person.BirthdayDay:00}-{person.BirthdayMonth:00}" : null));
        }

        return result;
    }
}
