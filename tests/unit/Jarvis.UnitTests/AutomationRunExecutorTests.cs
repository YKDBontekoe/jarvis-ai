using System.Text.Json;
using Jarvis.Application.Approvals;
using Jarvis.Application.Automations;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Automations;
using Jarvis.Domain.Conversations;
using Jarvis.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class AutomationRunExecutorTests
{
    private static readonly Guid OwnerId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Scheduled_run_during_cooldown_is_closed_as_skipped_instead_of_left_running()
    {
        var harness = new Harness(Rule(Notify()) with { CooldownUntil = Now.AddMinutes(3) });

        var result = await harness.Executor.ExecuteRunAsync(harness.Input(AutomationTriggerKinds.Schedule),
            CancellationToken.None);

        Assert.True(result.Continue);
        Assert.Equal(AutomationRunStatuses.Skipped, harness.RunStatus);
        Assert.Contains("cooldown", harness.RunSummary);
        Assert.Empty(harness.Notifications);
    }

    [Fact]
    public async Task Run_for_a_deleted_rule_is_closed_as_failed()
    {
        var harness = new Harness(null);

        await harness.Executor.ExecuteRunAsync(harness.Input(AutomationTriggerKinds.Schedule), CancellationToken.None);

        Assert.Equal(AutomationRunStatuses.Failed, harness.RunStatus);
    }

    [Fact]
    public async Task Manual_run_ignores_cooldown_and_does_not_start_a_new_one()
    {
        var harness = new Harness(Rule(Notify()) with { CooldownUntil = Now.AddMinutes(3) });

        await harness.Executor.ExecuteRunAsync(harness.Input(AutomationTriggerKinds.Manual), CancellationToken.None);

        Assert.Equal(AutomationRunStatuses.Completed, harness.RunStatus);
        Assert.Single(harness.Notifications);
        Assert.Null(harness.CooldownApplied);
    }

    [Fact]
    public async Task Test_run_leaves_the_rule_schedule_and_cooldown_alone()
    {
        var harness = new Harness(Rule(Notify()));

        await harness.Executor.ExecuteRunAsync(harness.Input(AutomationTriggerKinds.Manual, testRun: true),
            CancellationToken.None);

        Assert.Equal(AutomationRunStatuses.Completed, harness.RunStatus);
        Assert.False(harness.ScheduleStateUpdated);
    }

    [Fact]
    public async Task Automatic_run_starts_the_configured_cooldown()
    {
        var harness = new Harness(Rule(Notify()));

        await harness.Executor.ExecuteRunAsync(harness.Input(AutomationTriggerKinds.ReminderDue),
            CancellationToken.None);

        Assert.Equal(5, harness.CooldownApplied);
    }

    [Fact]
    public async Task Sensitive_action_waits_in_the_shared_approval_inbox()
    {
        var harness = new Harness(Rule(Notify(), Message()));

        var result = await harness.Executor.ExecuteRunAsync(harness.Input(AutomationTriggerKinds.Schedule),
            CancellationToken.None);

        Assert.True(result.WaitingApproval);
        var approval = Assert.Single(harness.Approvals);
        Assert.Equal(AutomationApprovals.RequestId(harness.RunId), approval.RequestId);
        Assert.Equal("automation_channel_message", approval.ToolName);
        Assert.Equal(harness.ConversationId, approval.ConversationId);
        using var arguments = JsonDocument.Parse(approval.ArgumentsJson);
        Assert.Equal("+31600000000", arguments.RootElement.GetProperty("recipient").GetString());
        Assert.Equal("Send a message", arguments.RootElement.GetProperty("action").GetString());
        Assert.Equal(approval.Id, harness.WaitingApprovalId);
        // The harmless action before it ran; the approval notice is the shared inbox one, not a separate type.
        Assert.Equal(["Hello", "Approval needed"], harness.Notifications);
        Assert.Empty(harness.ChannelMessages);
    }

    [Fact]
    public async Task Standing_approval_runs_a_sensitive_automation_action_without_asking()
    {
        var harness = new Harness(Rule(Message()));
        var category = ApprovalCategories.Resolve("automation_channel_message", "{}");
        Assert.Equal(StandingApprovalGrantResult.Granted,
            await harness.Standing.GrantAsync(OwnerId, category, CancellationToken.None));

        var result = await harness.Executor.ExecuteRunAsync(harness.Input(AutomationTriggerKinds.Schedule),
            CancellationToken.None);

        Assert.False(result.WaitingApproval);
        Assert.Empty(harness.Approvals);
        Assert.Equal(["On my way"], harness.ChannelMessages);
        Assert.Equal(AutomationRunStatuses.Completed, harness.RunStatus);
    }

    [Fact]
    public async Task Approval_card_lists_the_actions_that_approving_releases()
    {
        var harness = new Harness(Rule(Message(), Notify("After")));

        await harness.Executor.ExecuteRunAsync(harness.Input(AutomationTriggerKinds.Schedule), CancellationToken.None);

        using var arguments = JsonDocument.Parse(Assert.Single(harness.Approvals).ArgumentsJson);
        Assert.Equal("Notify you “After”", arguments.RootElement.GetProperty("afterwards").GetString());
    }

    [Fact]
    public async Task Approved_run_sends_the_approved_action_and_everything_after_it_once()
    {
        var harness = new Harness(Rule(Notify(), Message(), Notify("After")));

        await harness.Executor.ExecuteRunAsync(harness.Input(AutomationTriggerKinds.Schedule) with
        {
            AfterApproval = true
        }, CancellationToken.None);

        Assert.Single(harness.ChannelMessages);
        Assert.Equal(["After"], harness.Notifications);
        Assert.Equal(AutomationRunStatuses.Completed, harness.RunStatus);
    }

    [Fact]
    public async Task Reminder_trigger_starts_one_run_per_occurrence_of_a_repeating_reminder()
    {
        var rule = Rule(new ReminderDueTriggerDefinition(null), Notify()) with
        {
            Status = AutomationRuleStatuses.Enabled
        };
        var keys = new List<string>();
        var rules = Fake<IAutomationRuleRepository>.Create(("ListAsync", _ => (IReadOnlyList<AutomationRuleRecord>)[rule]));
        var runs = Fake<IAutomationRunRepository>.Create(("TryStartAsync", args =>
        {
            var input = (AutomationTriggerFireInput)args[0]!;
            var created = !keys.Contains(input.IdempotencyKey);
            keys.Add(input.IdempotencyKey);
            return (RunRecord(input.IdempotencyKey), created);
        }));
        var started = 0;
        var scheduler = Fake<IAutomationScheduler>.Create(("StartRunAsync", _ =>
        {
            started++;
            return Task.CompletedTask;
        }));
        var publisher = new AutomationTriggerPublisher(rules, runs, scheduler);
        var reminderId = Guid.CreateVersion7();

        await publisher.PublishReminderDueAsync(OwnerId, reminderId, "Pills", Now, CancellationToken.None);
        await publisher.PublishReminderDueAsync(OwnerId, reminderId, "Pills", Now, CancellationToken.None);
        await publisher.PublishReminderDueAsync(OwnerId, reminderId, "Pills", Now.AddDays(1), CancellationToken.None);

        Assert.Equal(2, started);
    }

    private static NotificationActionDefinition Notify(string title = "Hello") => new(title, "Body");

    private static ChannelMessageActionDefinition Message() => new(Guid.CreateVersion7(), "+31600000000", "On my way");

    private static AutomationRuleRecord Rule(params AutomationActionDefinition[] actions) =>
        Rule(new ScheduleTriggerDefinition(new TimeOnly(9, 0), "Europe/Amsterdam", null), actions);

    private static AutomationRuleRecord Rule(AutomationTriggerDefinition trigger,
        params AutomationActionDefinition[] actions)
    {
        var definition = new AutomationRuleDefinition(AutomationSchema.CurrentVersion, trigger, null, actions,
            new AutomationLimitsDefinition(null, null, 5));
        return new AutomationRuleRecord(Guid.CreateVersion7(), OwnerId, "Morning", 1,
            AutomationDefinitionJson.Serialize(definition), AutomationRuleStatuses.Enabled, "wf", null, null, null,
            null, Now, Now, Guid.CreateVersion7());
    }

    private static AutomationRunRecord RunRecord(string key) => new(Guid.CreateVersion7(), Guid.CreateVersion7(),
        OwnerId, "wf-run", key, AutomationTriggerKinds.ReminderDue, "Reminder due", false,
        AutomationRunStatuses.Running, "[]", null, null, Now, null);

    private sealed class Harness
    {
        public Guid RunId { get; } = Guid.CreateVersion7();
        public Guid ConversationId { get; }
        public string? RunStatus { get; private set; } = AutomationRunStatuses.Running;
        public string RunSummary { get; private set; } = string.Empty;
        public Guid? WaitingApprovalId { get; private set; }
        public int? CooldownApplied { get; private set; }
        public bool ScheduleStateUpdated { get; private set; }
        public List<string> Notifications { get; } = [];
        public List<string> ChannelMessages { get; } = [];
        public List<ToolApprovalRecord> Approvals { get; } = [];
        public AutomationRunExecutor Executor { get; }
        public StandingApprovalService Standing { get; } = new(new InMemorySettingsStore(), new NullAudit());
        private readonly AutomationRuleRecord? _rule;

        public Harness(AutomationRuleRecord? rule)
        {
            _rule = rule;
            ConversationId = rule?.ConversationId ?? Guid.CreateVersion7();
            var rules = Fake<IAutomationRuleRepository>.Create(
                ("GetForExecutionAsync", _ => rule),
                ("EnsureConversationAsync", _ => ConversationId),
                ("UpdateScheduleStateAsync", args =>
                {
                    ScheduleStateUpdated = true;
                    CooldownApplied = (int?)args[2];
                    return Task.CompletedTask;
                }));
            var runs = Fake<IAutomationRunRepository>.Create(
                ("CompleteAsync", _ => Close(AutomationRunStatuses.Completed, string.Empty)),
                ("FailAsync", args => Close(AutomationRunStatuses.Failed, (string?)args[1] ?? string.Empty)),
                ("SkipAsync", args => Close(AutomationRunStatuses.Skipped, (string)args[1]!)),
                ("MarkWaitingApprovalAsync", args =>
                {
                    RunStatus = AutomationRunStatuses.WaitingApproval;
                    WaitingApprovalId = (Guid)args[1]!;
                    return Task.CompletedTask;
                }));
            var notifications = Fake<INotificationRepository>.Create(("CreateAsync", args =>
            {
                Notifications.Add((string)args[2]!);
                return new NotificationRecord(Guid.CreateVersion7(), (string)args[1]!, (string)args[2]!,
                    (string)args[3]!, (Guid?)args[4], Now, null);
            }));
            var approvals = Fake<IToolApprovalStore>.Create(("CreateAsync", args =>
            {
                var record = new ToolApprovalRecord(Guid.CreateVersion7(), (Guid)args[0]!, (Guid)args[1]!, null,
                    (string)args[2]!, (string)args[3]!, (string)args[4]!, (string)args[5]!, "pending", null,
                    "not_started", null, Now, null);
                Approvals.Add(record);
                Notifications.Add("Approval needed");
                return new ToolApprovalCreateResult(record, true, Guid.CreateVersion7());
            }));
            var channel = Fake<IAutomationChannelSender>.Create(("SendPreconfiguredAsync", args =>
            {
                ChannelMessages.Add((string)args[3]!);
                return Guid.CreateVersion7();
            }));
            var conversations = Fake<IConversationStore>.Create(("AddMessageAsync", _ => Task.CompletedTask));
            var clock = new FixedClock(Now);
            Executor = new AutomationRunExecutor(rules, runs, notifications, approvals, Standing,
                Fake<IJarvisTaskService>.Create(), channel, conversations,
                new AutomationConditionEvaluator(Fake<IAutomationMetrics>.Create(), clock), clock,
                NullLogger<AutomationRunExecutor>.Instance);
        }

        public AutomationRunWorkflowInput Input(string triggerKind, bool testRun = false) =>
            new(_rule?.Id ?? Guid.CreateVersion7(), OwnerId, RunId, "key", triggerKind, "Because", testRun);

        private Task Close(string status, string summary)
        {
            RunStatus = status;
            RunSummary = summary;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
