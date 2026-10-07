using System.Globalization;
using Jarvis.Application.Finance;
using Jarvis.Application.Habits;
using Jarvis.Application.Inbox;
using Jarvis.Application.Integrations;
using Jarvis.Application.People;

namespace Jarvis.Application.Workflows;

/// <summary>One titled block of a morning briefing, such as today's calendar or the threads waiting for a reply.</summary>
public sealed record DailyBriefingSection(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// Contributes one section to the morning briefing. A provider returns null when it has nothing to say, and a provider
/// that throws is skipped, so one broken source never costs the owner the whole briefing.
/// </summary>
public interface IBriefingSectionProvider
{
    Task<DailyBriefingSection?> BuildAsync(Guid ownerId, DailyBriefingActivityInput day,
        CancellationToken cancellationToken);
}

public static class BriefingSections
{
    public const int MaxLines = 5;
    public const int MaxLineLength = 120;

    public static string Line(string? text)
    {
        var clean = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= MaxLineLength ? clean : clean[..(MaxLineLength - 1)].TrimEnd() + "…";
    }

    /// <summary>The first <see cref="MaxLines"/> lines, with a final "and N more" line when some were left out.</summary>
    public static IReadOnlyList<string> Cap(IReadOnlyList<string> lines)
    {
        if (lines.Count <= MaxLines) return lines;
        return [.. lines.Take(MaxLines), $"…and {lines.Count - MaxLines} more"];
    }

    public static DailyBriefingSection? Of(string title, IReadOnlyList<string> lines) =>
        lines.Count == 0 ? null : new DailyBriefingSection(title, Cap(lines));

    public static TimeZoneInfo Zone(string timeZoneId) =>
        LocalClock.TryFind(timeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;
}

/// <summary>Calendar events that start during the owner's local day.</summary>
public sealed class CalendarBriefingSection(ICalendarFeed calendar) : IBriefingSectionProvider
{
    public async Task<DailyBriefingSection?> BuildAsync(Guid ownerId, DailyBriefingActivityInput day,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CalendarEventRecord> events;
        try
        {
            events = await calendar.ListUpcomingAsync(ownerId, day.LocalDayStart, day.NextLocalDayStart,
                cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or HttpRequestException
                                              or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // An unreachable calendar feed is common and only means this section is left out.
            return null;
        }

        var zone = BriefingSections.Zone(day.TimeZoneId);
        return BriefingSections.Of("Calendar", events
            .Where(item => item.StartAt >= day.LocalDayStart && item.StartAt < day.NextLocalDayStart)
            .OrderBy(item => item.StartAt)
            .Select(item => BriefingSections.Line(
                $"{TimeZoneInfo.ConvertTime(item.StartAt, zone).ToString("HH:mm", CultureInfo.InvariantCulture)} {item.Title}"))
            .ToArray());
    }
}

/// <summary>Threads the inbox triage marked as needing a reply, most urgent first.</summary>
public sealed class InboxBriefingSection(IInboxService inbox) : IBriefingSectionProvider
{
    public async Task<DailyBriefingSection?> BuildAsync(Guid ownerId, DailyBriefingActivityInput day,
        CancellationToken cancellationToken)
    {
        var listing = await inbox.ListAsync(ownerId, new HashSet<string> { InboxStates.NeedsReply }, cancellationToken);
        var threads = listing.Threads
            .Where(thread => thread.State == InboxStates.NeedsReply)
            .OrderByDescending(thread => thread.Priority)
            .ThenBy(thread => thread.LastMessageAt)
            .ToArray();
        if (threads.Length == 0) return null;
        return BriefingSections.Of($"Waiting for your reply ({threads.Length})", threads
            .Select(thread => BriefingSections.Line(thread.Counterparty is { Length: > 0 } who
                ? $"{who} — {thread.Title}"
                : thread.Title))
            .ToArray());
    }
}

/// <summary>Accepted commitments that are due today or already overdue.</summary>
public sealed class CommitmentsBriefingSection(ICommitmentService commitments) : IBriefingSectionProvider
{
    public async Task<DailyBriefingSection?> BuildAsync(Guid ownerId, DailyBriefingActivityInput day,
        CancellationToken cancellationToken)
    {
        var open = await commitments.ListAsync(ownerId, null, CommitmentStatuses.Open, includeSuggested: false,
            cancellationToken);
        return BriefingSections.Of("Commitments", open
            .Where(item => !item.Suggested && item.DueOn is { } due && due <= day.LocalDate)
            .OrderBy(item => item.DueOn)
            .Select(item => BriefingSections.Line(Describe(item, day.LocalDate)))
            .ToArray());
    }

    public static string Describe(Jarvis.Domain.Inbox.Commitment item, DateOnly today)
    {
        var what = item.Direction == CommitmentDirections.OwedToMe
            ? $"{item.Counterparty} owes you: {item.Description}"
            : $"You owe {item.Counterparty}: {item.Description}";
        var due = item.DueOn!.Value;
        return due == today
            ? what + " (today)"
            : what + $" (overdue since {due.ToString("d MMM", CultureInfo.InvariantCulture)})";
    }
}

/// <summary>Habits not yet done today, as one line.</summary>
public sealed class HabitsBriefingSection(IHabitService habits) : IBriefingSectionProvider
{
    public async Task<DailyBriefingSection?> BuildAsync(Guid ownerId, DailyBriefingActivityInput day,
        CancellationToken cancellationToken)
    {
        var open = (await habits.ListAsync(ownerId, includeArchived: false, cancellationToken))
            .Where(summary => summary.IsOpenToday)
            .Select(summary => summary.Habit.Name)
            .ToArray();
        return open.Length == 0
            ? null
            : new DailyBriefingSection("Habits", [BriefingSections.Line(string.Join(", ", open))]);
    }
}

/// <summary>Birthdays in the coming week.</summary>
public sealed class BirthdaysBriefingSection(IPeopleService people) : IBriefingSectionProvider
{
    public const int HorizonDays = 7;

    public async Task<DailyBriefingSection?> BuildAsync(Guid ownerId, DailyBriefingActivityInput day,
        CancellationToken cancellationToken)
    {
        var lines = new List<(int Days, string Line)>();
        foreach (var person in await people.ListAsync(ownerId, cancellationToken))
        {
            if (PeopleCalendar.DaysUntilBirthday(person, day.LocalDate) is not { } days || days > HorizonDays) continue;
            var turns = PeopleCalendar.NextBirthday(person, day.LocalDate) is { } next
                ? PeopleCalendar.AgeOn(person, next)
                : null;
            var when = days == 0 ? "today" : days == 1 ? "tomorrow" : $"in {days} days";
            lines.Add((days, BriefingSections.Line(
                $"{person.Name} — {when}{(turns is { } age ? $" (turns {age})" : string.Empty)}")));
        }

        return BriefingSections.Of("Birthdays", lines.OrderBy(item => item.Days).Select(item => item.Line).ToArray());
    }
}

/// <summary>Budgets that are close to, or past, their limit this month.</summary>
public sealed class BudgetsBriefingSection(IFinanceService finance) : IBriefingSectionProvider
{
    public async Task<DailyBriefingSection?> BuildAsync(Guid ownerId, DailyBriefingActivityInput day,
        CancellationToken cancellationToken)
    {
        var statuses = await finance.BudgetStatusAsync(ownerId, day.LocalDate.Year, day.LocalDate.Month, day.LocalDate,
            cancellationToken);
        return BriefingSections.Of("Budgets", statuses
            .Where(status => status.State != BudgetStates.Ok)
            .OrderByDescending(status => status.Percent)
            .Select(status => BriefingSections.Line($"{status.Category} is at {status.Percent}% of its budget"))
            .ToArray());
    }
}
