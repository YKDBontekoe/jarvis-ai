using Jarvis.Api.Channels;
using Jarvis.Application.Channels;
using Jarvis.Application.Expenses;
using Jarvis.Application.Habits;
using Jarvis.Application.Lists;
using Jarvis.Application.Planner;
using Jarvis.Application.Preferences;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ChannelCommandHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ChannelCommandHandler Create(IListService? lists = null, IExpenseService? expenses = null,
        Jarvis.Application.Journal.IJournalService? journal = null, Jarvis.Application.Audit.IAuditEventStore? audit = null, INotificationRepository? notifications = null,
        IReminderService? reminders = null) => new(
        lists ?? new ListService(new InMemorySettingsStore()),
        reminders ?? Fake<IReminderService>.Create(),
        Fake<IOwnerPreferenceService>.Create(("GetAsync", _ => new PreferencesView("Europe/Amsterdam", "chosen", true, "now", null))),
        Fake<IHabitService>.Create(),
        expenses ?? Fake<IExpenseService>.Create(),
        Fake<IDayPlannerService>.Create(),
        journal ?? Fake<Jarvis.Application.Journal.IJournalService>.Create(),
        notifications ?? Fake<INotificationRepository>.Create(),
        audit ?? Fake<Jarvis.Application.Audit.IAuditEventStore>.Create(),
        new FixedTime(new DateTimeOffset(2026, 11, 7, 20, 30, 0, TimeSpan.Zero)));

    [Theory]
    [InlineData("What is on my shopping list?")]
    [InlineData("/new")]
    [InlineData("/help")]
    [InlineData("hello")]
    public async Task Ordinary_messages_and_other_slash_commands_are_left_to_the_assistant(string text) =>
        Assert.Null(await Create().TryHandleAsync(Owner, text, default));

    [Fact]
    public async Task List_commands_run_for_the_owner_of_the_connection_only()
    {
        var lists = new ListService(new InMemorySettingsStore());
        var handler = Create(lists);

        var added = await handler.TryHandleAsync(Owner, "/add Shopping: milk, eggs", default);
        var shown = await handler.TryHandleAsync(Owner, "/lists", default);
        var elsewhere = await handler.TryHandleAsync(Guid.NewGuid(), "/lists", default);

        Assert.Contains("milk", added, StringComparison.Ordinal);
        Assert.Contains("Shopping (2 open)", shown, StringComparison.Ordinal);
        Assert.StartsWith("No lists yet", elsewhere, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_malformed_command_answers_with_its_format_and_a_spend_goes_to_the_expense_service_as_chat()
    {
        ExpenseDraft? seen = null;
        var expenses = Fake<IExpenseService>.Create(("CreateAsync", args =>
        {
            seen = (ExpenseDraft)args[1]!;
            return ExpenseOperation<ExpenseCreated>.Ok(new ExpenseCreated(new Jarvis.Domain.Expenses.Expense(Guid.NewGuid(), Owner,
                12.5m, "EUR", "lunch", "food", null, new DateOnly(2026, 11, 7), null, ExpenseSources.Chat,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), false));
        }));
        var handler = Create(expenses: expenses);

        Assert.Equal(QuickCommands.SpentUsage, await handler.TryHandleAsync(Owner, "/spent lunch", default));
        var reply = await handler.TryHandleAsync(Owner, "/spent 12,50 lunch", default);

        Assert.Contains("Logged 12.50 EUR at lunch", reply, StringComparison.Ordinal);
        Assert.Equal(12.5m, seen!.Amount);
        Assert.Equal(ExpenseSources.Chat, seen.Source);
    }

    [Fact]
    public async Task A_journal_command_adds_to_todays_entry_in_the_owners_zone_and_audits_the_id_only()
    {
        Jarvis.Application.Journal.JournalDraft? seen = null;
        var entryId = Guid.NewGuid();
        var journal = Fake<Jarvis.Application.Journal.IJournalService>.Create(("MergeAsync", args =>
        {
            seen = (Jarvis.Application.Journal.JournalDraft)args[1]!;
            return (new Jarvis.Domain.Journal.JournalEntry(entryId, Owner, seen.EntryDate, "chat", seen.Content!, null, null, null, null, null, null,
                [], null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), true);
        }));
        var metadata = new List<string?>();
        var audit = Fake<Jarvis.Application.Audit.IAuditEventStore>.Create(("AppendAsync", args =>
        {
            metadata.Add((string?)args[6]);
            return new Jarvis.Application.Audit.AuditEventRecord(Guid.NewGuid(), null, "journal", "journal.updated", "low", null,
                DateTimeOffset.UtcNow, true, null);
        }));
        var handler = Create(journal: journal, audit: audit);

        var reply = await handler.TryHandleAsync(Owner, "/journal Walked along the canal and felt calm.", default);

        Assert.Contains("Added to your journal", reply, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 11, 7), seen!.EntryDate); // 20:30 UTC is 21:30 in Amsterdam, still the 7th
        Assert.Equal("chat", seen.Source);
        var logged = Assert.Single(metadata);
        Assert.Contains(entryId.ToString(), logged, StringComparison.Ordinal);
        Assert.DoesNotContain("canal", logged, StringComparison.Ordinal);
        Assert.Equal(QuickCommands.JournalUsage, await handler.TryHandleAsync(Owner, "/journal", default));
    }

    [Fact]
    public async Task Snooze_moves_the_newest_reminder_that_went_off_and_ignores_old_or_missing_ones()
    {
        var reminderId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 7, 20, 0, 0, TimeSpan.Zero); // 30 minutes before the handler's clock
        Guid? snoozed = null;
        DateTimeOffset? newDue = null;
        var reminders = Fake<IReminderService>.Create(("SnoozeAsync", args =>
        {
            snoozed = (Guid)args[0]!;
            newDue = (DateTimeOffset)args[2]!;
            return new ReminderRecord(reminderId, Owner, "Call mum", newDue.Value, "wf", "pending", at, null);
        }));
        List<NotificationRecord> list = [
            new(Guid.NewGuid(), "reminder.due", "Old", "", Guid.NewGuid(), at.AddHours(-3), null),
            new(Guid.NewGuid(), "reminder.due", "Call mum", "", reminderId, at, null),
            new(Guid.NewGuid(), "task.done", "Task", "", Guid.NewGuid(), at.AddMinutes(10), null),
        ];
        var notifications = Fake<INotificationRepository>.Create(("ListNotificationsAsync", _ => (IReadOnlyList<NotificationRecord>)list));
        var handler = Create(reminders: reminders, notifications: notifications);

        var reply = await handler.TryHandleAsync(Owner, "/snooze 15m", default);

        Assert.Contains("Snoozed until", reply, StringComparison.Ordinal);
        Assert.Equal(reminderId, snoozed);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 20, 45, 0, TimeSpan.Zero), newDue);
        list.Clear();
        Assert.StartsWith("No reminder went off", await handler.TryHandleAsync(Owner, "/snooze", default), StringComparison.Ordinal);
    }
}
