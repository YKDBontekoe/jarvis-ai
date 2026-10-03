using System.Text.Json;
using Jarvis.Application.Automations;
using Jarvis.Domain.Automations;
using Microsoft.Extensions.Logging;

namespace Jarvis.Workflows;

public sealed class AutomationRuleService(
    IAutomationRuleRepository rules,
    IAutomationRunRepository runs,
    IAutomationScheduler scheduler,
    ILogger<AutomationRuleService> logger) : IAutomationRuleService
{
    public Task<AutomationRuleRecord> CreateAsync(Guid ownerId, SaveAutomationRuleRequest request,
        CancellationToken cancellationToken)
    {
        AutomationRuleValidator.ValidateName(request.Name);
        var definition = request.ParseDefinition();
        AutomationRuleValidator.Validate(definition);
        return rules.CreateAsync(ownerId, request.Name, definition, cancellationToken);
    }

    public async Task<AutomationRuleRecord?> UpdateAsync(Guid id, Guid ownerId, SaveAutomationRuleRequest request,
        CancellationToken cancellationToken)
    {
        AutomationRuleValidator.ValidateName(request.Name);
        var definition = request.ParseDefinition();
        AutomationRuleValidator.Validate(definition);
        try
        {
            return await rules.UpdateDraftAsync(id, ownerId, request.Name, definition, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public Task<IReadOnlyList<AutomationRuleRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        rules.ListAsync(ownerId, cancellationToken);

    public Task<AutomationRuleRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        rules.GetAsync(id, ownerId, cancellationToken);

    public Task<AutomationValidationResult> ValidateAsync(AutomationRuleDefinition definition)
    {
        try
        {
            AutomationRuleValidator.Validate(definition);
            return Task.FromResult(new AutomationValidationResult(true, []));
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(new AutomationValidationResult(false, [exception.Message]));
        }
    }

    public async Task<AutomationRuleRecord?> EnableAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var rule = await rules.EnableAsync(id, ownerId, cancellationToken);
        if (rule is null) return null;
        try
        {
            await scheduler.ScheduleRuleAsync(rule, cancellationToken);
            await rules.MarkScheduleDispatchedAsync(rule.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Automation {RuleId} remains enabled for Temporal scheduling recovery.", id);
        }
        return rule;
    }

    public async Task<AutomationRuleRecord?> DisableAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var rule = await rules.DisableAsync(id, ownerId, cancellationToken);
        if (rule is null) return null;
        try
        {
            await scheduler.CancelRuleScheduleAsync(rule.ScheduleWorkflowId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Automation {RuleId} schedule may still be running in Temporal.", id);
        }
        return rule;
    }

    public Task<AutomationRunRecord?> TestRunAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        StartRunAsync(id, ownerId, AutomationTriggerKinds.Manual, "Test run", testRun: true, cancellationToken);

    public Task<AutomationRunRecord?> ManualRunAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        StartRunAsync(id, ownerId, AutomationTriggerKinds.Manual, "Manual run", testRun: false, cancellationToken);

    private async Task<AutomationRunRecord?> StartRunAsync(Guid ruleId, Guid ownerId, string triggerKind,
        string reason, bool testRun, CancellationToken cancellationToken)
    {
        var rule = await rules.GetAsync(ruleId, ownerId, cancellationToken);
        if (rule is null) return null;
        if (!testRun && rule.Status != AutomationRuleStatuses.Enabled) return null;

        if (await rules.CountActiveRunsAsync(ownerId, cancellationToken) >= AutomationSchema.MaxConcurrentRunsPerOwner)
            throw new InvalidOperationException("Too many automations are running for this account.");

        var idempotencyKey = $"{triggerKind}:{ruleId:N}:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var (run, created) = await runs.TryStartAsync(new AutomationTriggerFireInput(ruleId, ownerId, triggerKind,
            reason, idempotencyKey, testRun), cancellationToken);
        if (!created) return run;

        try
        {
            await scheduler.StartRunAsync(run, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await runs.FailAsync(run.Id, "Jarvis could not start this run.", "[]", CancellationToken.None);
            throw;
        }
        return run;
    }
}

public sealed class AutomationTriggerPublisher(
    IAutomationRuleRepository rules,
    IAutomationRunRepository runs,
    IAutomationScheduler scheduler) : IAutomationTriggerPublisher
{
    public async Task PublishReminderDueAsync(Guid ownerId, Guid reminderId, string reminderTitle,
        DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var all = await rules.ListAsync(ownerId, cancellationToken);
        foreach (var rule in all.Where(x => x.Status == AutomationRuleStatuses.Enabled))
        {
            var definition = AutomationDefinitionJson.Deserialize(rule.DefinitionJson);
            if (definition.Trigger is not ReminderDueTriggerDefinition reminderTrigger) continue;
            if (reminderTrigger.ReminderId is Guid specific && specific != reminderId) continue;

            var idempotencyKey = $"reminder_due:{reminderId:N}:{dueAt.ToUniversalTime():yyyyMMddHHmm}";
            var (run, created) = await runs.TryStartAsync(new AutomationTriggerFireInput(rule.Id, ownerId,
                AutomationTriggerKinds.ReminderDue, $"Reminder due: {reminderTitle}", idempotencyKey, false),
                cancellationToken);
            if (created) await scheduler.StartRunAsync(run, cancellationToken);
        }
    }
}

/// <summary>
/// Starts every enabled automation whose event trigger matches. One run per rule and event: the event's
/// fingerprint is the idempotency key, so a redelivered webhook or a repeated sync starts nothing twice. Each
/// automation's own cooldown keeps a rule that triggers itself (a task it creates finishing) from looping.
/// </summary>
public sealed class AutomationEventBus(
    IAutomationRuleRepository rules,
    IAutomationRunRepository runs,
    IAutomationScheduler scheduler,
    ILogger<AutomationEventBus> logger) : IAutomationEventBus
{
    public async Task<int> PublishAsync(Guid ownerId, AutomationEvent ev, CancellationToken cancellationToken)
    {
        ev = ev.Normalize();
        var started = 0;
        foreach (var rule in (await rules.ListAsync(ownerId, cancellationToken))
                     .Where(x => x.Status == AutomationRuleStatuses.Enabled))
        {
            try
            {
                if (AutomationDefinitionJson.Deserialize(rule.DefinitionJson).Trigger is not EventTriggerDefinition trigger ||
                    !AutomationEventMatcher.Matches(trigger, ev))
                    continue;
                if (await rules.CountActiveRunsAsync(ownerId, cancellationToken) >=
                    AutomationSchema.MaxConcurrentRunsPerOwner)
                {
                    logger.LogInformation("Event {EventKind} skipped: too many automation runs are active.", ev.Kind);
                    break;
                }

                var reason = $"{AutomationEventKinds.Describe(ev.Kind)}: {ev.Title}";
                var (run, created) = await runs.TryStartAsync(new AutomationTriggerFireInput(rule.Id, ownerId,
                    AutomationTriggerKinds.Event, reason.Length <= 480 ? reason : reason[..480],
                    ev.Fingerprint(), false, ev.ToJson()), cancellationToken);
                if (!created) continue;
                try
                {
                    await scheduler.StartRunAsync(run, cancellationToken);
                    started++;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await runs.FailAsync(run.Id, "Jarvis could not start this run.", "[]", CancellationToken.None);
                    logger.LogWarning(exception, "Automation {RuleId} could not be started for an event.", rule.Id);
                }
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                logger.LogWarning(exception, "Automation {RuleId} has an unreadable definition.", rule.Id);
            }
        }
        return started;
    }
}
