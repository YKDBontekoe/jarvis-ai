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
        AutomationRuleValidator.Validate(request.Definition);
        return rules.CreateAsync(ownerId, request.Name, request.Definition, cancellationToken);
    }

    public async Task<AutomationRuleRecord?> UpdateAsync(Guid id, Guid ownerId, SaveAutomationRuleRequest request,
        CancellationToken cancellationToken)
    {
        AutomationRuleValidator.ValidateName(request.Name);
        AutomationRuleValidator.Validate(request.Definition);
        try
        {
            return await rules.UpdateDraftAsync(id, ownerId, request.Name, request.Definition, cancellationToken);
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

        await scheduler.StartRunAsync(run, cancellationToken);
        return run;
    }
}

public sealed class AutomationTriggerPublisher(
    IAutomationRuleRepository rules,
    IAutomationRunRepository runs,
    IAutomationScheduler scheduler) : IAutomationTriggerPublisher
{
    public async Task PublishReminderDueAsync(Guid ownerId, Guid reminderId, string reminderTitle,
        CancellationToken cancellationToken)
    {
        var all = await rules.ListAsync(ownerId, cancellationToken);
        foreach (var rule in all.Where(x => x.Status == AutomationRuleStatuses.Enabled))
        {
            var definition = AutomationDefinitionJson.Deserialize(rule.DefinitionJson);
            if (definition.Trigger is not ReminderDueTriggerDefinition reminderTrigger) continue;
            if (reminderTrigger.ReminderId is Guid specific && specific != reminderId) continue;

            var idempotencyKey = $"reminder_due:{reminderId:N}";
            var (run, created) = await runs.TryStartAsync(new AutomationTriggerFireInput(rule.Id, ownerId,
                AutomationTriggerKinds.ReminderDue, $"Reminder due: {reminderTitle}", idempotencyKey, false),
                cancellationToken);
            if (created) await scheduler.StartRunAsync(run, cancellationToken);
        }
    }
}
