using System.Diagnostics;
using System.Text.Json;
using Jarvis.Application.Audit;
using Microsoft.Extensions.Logging;
using Jarvis.Application.Automations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Automations;

namespace Jarvis.Workflows;

public sealed class AutomationRunExecutor(
    IAutomationRuleRepository rules,
    IAutomationRunRepository runs,
    INotificationRepository notifications,
    IJarvisTaskService tasks,
    IAutomationChannelSender channelSender,
    AutomationConditionEvaluator conditions,
    IAuditEventStore auditEvents,
    TimeProvider timeProvider,
    ILogger<AutomationRunExecutor> logger) : IAutomationRunExecutor
{
    public async Task<AutomationRunActivityResult> ExecuteRunAsync(AutomationRunWorkflowInput input,
        CancellationToken cancellationToken)
    {
        var rule = await rules.GetForExecutionAsync(input.RuleId, cancellationToken);
        if (rule is null || rule.OwnerId != input.OwnerId)
            return Failed("Rule not found.");

        if (!input.AfterApproval && rule.Status != AutomationRuleStatuses.Enabled && !input.TestRun)
            return Failed("Rule is not enabled.");

        var definition = AutomationDefinitionJson.Parse(rule.DefinitionJson);
        if (!input.AfterApproval && !input.TestRun && rule.CooldownUntil is not null &&
            rule.CooldownUntil > timeProvider.GetUtcNow())
            return Failed("Rule is in cooldown.");

        if (!input.AfterApproval &&
            !await conditions.EvaluateAllAsync(input.OwnerId, definition.Conditions, cancellationToken))
        {
            var skipped = Serialize([new AutomationActionResult("conditions", "skipped", "Conditions not met.", null)]);
            await runs.CompleteAsync(input.RunId, skipped, cancellationToken);
            return new AutomationRunActivityResult(true, skipped, false);
        }

        var limits = definition.Limits;
        var maxActions = limits?.MaxActionsPerRun ?? AutomationSchema.DefaultMaxActionsPerRun;
        var maxDuration = TimeSpan.FromMinutes(limits?.MaxRunDurationMinutes ?? AutomationSchema.DefaultMaxRunDurationMinutes);
        var deadline = timeProvider.GetUtcNow() + maxDuration;
        var results = new List<AutomationActionResult>();
        var actionIndex = 0;

        foreach (var action in definition.Actions)
        {
            if (input.AfterApproval && !AutomationActionPolicy.RequiresApproval(action)) continue;
            if (actionIndex >= maxActions) break;
            if (timeProvider.GetUtcNow() > deadline)
            {
                results.Add(new AutomationActionResult(action.Kind, "skipped", "Run duration limit reached.", null));
                break;
            }

            if (AutomationActionPolicy.RequiresApproval(action) && !input.TestRun && !input.AfterApproval)
            {
                var approvalId = Guid.CreateVersion7();
                await runs.MarkWaitingApprovalAsync(input.RunId, approvalId, cancellationToken);
                await notifications.CreateAsync(input.OwnerId, "automation.approval",
                    "Automation needs approval", $"Approve action '{action.Kind}' for {rule.Name}.",
                    input.RunId, cancellationToken);
                await auditEvents.AppendAsync(input.OwnerId, "automations", "run.waiting_approval", "high", true,
                    approvalId, JsonSerializer.Serialize(new { runId = input.RunId, action = action.Kind }),
                    cancellationToken);
                results.Add(new AutomationActionResult(action.Kind, "waiting_approval", null, approvalId));
                return new AutomationRunActivityResult(false, Serialize(results), true);
            }

            try
            {
                results.Add(await ExecuteActionAsync(input, action, cancellationToken));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Automation action {ActionKind} failed for run {RunId}.", action.Kind,
                    input.RunId);
                results.Add(new AutomationActionResult(action.Kind, "failed",
                    Sanitize(exception.Message), null));
            }

            actionIndex++;
        }

        var json = Serialize(results);
        await runs.CompleteAsync(input.RunId, json, cancellationToken);
        var cooldown = limits?.CooldownMinutes ?? AutomationSchema.DefaultCooldownMinutes;
        DateTimeOffset? next = definition.Trigger is ScheduleTriggerDefinition schedule
            ? AutomationScheduleClock.GetNextScheduleFireUtc(timeProvider.GetUtcNow(), schedule)
            : null;
        await rules.UpdateScheduleStateAsync(input.RuleId, next, cooldown, cancellationToken);
        return new AutomationRunActivityResult(true, json, false);
    }

    private async Task<AutomationActionResult> ExecuteActionAsync(AutomationRunWorkflowInput input,
        AutomationActionDefinition action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case NotificationActionDefinition notification:
            {
                var record = await notifications.CreateAsync(input.OwnerId, "automation.notification",
                    notification.Title, notification.Body, input.RuleId, cancellationToken);
                return new AutomationActionResult(action.Kind, "completed", null, record.Id);
            }
            case TaskActionDefinition task:
            {
                var created = await tasks.CreateAsync(input.OwnerId, task.Title, task.Prompt, cancellationToken);
                return new AutomationActionResult(action.Kind, "completed", null, created.Id);
            }
            case ChannelMessageActionDefinition channel:
            {
                if (input.TestRun)
                    return new AutomationActionResult(action.Kind, "skipped", "Test run does not send messages.", null);
                var messageId = await channelSender.SendPreconfiguredAsync(input.OwnerId, channel.ConnectionId,
                    channel.Recipient, channel.Body, cancellationToken);
                return new AutomationActionResult(action.Kind, "completed", null, messageId);
            }
            case AgentRunActionDefinition agent:
            {
                if (input.TestRun)
                    return new AutomationActionResult(action.Kind, "skipped", "Test run does not start agent tasks.", null);
                var created = await tasks.CreateAsync(input.OwnerId, agent.Title, agent.Prompt, cancellationToken);
                return new AutomationActionResult(action.Kind, "completed", null, created.Id);
            }
            default:
                return new AutomationActionResult(action.Kind, "failed", "Unknown action.", null);
        }
    }

    private AutomationRunActivityResult Failed(string summary) =>
        new(false, Serialize([new AutomationActionResult("run", "failed", summary, null)]), false);

    private static string Serialize(IReadOnlyList<AutomationActionResult> results) =>
        JsonSerializer.Serialize(results);

    private static string Sanitize(string message) =>
        message.Length <= 200 ? message : message[..200];
}
