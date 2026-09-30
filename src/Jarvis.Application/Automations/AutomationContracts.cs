using System.Text.Json;
using Jarvis.Domain.Automations;

namespace Jarvis.Application.Automations;

public sealed record AutomationRuleRecord(
    Guid Id,
    Guid OwnerId,
    string Name,
    int SchemaVersion,
    string DefinitionJson,
    string Status,
    string ScheduleWorkflowId,
    DateTimeOffset? ScheduleDispatchedAt,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? CooldownUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ConversationId = null);

public sealed record AutomationRunRecord(
    Guid Id,
    Guid RuleId,
    Guid OwnerId,
    string WorkflowId,
    string IdempotencyKey,
    string TriggerKind,
    string TriggerReason,
    bool TestRun,
    string Status,
    string ActionResultsJson,
    string? FailureSummary,
    Guid? ApprovalId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record SaveAutomationRuleRequest(string Name, JsonElement Definition)
{
    public AutomationRuleDefinition ParseDefinition() => AutomationDefinitionJson.Deserialize(Definition.GetRawText());
}

public sealed record AutomationValidationResult(bool Valid, IReadOnlyList<string> Errors);

public sealed record AutomationRunRequest(string? IdempotencyKey, string TriggerKind, string TriggerReason,
    bool TestRun);

public sealed record AutomationRunWorkflowInput(Guid RuleId, Guid OwnerId, Guid RunId, string IdempotencyKey,
    string TriggerKind, string TriggerReason, bool TestRun, bool AfterApproval = false);

public sealed record AutomationScheduleWorkflowInput(Guid RuleId, Guid OwnerId, string ScheduleWorkflowId);

public sealed record AutomationPollWorkflowInput(Guid RuleId, Guid OwnerId, string ScheduleWorkflowId,
    int IntervalMinutes);

public sealed record AutomationActionResult(string Kind, string Status, string? Detail, Guid? ResourceId);

public sealed record AutomationRunActivityResult(bool Continue, string ActionResultsJson, bool WaitingApproval);

public sealed record AutomationRunCloseInput(Guid RunId, Guid OwnerId, string Reason);

public sealed record AutomationTriggerFireInput(Guid RuleId, Guid OwnerId, string TriggerKind, string TriggerReason,
    string IdempotencyKey, bool TestRun);

public interface IAutomationRuleRepository
{
    Task<AutomationRuleRecord> CreateAsync(Guid ownerId, string name, AutomationRuleDefinition definition,
        CancellationToken cancellationToken);
    Task<AutomationRuleRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<AutomationRuleRecord?> GetByConversationIdAsync(Guid conversationId, Guid ownerId,
        CancellationToken cancellationToken);
    Task<AutomationRuleRecord?> GetForExecutionAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AutomationRuleRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<Guid> EnsureConversationAsync(Guid id, CancellationToken cancellationToken);
    Task<AutomationRuleRecord> UpdateDraftAsync(Guid id, Guid ownerId, string name,
        AutomationRuleDefinition definition, CancellationToken cancellationToken);
    Task<AutomationRuleRecord?> EnableAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<AutomationRuleRecord?> DisableAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AutomationRuleRecord>> ListPendingScheduleDispatchAsync(CancellationToken cancellationToken);
    Task<int> RequeueStaleSchedulesAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);
    Task MarkScheduleDispatchedAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Records that a run happened: sets the last run time, the next fire and an optional cooldown.</summary>
    Task UpdateScheduleStateAsync(Guid id, DateTimeOffset? nextRunAt, int? cooldownMinutes,
        CancellationToken cancellationToken);
    /// <summary>Stores the next planned fire without claiming that a run happened.</summary>
    Task UpdateNextRunAsync(Guid id, DateTimeOffset? nextRunAt, CancellationToken cancellationToken);
    Task<int> CountActiveRunsAsync(Guid ownerId, CancellationToken cancellationToken);
}

public interface IAutomationRunRepository
{
    Task<(AutomationRunRecord Run, bool Created)> TryStartAsync(AutomationTriggerFireInput input,
        CancellationToken cancellationToken);
    Task<AutomationRunRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AutomationRunRecord>> ListForRuleAsync(Guid ruleId, Guid ownerId, int limit,
        CancellationToken cancellationToken);
    Task CompleteAsync(Guid runId, string actionResultsJson, CancellationToken cancellationToken);
    Task FailAsync(Guid runId, string? summary, string actionResultsJson, CancellationToken cancellationToken);
    Task SkipAsync(Guid runId, string reason, string actionResultsJson, CancellationToken cancellationToken);
    Task MarkWaitingApprovalAsync(Guid runId, Guid approvalId, CancellationToken cancellationToken);
    Task<AutomationRunRecord?> GetByApprovalIdAsync(Guid approvalId, Guid ownerId, CancellationToken cancellationToken);
    /// <summary>The newest run of each of the owner's rules, keyed by rule id.</summary>
    Task<IReadOnlyDictionary<Guid, AutomationRunRecord>> LatestPerRuleAsync(Guid ownerId,
        CancellationToken cancellationToken);
    /// <summary>
    /// Closes runs whose workflow can no longer finish them: running past every activity timeout, or waiting on an
    /// approval longer than the workflow waits. Without this they count against the owner's concurrent-run limit
    /// forever and block every other automation.
    /// </summary>
    Task<int> ExpireAbandonedAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);
    /// <summary>Fails one active run and withdraws its open approval.</summary>
    Task ExpireAsync(Guid runId, string reason, CancellationToken cancellationToken);
    Task<bool> HasRecentRunAsync(Guid ruleId, string idempotencyKey, CancellationToken cancellationToken);
}

public interface IAutomationRuleService
{
    Task<AutomationRuleRecord> CreateAsync(Guid ownerId, SaveAutomationRuleRequest request,
        CancellationToken cancellationToken);
    Task<AutomationRuleRecord?> UpdateAsync(Guid id, Guid ownerId, SaveAutomationRuleRequest request,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AutomationRuleRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<AutomationRuleRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<AutomationValidationResult> ValidateAsync(AutomationRuleDefinition definition);
    Task<AutomationRuleRecord?> EnableAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<AutomationRuleRecord?> DisableAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<AutomationRunRecord?> TestRunAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
    Task<AutomationRunRecord?> ManualRunAsync(Guid id, Guid ownerId, CancellationToken cancellationToken);
}

public interface IAutomationScheduler
{
    Task ScheduleRuleAsync(AutomationRuleRecord rule, CancellationToken cancellationToken);
    Task CancelRuleScheduleAsync(string scheduleWorkflowId, CancellationToken cancellationToken);
    Task StartRunAsync(AutomationRunRecord run, CancellationToken cancellationToken);
    Task SignalApprovalResolvedAsync(string runWorkflowId, bool approved, CancellationToken cancellationToken);
}

public interface IAutomationTriggerPublisher
{
    /// <param name="dueAt">The occurrence that fired, so each firing of a repeating reminder starts its own run.</param>
    Task PublishReminderDueAsync(Guid ownerId, Guid reminderId, string reminderTitle, DateTimeOffset dueAt,
        CancellationToken cancellationToken);
}

/// <summary>
/// Applies an owner's decision from the shared approval inbox to the automation run that asked for it.
/// </summary>
public interface IAutomationApprovalResolver
{
    /// <returns>The chat line describing the outcome, or null when the run no longer waits on this approval.</returns>
    Task<string?> ResolveAsync(Guid ownerId, Guid approvalId, bool approved, CancellationToken cancellationToken);
}

public static class AutomationApprovals
{
    public const string RequestPrefix = "automation-run:";

    public static string RequestId(Guid runId) => RequestPrefix + runId.ToString("N");

    public static bool IsAutomationApproval(string requestId) =>
        requestId.StartsWith(RequestPrefix, StringComparison.Ordinal);

    public static string ToolName(AutomationActionDefinition action) => "automation_" + action.Kind;

    /// <summary>How long a run waits for a decision before it gives up. Mirrors the run workflow timer.</summary>
    public static readonly TimeSpan DecisionWindow = TimeSpan.FromDays(7);
}

public interface IAutomationRunExecutor
{
    Task<AutomationRunActivityResult> ExecuteRunAsync(AutomationRunWorkflowInput input,
        CancellationToken cancellationToken);
}

public interface IAutomationChannelSender
{
    Task<Guid> SendPreconfiguredAsync(Guid ownerId, Guid connectionId, string recipient, string body,
        CancellationToken cancellationToken);
}

public static class AutomationActionPolicy
{
    public static bool RequiresApproval(AutomationActionDefinition action) => action switch
    {
        ChannelMessageActionDefinition => true,
        AgentRunActionDefinition => true,
        TaskActionDefinition => false,
        NotificationActionDefinition => false,
        _ => true
    };
}

public static class AutomationRecordMapping
{
    public static AutomationRuleRecord ToRecord(this AutomationRule rule) => new(
        rule.Id, rule.OwnerId, rule.Name, rule.SchemaVersion, rule.DefinitionJson, rule.Status,
        rule.ScheduleWorkflowId, rule.ScheduleDispatchedAt, rule.LastRunAt, rule.NextRunAt, rule.CooldownUntil,
        rule.CreatedAt, rule.UpdatedAt, rule.ConversationId);

    public static AutomationRunRecord ToRecord(this AutomationRun run) => new(
        run.Id, run.RuleId, run.OwnerId, run.WorkflowId, run.IdempotencyKey, run.TriggerKind, run.TriggerReason,
        run.TestRun, run.Status, run.ActionResultsJson, run.FailureSummary, run.ApprovalId, run.StartedAt,
        run.CompletedAt);
}
