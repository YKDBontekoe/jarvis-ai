using Jarvis.Application.Automations;
using Jarvis.Domain.Automations;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class AutomationScheduleActivityContract
{
    [Activity("ResolveAutomationSchedule")]
    public abstract Task<DateTimeOffset?> ResolveNextFireAsync(AutomationScheduleWorkflowInput input);

    [Activity("FireAutomationSchedule")]
    public abstract Task FireScheduleAsync(AutomationTriggerFireInput input);
}

[Workflow]
public sealed class AutomationScheduleWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(AutomationScheduleWorkflowInput input)
    {
        var options = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(2),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(5),
                MaximumInterval = TimeSpan.FromMinutes(2),
                MaximumAttempts = 5
            }
        };

        while (true)
        {
            var next = await Workflow.ExecuteActivityAsync(
                (AutomationScheduleActivityContract activities) => activities.ResolveNextFireAsync(input), options);
            if (next is null) return;

            var now = new DateTimeOffset(DateTime.SpecifyKind(Workflow.UtcNow, DateTimeKind.Utc));
            var delay = next.Value - now;
            if (delay > TimeSpan.Zero) await Workflow.DelayAsync(delay);

            var idempotencyKey = $"schedule:{input.RuleId:N}:{next.Value:yyyyMMddHHmm}";
            await Workflow.ExecuteActivityAsync(
                (AutomationScheduleActivityContract activities) => activities.FireScheduleAsync(
                    new AutomationTriggerFireInput(input.RuleId, input.OwnerId, AutomationTriggerKinds.Schedule,
                        "Scheduled run", idempotencyKey, false)),
                options);

            if (Workflow.ContinueAsNewSuggested)
                throw Workflow.CreateContinueAsNewException(
                    (AutomationScheduleWorkflow workflow) => workflow.RunAsync(input));
        }
    }
}

public abstract class AutomationPollActivityContract
{
    [Activity("CheckAutomationPollTrigger")]
    public abstract Task<bool> CheckAsync(AutomationPollWorkflowInput input);

    [Activity("FireAutomationPoll")]
    public abstract Task FirePollAsync(AutomationTriggerFireInput input);
}

[Workflow]
public sealed class AutomationPollWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(AutomationPollWorkflowInput input)
    {
        var options = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromSeconds(60),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(5),
                MaximumInterval = TimeSpan.FromMinutes(2),
                MaximumAttempts = 5
            }
        };

        while (true)
        {
            var matched = await Workflow.ExecuteActivityAsync(
                (AutomationPollActivityContract activities) => activities.CheckAsync(input), options);
            if (matched)
            {
                var idempotencyKey = $"poll:{input.RuleId:N}:{Workflow.UtcNow:yyyyMMddHHmm}";
                await Workflow.ExecuteActivityAsync(
                    (AutomationPollActivityContract activities) => activities.FirePollAsync(
                        new AutomationTriggerFireInput(input.RuleId, input.OwnerId,
                            "poll", "Polling trigger matched", idempotencyKey, false)),
                    options);
            }

            await Workflow.DelayAsync(TimeSpan.FromMinutes(input.IntervalMinutes));
            if (Workflow.ContinueAsNewSuggested)
                throw Workflow.CreateContinueAsNewException(
                    (AutomationPollWorkflow workflow) => workflow.RunAsync(input));
        }
    }
}
