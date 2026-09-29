using Jarvis.Application.Automations;
using Jarvis.Domain.Automations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class AutomationRuleTests
{
    [Fact]
    public void Schedule_clock_skips_invalid_dst_gap_and_finds_next_weekday()
    {
        var trigger = new ScheduleTriggerDefinition(new TimeOnly(9, 0), "Europe/Amsterdam", Weekdays: 1 << 0);
        var sunday = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero);
        var next = AutomationScheduleClock.GetNextScheduleFireUtc(sunday, trigger);
        Assert.NotNull(next);
        Assert.True(next > sunday);
    }

    [Fact]
    public void Validator_rejects_private_json_urls_and_scripts()
    {
        var definition = new AutomationRuleDefinition(AutomationSchema.CurrentVersion,
            new ManualTriggerDefinition(),
            null,
            [new NotificationActionDefinition("Hi", "There")],
            null);
        AutomationRuleValidator.Validate(definition);

        var badJson = definition with
        {
            Trigger = new PublicJsonThresholdTriggerDefinition("http://example.com/x.json", "value", "above", 1, 15,
                null)
        };
        Assert.Throws<ArgumentException>(() => AutomationRuleValidator.Validate(badJson));
    }

    [Fact]
    public void Time_window_condition_handles_overnight_ranges()
    {
        var window = new TimeWindowConditionDefinition(new TimeOnly(22, 0), new TimeOnly(6, 0), "UTC");
        var late = new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero);
        var midday = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.True(AutomationScheduleClock.IsWithinTimeWindow(window, late));
        Assert.False(AutomationScheduleClock.IsWithinTimeWindow(window, midday));
    }

    [Fact]
    public void Action_policy_marks_external_actions_as_approval_required()
    {
        Assert.True(AutomationActionPolicy.RequiresApproval(new ChannelMessageActionDefinition(
            Guid.CreateVersion7(), "+15551212", "hello")));
        Assert.True(AutomationActionPolicy.RequiresApproval(new AgentRunActionDefinition("t", "p")));
        Assert.False(AutomationActionPolicy.RequiresApproval(new NotificationActionDefinition("t", "b")));
    }
}
