using Jarvis.Application.Channels;
using Jarvis.Application.Expenses;
using Jarvis.Application.Habits;
using Jarvis.Application.Lists;
using Jarvis.Application.Planner;
using Jarvis.Application.Preferences;
using Jarvis.Application.Workflows;

namespace Jarvis.Api.Channels;

/// <summary>
/// The short commands an owner can type in WhatsApp or Signal that need no model: quick check-ins and spending notes,
/// reminders and lists. The router asks it first; a message that is not one of these returns null and goes on to the
/// assistant. Every command is scoped to the owner of the connection it arrived on.
/// </summary>
public sealed class ChannelCommandHandler(
    IListService lists,
    IReminderService reminders,
    IOwnerPreferenceService preferences,
    IHabitService habits,
    IExpenseService expenses,
    IDayPlannerService planner,
    TimeProvider? timeProvider = null)
{
    private const string NothingToCheckIn = "Nothing to check in.";

    /// <summary>The reply to send, or null when <paramref name="text"/> is not a command.</summary>
    public async Task<string?> TryHandleAsync(Guid ownerId, string text, CancellationToken cancellationToken)
    {
        if (QuickCommands.Parse(text) is { } quick) return await RunQuickCommandAsync(ownerId, quick, cancellationToken);
        if (ReminderCommands.Parse(text) is { } reminder) return await RunReminderCommandAsync(ownerId, reminder, cancellationToken);
        if (ListCommands.Parse(text) is { } list) return await RunListCommandAsync(ownerId, list, cancellationToken);
        return null;
    }

    private async Task<string> RunQuickCommandAsync(Guid ownerId, QuickCommand command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case QuickCommand.Usage usage:
                return usage.Message;
            case QuickCommand.CheckIn checkIn:
                var checkedIn = await habits.SetDoneByNameAsync(ownerId, checkIn.Habits, null, true, cancellationToken);
                if (!checkedIn.Succeeded) return "I could not check that in: " + checkedIn.Message;
                var result = checkedIn.Value!;
                var lines = new List<string>();
                lines.AddRange(result.Changed.Select(h => $"✅ {h.Habit.Name}: streak {h.Stats.CurrentStreak} {h.Stats.StreakUnit}"));
                lines.AddRange(result.Unchanged.Select(h => $"{h.Habit.Name} was already checked in today."));
                lines.AddRange(result.NotFound.Select(name => $"No habit called \"{name}\"."));
                lines.AddRange(result.Ambiguous.Select(a => $"\"{a.Text}\" matches more than one habit: {string.Join(", ", a.Candidates)}."));
                return lines.Count == 0 ? NothingToCheckIn : string.Join("\n", lines);
            case QuickCommand.Spent spent:
                var zoneView = await preferences.GetAsync(ownerId, cancellationToken);
                var zone = LocalClock.TryFind(zoneView.HomeTimeZone, out var found) ? found : TimeZoneInfo.Utc;
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime((timeProvider ?? TimeProvider.System).GetUtcNow(), zone).DateTime);
                var created = await expenses.CreateAsync(ownerId,
                    new ExpenseDraft(spent.Amount, spent.Currency, spent.Description, null, null, null, null, ExpenseSources.Chat),
                    today, true, cancellationToken);
                if (!created.Succeeded) return "I could not log that: " + created.Message;
                var expense = created.Value!.Expense;
                return created.Value.IsDuplicate
                    ? $"That looks like the same expense as one already logged today ({expense.Amount:0.00} {expense.Currency}); I did not add it twice."
                    : $"💶 Logged {expense.Amount:0.00} {expense.Currency}" +
                      (string.IsNullOrWhiteSpace(expense.Merchant) ? "" : $" at {expense.Merchant}") + $" under {expense.Category}.";
            case QuickCommand.Today:
                var day = await planner.GetTodayAsync(ownerId, null, cancellationToken);
                var dayZone = LocalClock.TryFind(day.TimeZoneId, out var planZone) ? planZone : TimeZoneInfo.Utc;
                if (day.Entries.Count == 0) return $"Nothing is planned today. {day.FreeMinutes} min free.";
                return string.Join("\n", day.Entries.Take(15).Select(entry =>
                    $"{TimeZoneInfo.ConvertTime(entry.StartAt, dayZone):HH:mm} {entry.Title}")) + $"\n{day.FreeMinutes} min free.";
            default:
                return QuickCommands.CheckInUsage;
        }
    }

    private async Task<string> RunReminderCommandAsync(Guid ownerId, ReminderCommand command,
        CancellationToken cancellationToken)
    {
        var view = await preferences.GetAsync(ownerId, cancellationToken);
        var zone = LocalClock.TryFind(view.HomeTimeZone, out var found) ? found : TimeZoneInfo.Utc;
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        switch (command)
        {
            case ReminderCommand.Usage usage:
                return usage.Message;
            case ReminderCommand.List:
                var pending = (await reminders.ListAsync(ownerId, cancellationToken))
                    .Where(item => item.Status == "pending" && item.Place is null).OrderBy(item => item.DueAt).Take(8).ToList();
                return pending.Count == 0
                    ? "Nothing is coming up. Try /remind in 30m call mum"
                    : string.Join("\n", pending.Select(item =>
                        $"• {TimeZoneInfo.ConvertTime(item.DueAt, zone):ddd d MMM HH:mm} {item.Title}"));
            default:
                try
                {
                    var due = ReminderCommands.Resolve(command, now, zone);
                    var created = await reminders.CreateAsync(ownerId,
                        new CreateReminderRequest(ReminderCommands.Title(command), due, null, 0, zone.Id), cancellationToken);
                    return $"⏰ Reminder set for {TimeZoneInfo.ConvertTime(created.DueAt, zone):ddd d MMM HH:mm}: {created.Title}";
                }
                catch (Exception exception) when (exception is ArgumentException)
                {
                    return "I could not set that reminder: " + exception.Message;
                }
        }
    }

    private async Task<string> RunListCommandAsync(Guid ownerId, ListCommand command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case ListCommand.Usage usage:
                return usage.Message;
            case ListCommand.Show show:
                var all = await lists.ListAsync(ownerId, cancellationToken);
                if (show.List is null)
                    return all.Count == 0
                        ? "No lists yet. Try /add shopping: milk, eggs"
                        : string.Join("\n", all.Take(20).Select(l => $"• {l.Name} ({l.Items.Count(i => !i.Done)} open)"));
                var matches = all.Where(l => l.Name.Contains(show.List, StringComparison.OrdinalIgnoreCase)).ToList();
                var exact = matches.FirstOrDefault(l => l.Name.Equals(show.List, StringComparison.OrdinalIgnoreCase));
                if (exact is not null) return ListCommands.Render(exact);
                return matches.Count switch
                {
                    1 => ListCommands.Render(matches[0]),
                    0 => "There is no list with that name. /lists shows them.",
                    _ => "More than one list matches: " + string.Join(", ", matches.Select(l => l.Name)),
                };
            case ListCommand.Add add:
                var added = await lists.AddItemsAsync(ownerId, add.List, add.Items, cancellationToken);
                return added.Succeeded ? ListCommands.Render(added.Value!) : "I could not add that: " + added.Message;
            case ListCommand.Tick tick:
                var ticked = await lists.SetDoneAsync(ownerId, tick.List, tick.Items, true, cancellationToken);
                return ticked.Succeeded ? ListCommands.Render(ticked.Value!) : "I could not tick that: " + ticked.Message;
            default:
                return ListCommands.UsageText;
        }
    }
}
