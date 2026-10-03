using System.Globalization;
using Jarvis.Application.Conversations;
using Jarvis.Application.Expenses;
using Jarvis.Application.Habits;
using Jarvis.Application.Journal;
using Jarvis.Application.Memory;
using Jarvis.Application.People;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Timeline;

internal static class TimelineFormat
{
    public static string Money(decimal amount, string currency) =>
        currency switch
        {
            "EUR" => "€" + amount.ToString("0.00", CultureInfo.InvariantCulture),
            "USD" => "$" + amount.ToString("0.00", CultureInfo.InvariantCulture),
            "GBP" => "£" + amount.ToString("0.00", CultureInfo.InvariantCulture),
            _ => amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + currency
        };

    public static bool InWindow(DateOnly date, TimelineWindow window) => date >= window.From && date <= window.To;
}

public sealed class JournalTimelineSource(IJournalRepository journal) : ITimelineSource
{
    public IReadOnlySet<string> Kinds { get; } = new HashSet<string> { TimelineKinds.Journal };

    public async Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window,
        CancellationToken cancellationToken)
    {
        var entries = await journal.ListAsync(window.OwnerId, window.From, window.To, window.Limit, cancellationToken);
        return entries.Select(entry =>
        {
            var ratings = new List<string>();
            if (entry.Rating is { } rating) ratings.Add($"day {rating}/10");
            if (entry.Mood is { } mood) ratings.Add($"mood {mood}/5");
            if (entry.Energy is { } energy) ratings.Add($"energy {energy}/5");
            if (entry.Stress is { } stress) ratings.Add($"stress {stress}/5");
            var body = TimelineRules.Shorten(
                string.IsNullOrWhiteSpace(entry.Highlights) ? entry.Content : entry.Highlights,
                TimelineRules.MaxDetailLength);
            var detail = string.Join(" · ", new[] { string.Join(", ", ratings), body }.Where(x => x.Length > 0));
            return new TimelineEvent($"{TimelineKinds.Journal}:{entry.Id}", TimelineKinds.Journal, "Journal entry",
                detail.Length == 0 ? null : detail, TimelineRules.Stamp(entry.EntryDate, entry.CreatedAt, window.Zone),
                entry.EntryDate, entry.Id);
        }).ToArray();
    }
}

public sealed class ExpenseTimelineSource(IExpenseRepository expenses) : ITimelineSource
{
    public IReadOnlySet<string> Kinds { get; } = new HashSet<string> { TimelineKinds.Expense };

    public async Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window,
        CancellationToken cancellationToken)
    {
        var list = await expenses.ListAsync(window.OwnerId, window.From, window.To, cancellationToken);
        return list.Take(window.Limit).Select(expense =>
        {
            var title = "Spent " + TimelineFormat.Money(expense.Amount, expense.Currency) +
                        (expense.Merchant is null ? "" : " at " + TimelineRules.Shorten(expense.Merchant, 60));
            return new TimelineEvent($"{TimelineKinds.Expense}:{expense.Id}", TimelineKinds.Expense, title,
                TimelineRules.Shorten(expense.Note is null ? expense.Category : $"{expense.Category} · {expense.Note}",
                    TimelineRules.MaxDetailLength),
                TimelineRules.Stamp(expense.SpentOn, expense.CreatedAt, window.Zone), expense.SpentOn, expense.Id,
                expense.Amount, expense.Currency);
        }).ToArray();
    }
}

public sealed class HabitTimelineSource(IHabitRepository habits) : ITimelineSource
{
    public IReadOnlySet<string> Kinds { get; } = new HashSet<string> { TimelineKinds.Habit };

    public async Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window,
        CancellationToken cancellationToken)
    {
        var all = await habits.ListAsync(window.OwnerId, true, cancellationToken);
        if (all.Count == 0) return [];
        var names = all.ToDictionary(x => x.Id, x => x.Name);
        var checkIns = await habits.ListCheckInsAsync(window.OwnerId, names.Keys.ToArray(), cancellationToken);
        return checkIns.Where(x => TimelineFormat.InWindow(x.Date, window) && names.ContainsKey(x.HabitId))
            .OrderByDescending(x => x.Date).Take(window.Limit)
            .Select(x => new TimelineEvent($"{TimelineKinds.Habit}:{x.Id}", TimelineKinds.Habit,
                "Did " + TimelineRules.Shorten(names[x.HabitId], 60), null,
                TimelineRules.Stamp(x.Date, x.CreatedAt, window.Zone), x.Date, x.HabitId))
            .ToArray();
    }
}

/// <summary>Birthdays that fall in the window and the last time the owner was in touch with someone.</summary>
public sealed class PeopleTimelineSource(IPeopleRepository people) : ITimelineSource
{
    public IReadOnlySet<string> Kinds { get; } =
        new HashSet<string> { TimelineKinds.Contact, TimelineKinds.Birthday };

    public async Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window,
        CancellationToken cancellationToken)
    {
        var events = new List<TimelineEvent>();
        foreach (var person in await people.ListAsync(window.OwnerId, cancellationToken))
        {
            if (person.LastContactedAt is { } contacted &&
                TimelineRules.LocalDate(contacted, window.Zone) is var contactDay &&
                TimelineFormat.InWindow(contactDay, window))
            {
                events.Add(new TimelineEvent($"{TimelineKinds.Contact}:{person.Id}", TimelineKinds.Contact,
                    "In touch with " + TimelineRules.Shorten(person.Name, 60), person.Relationship,
                    contacted, contactDay, person.Id));
            }

            if (person is { BirthdayMonth: { } month, BirthdayDay: { } day })
            {
                for (var year = window.From.Year; year <= window.To.Year; year++)
                {
                    // 29 February is celebrated on the 28th in other years.
                    var dayOfMonth = Math.Min(day, DateTime.DaysInMonth(year, month));
                    var date = new DateOnly(year, month, dayOfMonth);
                    if (!TimelineFormat.InWindow(date, window)) continue;
                    var age = person.BirthYear is { } born ? $"turns {year - born}" : null;
                    events.Add(new TimelineEvent($"{TimelineKinds.Birthday}:{person.Id}:{year}",
                        TimelineKinds.Birthday, TimelineRules.Shorten(person.Name, 60) + "'s birthday", age,
                        TimelineRules.Noon(date, window.Zone), date, person.Id));
                }
            }
        }
        return events;
    }
}

public sealed class TaskTimelineSource(IJarvisTaskRepository tasks) : ITimelineSource
{
    public IReadOnlySet<string> Kinds { get; } = new HashSet<string> { TimelineKinds.Task };

    public async Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window,
        CancellationToken cancellationToken)
    {
        var all = await tasks.ListAsync(window.OwnerId, cancellationToken);
        return all.Where(x => x.CompletedAt is not null && x.Status is "completed" or "failed")
            .Select(x => (Task: x, Date: TimelineRules.LocalDate(x.CompletedAt!.Value, window.Zone)))
            .Where(x => TimelineFormat.InWindow(x.Date, window))
            .OrderByDescending(x => x.Task.CompletedAt).Take(window.Limit)
            .Select(x => new TimelineEvent($"{TimelineKinds.Task}:{x.Task.Id}", TimelineKinds.Task,
                (x.Task.Status == "failed" ? "Task failed: " : "Finished task: ") +
                TimelineRules.Shorten(x.Task.Title, 80),
                TimelineRules.Shorten(x.Task.Summary, TimelineRules.MaxDetailLength) is { Length: > 0 } summary
                    ? summary
                    : null,
                x.Task.CompletedAt!.Value, x.Date, x.Task.Id))
            .ToArray();
    }
}

public sealed class ReminderTimelineSource(IReminderRepository reminders) : ITimelineSource
{
    public IReadOnlySet<string> Kinds { get; } = new HashSet<string> { TimelineKinds.Reminder };

    public async Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window,
        CancellationToken cancellationToken)
    {
        var all = await reminders.ListRemindersAsync(window.OwnerId, cancellationToken);
        return all.Select(x => (Reminder: x, At: x.LastDeliveredAt ?? x.CompletedAt))
            .Where(x => x.At is not null)
            .Select(x => (x.Reminder, At: x.At!.Value, Date: TimelineRules.LocalDate(x.At!.Value, window.Zone)))
            .Where(x => TimelineFormat.InWindow(x.Date, window))
            .OrderByDescending(x => x.At).Take(window.Limit)
            .Select(x => new TimelineEvent($"{TimelineKinds.Reminder}:{x.Reminder.Id}", TimelineKinds.Reminder,
                "Reminder: " + TimelineRules.Shorten(x.Reminder.Title, 100), null, x.At, x.Date, x.Reminder.Id))
            .ToArray();
    }
}

/// <summary>What Jarvis learned. Journal memories are skipped because the journal source already shows them.</summary>
public sealed class MemoryTimelineSource(IMemoryRepository memories) : ITimelineSource
{
    public IReadOnlySet<string> Kinds { get; } = new HashSet<string> { TimelineKinds.Memory };

    public async Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window,
        CancellationToken cancellationToken)
    {
        var all = await memories.ListAsync(window.OwnerId, null, cancellationToken);
        return all.Where(x => x.Kind != JournalMemoryFormatter.MemoryKind)
            .Select(x => (Memory: x, Date: TimelineRules.LocalDate(x.CreatedAt, window.Zone)))
            .Where(x => TimelineFormat.InWindow(x.Date, window))
            .OrderByDescending(x => x.Memory.CreatedAt).Take(window.Limit)
            .Select(x => new TimelineEvent($"{TimelineKinds.Memory}:{x.Memory.Id}", TimelineKinds.Memory,
                "Learned: " + TimelineRules.Shorten(x.Memory.Content, TimelineRules.MaxTitleLength), x.Memory.Kind,
                x.Memory.CreatedAt, x.Date, x.Memory.Id))
            .ToArray();
    }
}

public sealed class ConversationTimelineSource(IConversationStore conversations) : ITimelineSource
{
    public IReadOnlySet<string> Kinds { get; } = new HashSet<string> { TimelineKinds.Conversation };

    public async Task<IReadOnlyList<TimelineEvent>> ListAsync(TimelineWindow window,
        CancellationToken cancellationToken)
    {
        var all = await conversations.ListAsync(window.OwnerId, cancellationToken);
        return all.Select(x => (Conversation: x, Date: TimelineRules.LocalDate(x.CreatedAt, window.Zone)))
            .Where(x => TimelineFormat.InWindow(x.Date, window))
            .OrderByDescending(x => x.Conversation.CreatedAt).Take(window.Limit)
            .Select(x => new TimelineEvent($"{TimelineKinds.Conversation}:{x.Conversation.Id}",
                TimelineKinds.Conversation, "Chat: " + TimelineRules.Shorten(x.Conversation.Title, 100), null,
                x.Conversation.CreatedAt, x.Date, x.Conversation.Id))
            .ToArray();
    }
}
