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

    private static ChannelCommandHandler Create(IListService? lists = null, IExpenseService? expenses = null) => new(
        lists ?? new ListService(new InMemorySettingsStore()),
        Fake<IReminderService>.Create(),
        Fake<IOwnerPreferenceService>.Create(("GetAsync", _ => new PreferencesView("Europe/Amsterdam", "chosen", true, "now", null))),
        Fake<IHabitService>.Create(),
        expenses ?? Fake<IExpenseService>.Create(),
        Fake<IDayPlannerService>.Create());

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
}
