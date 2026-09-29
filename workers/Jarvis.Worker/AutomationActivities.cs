using Jarvis.Application.Automations;
using Jarvis.Domain.Automations;
using Jarvis.Workflows;
using Temporalio.Activities;

namespace Jarvis.Worker;

internal sealed class AutomationRunActivities(IServiceScopeFactory scopeFactory) : AutomationRunActivityContract
{
    [Activity("ExecuteAutomationRun")]
    public override async Task<AutomationRunActivityResult> ExecuteAsync(AutomationRunWorkflowInput input)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAutomationRunExecutor>()
            .ExecuteRunAsync(input, ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity("CompleteAutomationRunAfterApproval")]
    public override async Task<AutomationRunActivityResult> CompleteAfterApprovalAsync(
        AutomationRunWorkflowInput input)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAutomationRunExecutor>()
            .ExecuteRunAsync(input with { AfterApproval = true }, ActivityExecutionContext.Current.CancellationToken);
    }
}

internal sealed class AutomationScheduleActivities(IServiceScopeFactory scopeFactory) :
    AutomationScheduleActivityContract
{
    [Activity("ResolveAutomationSchedule")]
    public override async Task<DateTimeOffset?> ResolveNextFireAsync(AutomationScheduleWorkflowInput input)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var rules = scope.ServiceProvider.GetRequiredService<IAutomationRuleRepository>();
        var rule = await rules.GetForExecutionAsync(input.RuleId, ActivityExecutionContext.Current.CancellationToken);
        if (rule is null || rule.Status != AutomationRuleStatuses.Enabled) return null;
        var definition = AutomationDefinitionJson.Deserialize(rule.DefinitionJson);
        if (definition.Trigger is not ScheduleTriggerDefinition schedule) return null;
        var next = AutomationScheduleClock.GetNextScheduleFireUtc(DateTimeOffset.UtcNow, schedule);
        if (next is not null)
            await rules.UpdateScheduleStateAsync(rule.Id, next, null, ActivityExecutionContext.Current.CancellationToken);
        return next;
    }

    [Activity("FireAutomationSchedule")]
    public override async Task FireScheduleAsync(AutomationTriggerFireInput input) =>
        await AutomationFireHelper.FireAsync(scopeFactory, input);
}

internal sealed class AutomationPollActivities(IServiceScopeFactory scopeFactory) : AutomationPollActivityContract
{
    [Activity("CheckAutomationPollTrigger")]
    public override async Task<bool> CheckAsync(AutomationPollWorkflowInput input)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var rules = scope.ServiceProvider.GetRequiredService<IAutomationRuleRepository>();
        var rule = await rules.GetForExecutionAsync(input.RuleId, ActivityExecutionContext.Current.CancellationToken);
        if (rule is null || rule.Status != AutomationRuleStatuses.Enabled) return false;
        if (rule.CooldownUntil is not null && rule.CooldownUntil > DateTimeOffset.UtcNow) return false;
        var definition = AutomationDefinitionJson.Deserialize(rule.DefinitionJson);
        var evaluator = scope.ServiceProvider.GetRequiredService<AutomationConditionEvaluator>();
        if (!await evaluator.EvaluateAllAsync(rule.OwnerId, definition.Conditions,
                ActivityExecutionContext.Current.CancellationToken))
            return false;
        return await evaluator.EvaluatePollingTriggerAsync(rule.OwnerId, definition.Trigger,
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity("FireAutomationPoll")]
    public override async Task FirePollAsync(AutomationTriggerFireInput input) =>
        await AutomationFireHelper.FireAsync(scopeFactory, input);
}

internal static class AutomationFireHelper
{
    public static async Task FireAsync(IServiceScopeFactory scopeFactory, AutomationTriggerFireInput input)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var runs = scope.ServiceProvider.GetRequiredService<IAutomationRunRepository>();
        var scheduler = scope.ServiceProvider.GetRequiredService<IAutomationScheduler>();
        var rules = scope.ServiceProvider.GetRequiredService<IAutomationRuleRepository>();
        if (await rules.CountActiveRunsAsync(input.OwnerId, ActivityExecutionContext.Current.CancellationToken) >=
            AutomationSchema.MaxConcurrentRunsPerOwner)
            return;

        var (run, created) = await runs.TryStartAsync(input, ActivityExecutionContext.Current.CancellationToken);
        if (created) await scheduler.StartRunAsync(run, ActivityExecutionContext.Current.CancellationToken);
    }
}
