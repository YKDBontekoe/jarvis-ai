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
    Task UpdateScheduleStateAsync(Guid id, DateTimeOffset? nextRunAt, int? cooldownMinutes,
        CancellationToken cancellationToken);
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
    Task MarkWaitingApprovalAsync(Guid runId, Guid approvalId, CancellationToken cancellationToken);
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
    Task PublishReminderDueAsync(Guid ownerId, Guid reminderId, string reminderTitle,
        CancellationToken cancellationToken);
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
