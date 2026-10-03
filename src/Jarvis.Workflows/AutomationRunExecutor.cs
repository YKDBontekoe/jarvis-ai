using System.Text.Json;
using Jarvis.Application.Approvals;
using Microsoft.Extensions.Logging;
using Jarvis.Application.Automations;
using Jarvis.Application.Conversations;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Automations;
using Jarvis.Domain.Conversations;

namespace Jarvis.Workflows;

public sealed class AutomationRunExecutor(
    IAutomationRuleRepository rules,
    IAutomationRunRepository runs,
    INotificationRepository notifications,
    IToolApprovalStore approvals,
    IStandingApprovalService standingApprovals,
    IJarvisTaskService tasks,
    IAutomationChannelSender channelSender,
    IConversationStore conversations,
    AutomationConditionEvaluator conditions,
    TimeProvider timeProvider,
    ILogger<AutomationRunExecutor> logger,
    Jarvis.Application.Modes.IModeService? modes = null) : IAutomationRunExecutor
{
    public async Task<AutomationRunActivityResult> ExecuteRunAsync(AutomationRunWorkflowInput input,
        CancellationToken cancellationToken)
    {
        var rule = await rules.GetForExecutionAsync(input.RuleId, cancellationToken);
        if (rule is null || rule.OwnerId != input.OwnerId)
            return await FailAsync(input, "The automation no longer exists.", cancellationToken);

        if (!input.AfterApproval && rule.Status != AutomationRuleStatuses.Enabled && !input.TestRun)
            return await SkipAsync(input, rule, "The automation is switched off.", cancellationToken);

        var definition = AutomationDefinitionJson.Parse(rule.DefinitionJson);
        var ev = AutomationEvent.FromJson(input.EventJson);
        var ownerAsked = input.TestRun || input.TriggerKind == AutomationTriggerKinds.Manual;
        if (!input.AfterApproval && !ownerAsked && rule.CooldownUntil is not null &&
            rule.CooldownUntil > timeProvider.GetUtcNow())
            return await SkipAsync(input, rule, "It already ran moments ago (cooldown).", cancellationToken);

        if (!input.AfterApproval &&
            !await conditions.EvaluateAllAsync(input.OwnerId, definition.Conditions, cancellationToken))
            return await SkipAsync(input, rule, "Its conditions were not met.", cancellationToken);

        var limits = definition.Limits;
        var maxActions = limits?.MaxActionsPerRun ?? AutomationSchema.DefaultMaxActionsPerRun;
        var maxDuration = TimeSpan.FromMinutes(limits?.MaxRunDurationMinutes ?? AutomationSchema.DefaultMaxRunDurationMinutes);
        var deadline = timeProvider.GetUtcNow() + maxDuration;
        var results = new List<AutomationActionResult>();
        var actionIndex = 0;
        // Actions before the first one that needs approval already ran in the first pass; the approved pass
        // picks up from that action and runs everything after it too.
        var resumeFrom = input.AfterApproval ? FirstApprovalIndex(definition.Actions, ev) : 0;

        for (var index = resumeFrom; index < definition.Actions.Count; index++)
        {
            var action = definition.Actions[index];
            // A branch that does not apply is skipped without costing an action or asking for approval.
            if (!AutomationActionConditions.Matches(action.If, ev))
            {
                results.Add(new AutomationActionResult(action.Kind, "skipped", "Its condition was not met.", null));
                continue;
            }
            if (actionIndex >= maxActions) break;
            if (timeProvider.GetUtcNow() > deadline)
            {
                results.Add(new AutomationActionResult(action.Kind, "skipped", "Run duration limit reached.", null));
                break;
            }

            if (AutomationActionPolicy.RequiresApproval(action) && !input.TestRun && !input.AfterApproval)
            {
                var toolName = AutomationApprovals.ToolName(action);
                var arguments = ApprovalArguments(rule, definition.Actions, index, ev);
                var category = ApprovalCategories.Resolve(toolName, arguments);
                if (category.CanRemember &&
                    await standingApprovals.IsGrantedAsync(input.OwnerId, category.Key, cancellationToken))
                {
                    await standingApprovals.RecordAutomaticUseAsync(input.OwnerId, toolName, category,
                        rule.ConversationId, cancellationToken);
                }
                else
                {
                    var approval = await approvals.CreateAsync(input.OwnerId,
                        rule.ConversationId ?? await rules.EnsureConversationAsync(rule.Id, cancellationToken),
                        AutomationApprovals.RequestId(input.RunId), $"action-{index}",
                        toolName, arguments, null, cancellationToken);
                    await runs.MarkWaitingApprovalAsync(input.RunId, approval.Approval.Id, cancellationToken);
                    results.Add(new AutomationActionResult(action.Kind, "waiting_approval", null, approval.Approval.Id));
                    if (approval.Created)
                        await PostToLinkedChatAsync(rule, LinkedConversationCopy.AutomationWaitingApproval(rule.Name,
                            Label(action)), cancellationToken);
                    return new AutomationRunActivityResult(false, Serialize(results), true);
                }
            }

            try
            {
                results.Add(await ExecuteActionAsync(input, action, ev, cancellationToken));
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
        var failed = results.Any(result => result.Status == "failed");
        if (failed)
            await runs.FailAsync(input.RunId, "One or more actions failed.", json, cancellationToken);
        else
            await runs.CompleteAsync(input.RunId, json, cancellationToken);
        if (!input.TestRun)
        {
            // Only automatic triggers start a cooldown; a run the owner asked for must not block the next
            // scheduled one.
            int? cooldown = ownerAsked ? null : limits?.CooldownMinutes ?? AutomationSchema.DefaultCooldownMinutes;
            DateTimeOffset? next = definition.Trigger is ScheduleTriggerDefinition schedule && rule.Status ==
                AutomationRuleStatuses.Enabled
                ? AutomationScheduleClock.GetNextScheduleFireUtc(timeProvider.GetUtcNow(), schedule)
                : null;
            await rules.UpdateScheduleStateAsync(input.RuleId, next, cooldown, cancellationToken);
        }
        await PostToLinkedChatAsync(rule, LinkedConversationCopy.AutomationRun(rule.Name,
            failed ? "failed" : "completed", input.TriggerReason, json, input.TestRun), cancellationToken);
        return new AutomationRunActivityResult(true, json, false);
    }

    private static int FirstApprovalIndex(IReadOnlyList<AutomationActionDefinition> actions, AutomationEvent? ev)
    {
        for (var index = 0; index < actions.Count; index++)
            if (AutomationActionConditions.Matches(actions[index].If, ev) &&
                AutomationActionPolicy.RequiresApproval(actions[index]))
                return index;
        return actions.Count;
    }

    /// <summary>
    /// What the approval card shows: the automation, the exact action waiting for a decision and, in plain words,
    /// the actions that follow it, because approving releases all of them.
    /// </summary>
    private static string ApprovalArguments(AutomationRuleRecord rule,
        IReadOnlyList<AutomationActionDefinition> actions, int index, AutomationEvent? ev)
    {
        var fields = new Dictionary<string, string> { ["automation"] = rule.Name, ["action"] = Label(actions[index]) };
        // The card shows the texts as they will be sent, with the event filled in.
        var (title, body) = AutomationSimulator.Texts(actions[index], ev);
        switch (actions[index])
        {
            case ChannelMessageActionDefinition channel:
                fields["recipient"] = channel.Recipient;
                fields["message"] = Preview(body ?? channel.Body);
                break;
            case AgentRunActionDefinition:
                fields["title"] = title ?? "";
                fields["instructions"] = Preview(body ?? "");
                break;
            case TaskActionDefinition:
                fields["title"] = title ?? "";
                fields["instructions"] = Preview(body ?? "");
                break;
            case NotificationActionDefinition:
                fields["title"] = title ?? "";
                fields["message"] = Preview(body ?? "");
                break;
        }
        if (ev is not null) fields["triggeredBy"] = Preview($"{ev.Kind}: {ev.Title}", 160);

        var afterwards = actions.Skip(index + 1).Where(x => AutomationActionConditions.Matches(x.If, ev))
            .Select(Summary).ToArray();
        if (afterwards.Length > 0) fields["afterwards"] = string.Join("; ", afterwards);
        return JsonSerializer.Serialize(fields);
    }

    private static string Label(AutomationActionDefinition action) => action switch
    {
        ChannelMessageActionDefinition => "Send a message",
        AgentRunActionDefinition => "Start an agent task",
        TaskActionDefinition => "Create a task",
        NotificationActionDefinition => "Notify you",
        SetModeActionDefinition => "Switch mode",
        _ => action.Kind
    };

    private static string Summary(AutomationActionDefinition action) => action switch
    {
        ChannelMessageActionDefinition channel => $"Send “{Preview(channel.Body, 80)}” to {channel.Recipient}",
        AgentRunActionDefinition agent => $"Start an agent task “{agent.Title}”",
        TaskActionDefinition task => $"Create a task “{task.Title}”",
        NotificationActionDefinition notification => $"Notify you “{notification.Title}”",
        SetModeActionDefinition mode => $"Switch to {mode.Mode} mode",
        _ => action.Kind
    };

    private async Task<AutomationRunActivityResult> SkipAsync(AutomationRunWorkflowInput input,
        AutomationRuleRecord rule, string reason, CancellationToken cancellationToken)
    {
        var json = Serialize([new AutomationActionResult("run", "skipped", reason, null)]);
        await runs.SkipAsync(input.RunId, reason, json, cancellationToken);
        if (input.TestRun || input.TriggerKind == AutomationTriggerKinds.Manual)
            await PostToLinkedChatAsync(rule, LinkedConversationCopy.AutomationRun(rule.Name, "skipped",
                input.TriggerReason, json, input.TestRun), cancellationToken);
        return new AutomationRunActivityResult(true, json, false);
    }

    private async Task<AutomationRunActivityResult> FailAsync(AutomationRunWorkflowInput input, string summary,
        CancellationToken cancellationToken)
    {
        var json = Serialize([new AutomationActionResult("run", "failed", summary, null)]);
        await runs.FailAsync(input.RunId, summary, json, cancellationToken);
        return new AutomationRunActivityResult(false, json, false);
    }

    private async Task PostToLinkedChatAsync(AutomationRuleRecord rule, string content,
        CancellationToken cancellationToken)
    {
        var conversationId = rule.ConversationId;
        if (conversationId is null)
        {
            try
            {
                conversationId = await rules.EnsureConversationAsync(rule.Id, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                return;
            }
        }

        await conversations.AddMessageAsync(new Message(conversationId.Value, "assistant", content), cancellationToken);
    }

    private async Task<AutomationActionResult> ExecuteActionAsync(AutomationRunWorkflowInput input,
        AutomationActionDefinition action, AutomationEvent? ev, CancellationToken cancellationToken)
    {
        var (title, body) = AutomationSimulator.Texts(action, ev);
        switch (action)
        {
            case NotificationActionDefinition:
            {
                var record = await notifications.CreateAsync(input.OwnerId, "automation.notification",
                    Clip(title!, 200), Clip(body!, 2000), input.RuleId, cancellationToken);
                return new AutomationActionResult(action.Kind, "completed", null, record.Id);
            }
            case TaskActionDefinition:
            {
                var created = await tasks.CreateAsync(input.OwnerId, Clip(title!, 200), body!, cancellationToken);
                return new AutomationActionResult(action.Kind, "completed", null, created.Id);
            }
            case ChannelMessageActionDefinition channel:
            {
                if (input.TestRun)
                    return new AutomationActionResult(action.Kind, "skipped", "Test run does not send messages.", null);
                var messageId = await channelSender.SendPreconfiguredAsync(input.OwnerId, channel.ConnectionId,
                    channel.Recipient, body!, cancellationToken);
                return new AutomationActionResult(action.Kind, "completed", null, messageId);
            }
            case AgentRunActionDefinition:
            {
                if (input.TestRun)
                    return new AutomationActionResult(action.Kind, "skipped", "Test run does not start agent tasks.", null);
                var created = await tasks.CreateAsync(input.OwnerId, Clip(title!, 200), body!, cancellationToken);
                return new AutomationActionResult(action.Kind, "completed", null, created.Id);
            }
            case SetModeActionDefinition setMode:
            {
                if (input.TestRun)
                    return new AutomationActionResult(action.Kind, "skipped", "Test run does not change the mode.", null);
                if (modes is null)
                    return new AutomationActionResult(action.Kind, "failed", "Modes are not available here.", null);
                var changed = await modes.SetModeAsync(input.OwnerId, setMode.Mode, setMode.Minutes, cancellationToken);
                return changed.Succeeded
                    ? new AutomationActionResult(action.Kind, "completed", null, null)
                    : new AutomationActionResult(action.Kind, "failed", changed.Message, null);
            }
            default:
                return new AutomationActionResult(action.Kind, "failed", "Unknown action.", null);
        }
    }

    private static string Serialize(IReadOnlyList<AutomationActionResult> results) =>
        JsonSerializer.Serialize(results);

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];

    private static string Preview(string text, int max = 600) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string Sanitize(string message) =>
        message.Length <= 200 ? message : message[..200];
}
