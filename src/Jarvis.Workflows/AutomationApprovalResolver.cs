using Jarvis.Application.Automations;
using Jarvis.Application.Conversations;
using Jarvis.Domain.Automations;
using Microsoft.Extensions.Logging;

namespace Jarvis.Workflows;

/// <summary>
/// Hands a decision from the shared approval inbox (chat card, approvals screen, notification, messaging reply)
/// to the paused automation run, so automations follow the same approval path as every other sensitive action.
/// </summary>
public sealed class AutomationApprovalResolver(
    IAutomationRunRepository runs,
    IAutomationRuleRepository rules,
    IAutomationScheduler scheduler,
    ILogger<AutomationApprovalResolver> logger) : IAutomationApprovalResolver
{
    public async Task<string?> ResolveAsync(Guid ownerId, Guid approvalId, bool approved,
        CancellationToken cancellationToken)
    {
        var run = await runs.GetByApprovalIdAsync(approvalId, ownerId, cancellationToken);
        if (run is null || run.Status != AutomationRunStatuses.WaitingApproval) return null;
        var rule = await rules.GetAsync(run.RuleId, ownerId, cancellationToken);
        var name = LinkedConversationCopy.Sanitize(rule?.Name ?? "Automation");

        try
        {
            await scheduler.SignalApprovalResolvedAsync(run.WorkflowId, approved, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The run workflow is gone (expired or terminated), so nothing can act on this decision any more.
            logger.LogWarning(exception, "Automation run {RunId} no longer accepts an approval decision.", run.Id);
            await runs.ExpireAsync(run.Id, "The run stopped before the decision arrived.", CancellationToken.None);
            return $"Automation “{name}” had already stopped, so nothing was run. Start it again if you still want it.";
        }

        if (!approved)
        {
            await runs.FailAsync(run.Id, "Declined by owner.", run.ActionResultsJson, CancellationToken.None);
            return $"Declined. Automation “{name}” stopped this run and sent nothing.";
        }
        return $"Approved. Automation “{name}” is finishing this run; I’ll post the result here.";
    }
}
