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

    [Fact]
    public void AttachConversation_stores_the_linked_chat()
    {
        var rule = new AutomationRule(Guid.CreateVersion7(), "Morning", "{}", 1);
        var conversationId = Guid.CreateVersion7();
        rule.AttachConversation(conversationId);
        Assert.Equal(conversationId, rule.ConversationId);
        Assert.Throws<ArgumentException>(() => rule.AttachConversation(Guid.Empty));
    }

    [Theory]
    [InlineData("{\"trigger\": 5}")]
    [InlineData("{\"trigger\": {}, \"actions\": []}")]
    [InlineData("{\"trigger\": {\"kind\": 3}, \"actions\": []}")]
    [InlineData("{\"trigger\": {\"kind\": \"manual\"}, \"actions\": [{}]}")]
    [InlineData("{\"trigger\": {\"kind\": \"manual\"}, \"actions\": [\"x\"]}")]
    [InlineData("{\"trigger\": {\"kind\": \"manual\"}, \"conditions\": [{}], \"actions\": []}")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void A_malformed_definition_is_a_json_error_the_api_can_report_not_a_crash(string json) =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => AutomationDefinitionJson.Deserialize(json));

    [Fact]
    public void A_request_without_a_definition_is_a_json_error()
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<SaveAutomationRuleRequest>(
            "{\"name\": \"No definition\"}")!;

        Assert.Throws<System.Text.Json.JsonException>(() => request.ParseDefinition());
    }

    [Theory]
    [InlineData("{\"trigger\": {\"kind\": \"schedule\", \"timeZoneId\": \"Mars/Olympus\"}, \"actions\": [{\"kind\": \"notification\", \"title\": \"a\", \"body\": \"b\"}]}")]
    [InlineData("{\"trigger\": {\"kind\": \"schedule\"}, \"actions\": [{\"kind\": \"notification\", \"title\": \"a\", \"body\": \"b\"}]}")]
    [InlineData("{\"schemaVersion\": 1, \"trigger\": {\"kind\": \"manual\"}, \"conditions\": [{\"kind\": \"time_window\", \"timeZoneId\": \"Nowhere/City\"}], \"actions\": [{\"kind\": \"notification\", \"title\": \"a\", \"body\": \"b\"}]}")]
    [InlineData("{}")]
    public void An_unusable_definition_is_reported_as_invalid_not_thrown_as_a_server_error(string json)
    {
        var definition = AutomationDefinitionJson.Deserialize(json);

        Assert.ThrowsAny<ArgumentException>(() => AutomationRuleValidator.Validate(definition));
    }
}
