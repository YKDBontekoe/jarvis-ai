namespace Jarvis.Domain.Automations;

public sealed class AutomationRun
{
    private AutomationRun() { }

    public AutomationRun(Guid ruleId, Guid ownerId, string workflowId, string idempotencyKey,
        string triggerKind, string triggerReason, bool testRun = false, string? eventJson = null)
    {
        Id = Guid.CreateVersion7();
        RuleId = ruleId;
        OwnerId = ownerId;
        WorkflowId = workflowId;
        IdempotencyKey = idempotencyKey;
        TriggerKind = triggerKind;
        TriggerReason = triggerReason;
        TestRun = testRun;
        EventJson = eventJson;
        Status = AutomationRunStatuses.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid RuleId { get; private set; }
    public Guid OwnerId { get; private set; }
    public string WorkflowId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string TriggerKind { get; private set; } = string.Empty;
    public string TriggerReason { get; private set; } = string.Empty;
    public bool TestRun { get; private set; }

    /// <summary>The event that fired an event-triggered run, as JSON; null for other triggers.</summary>
    public string? EventJson { get; private set; }
    public string Status { get; private set; } = AutomationRunStatuses.Running;
    public string ActionResultsJson { get; private set; } = "[]";
    public string? FailureSummary { get; private set; }
    public Guid? ApprovalId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void MarkWaitingApproval(Guid approvalId)
    {
        Status = AutomationRunStatuses.WaitingApproval;
        ApprovalId = approvalId;
    }

    public void Complete(string actionResultsJson)
    {
        Status = AutomationRunStatuses.Completed;
        ActionResultsJson = actionResultsJson;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string? summary, string actionResultsJson)
    {
        Status = AutomationRunStatuses.Failed;
        FailureSummary = summary;
        ActionResultsJson = actionResultsJson;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The run started but had nothing to do (conditions not met, cooldown, rule switched off).</summary>
    public void Skip(string reason, string actionResultsJson)
    {
        Status = AutomationRunStatuses.Skipped;
        FailureSummary = reason;
        ActionResultsJson = actionResultsJson;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public bool IsActive => Status is AutomationRunStatuses.Running or AutomationRunStatuses.WaitingApproval;

    public void Cancel()
    {
        Status = AutomationRunStatuses.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}

public static class AutomationRunStatuses
{
    public const string Running = "running";
    public const string WaitingApproval = "waiting_approval";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Skipped = "skipped";
}
