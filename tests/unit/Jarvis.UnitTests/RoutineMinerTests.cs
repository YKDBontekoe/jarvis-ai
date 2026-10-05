using Jarvis.Application.Automations;
using Jarvis.Application.Routines;
using Jarvis.Application.Timeline;
using Jarvis.Domain.Automations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class RoutineMinerTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    // A Friday.
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static TimelineEvent Event(string kind, string title, DateTimeOffset at) =>
        new($"{kind}:{at:O}", kind, title, null, at, DateOnly.FromDateTime(at.UtcDateTime));

    [Fact]
    public void Nightly_journaling_becomes_a_schedule_suggestion_that_validates()
    {
        var events = Enumerable.Range(1, 40)
            .Select(i => Now.Date.AddDays(-i).AddHours(22).AddMinutes(i % 3 * 5))
            .Select(d => Event(TimelineKinds.Journal, "My day", new DateTimeOffset(d, TimeSpan.Zero)))
            .ToList();

        var suggestions = RoutineMinerEngine.Mine(events, Utc, Now);

        var suggestion = Assert.Single(suggestions, s => s.Fingerprint == "time:journal");
        var trigger = Assert.IsType<ScheduleTriggerDefinition>(suggestion.Definition.Trigger);
        Assert.Equal(22, trigger.LocalTime.Hour);
        Assert.Null(trigger.Weekdays);
        AutomationRuleValidator.Validate(suggestion.Definition);
        Assert.Contains("22:", suggestion.Evidence);
        Assert.InRange(suggestion.Confidence, 0.5, 1.0);
    }

    [Fact]
    public void A_weekday_only_habit_keeps_only_those_weekdays()
    {
        var events = new List<TimelineEvent>();
        for (var i = 1; i <= 56; i++)
        {
            var day = Now.Date.AddDays(-i);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            events.Add(Event(TimelineKinds.Habit, "Stretch", new DateTimeOffset(day.AddHours(7), TimeSpan.Zero)));
        }

        var suggestion = Assert.Single(RoutineMinerEngine.Mine(events, Utc, Now));
        var trigger = Assert.IsType<ScheduleTriggerDefinition>(suggestion.Definition.Trigger);
        Assert.Equal(0b0011111, trigger.Weekdays);
        Assert.Contains("weekday", suggestion.Evidence);
    }

    [Fact]
    public void A_handful_of_scattered_events_is_not_a_routine()
    {
        var events = new[] { 3, 10, 17 }.Select(i =>
            Event(TimelineKinds.Journal, "x", new DateTimeOffset(Now.Date.AddDays(-i).AddHours(i), TimeSpan.Zero)))
            .ToList();

        Assert.Empty(RoutineMinerEngine.Mine(events, Utc, Now));
    }

    [Fact]
    public void Events_older_than_the_window_are_ignored()
    {
        var events = Enumerable.Range(80, 40)
            .Select(i => Event(TimelineKinds.Journal, "My day",
                new DateTimeOffset(Now.Date.AddDays(-i).AddHours(22), TimeSpan.Zero)))
            .ToList();

        Assert.Empty(RoutineMinerEngine.Mine(events, Utc, Now));
    }

    [Fact]
    public void An_expense_that_is_reliably_followed_by_journaling_becomes_an_event_suggestion()
    {
        var events = new List<TimelineEvent>();
        for (var i = 1; i <= 10; i++)
        {
            // Irregular days and hours so no time habit competes.
            var at = new DateTimeOffset(Now.Date.AddDays(-i * 3).AddHours(8 + i), TimeSpan.Zero);
            events.Add(Event(TimelineKinds.Expense, "Coffee", at));
            events.Add(Event(TimelineKinds.Journal, "Reflect", at.AddMinutes(40)));
        }

        var suggestion = Assert.Single(RoutineMinerEngine.Mine(events, Utc, Now),
            s => s.Fingerprint == "follow:expense>journal");
        var trigger = Assert.IsType<EventTriggerDefinition>(suggestion.Definition.Trigger);
        Assert.Equal(AutomationEventKinds.ExpenseLogged, trigger.EventKind);
        Assert.Equal(RoutineMinerEngine.FollowUpCooldownMinutes, suggestion.Definition.Limits?.CooldownMinutes);
        AutomationRuleValidator.Validate(suggestion.Definition);
    }

    [Fact]
    public void A_follow_up_that_happens_only_sometimes_is_not_suggested()
    {
        var events = new List<TimelineEvent>();
        for (var i = 1; i <= 10; i++)
        {
            var at = new DateTimeOffset(Now.Date.AddDays(-i * 3).AddHours(8 + i), TimeSpan.Zero);
            events.Add(Event(TimelineKinds.Expense, "Coffee", at));
            if (i % 3 == 0) events.Add(Event(TimelineKinds.Journal, "Reflect", at.AddMinutes(40)));
        }

        Assert.DoesNotContain(RoutineMinerEngine.Mine(events, Utc, Now), s => s.Fingerprint.StartsWith("follow:"));
    }

    [Fact]
    public void A_recurring_task_gets_a_task_action_but_never_a_task_trigger()
    {
        var events = new List<TimelineEvent>();
        for (var i = 1; i <= 56; i++)
        {
            var day = Now.Date.AddDays(-i);
            if (day.DayOfWeek != DayOfWeek.Monday) continue;
            events.Add(Event(TimelineKinds.Task, "Weekly report", new DateTimeOffset(day.AddHours(9), TimeSpan.Zero)));
        }

        var suggestion = Assert.Single(RoutineMinerEngine.Mine(events, Utc, Now));
        Assert.IsType<ScheduleTriggerDefinition>(suggestion.Definition.Trigger);
        Assert.IsType<TaskActionDefinition>(Assert.Single(suggestion.Definition.Actions));
    }

    [Fact]
    public void Suggestions_are_capped_and_ranked_by_confidence()
    {
        var events = new List<TimelineEvent>();
        for (var h = 0; h < 8; h++)
            for (var i = 1; i <= 50; i++)
                events.Add(Event(TimelineKinds.Habit, $"Habit {h}",
                    new DateTimeOffset(Now.Date.AddDays(-i).AddHours(h * 2 + 1), TimeSpan.Zero)));

        var suggestions = RoutineMinerEngine.Mine(events, Utc, Now);

        Assert.Equal(RoutineMinerEngine.MaxSuggestions, suggestions.Count);
        Assert.Equal(suggestions.OrderByDescending(s => s.Confidence).Select(s => s.Fingerprint),
            suggestions.Select(s => s.Fingerprint));
    }
}
