namespace Jarvis.Domain.Audit;

public sealed class AuditEvent
{
    private AuditEvent() { }

    public AuditEvent(Guid ownerId, string tool, string action, string riskClass, bool success,
        Guid? approvalId = null, Guid? agentRunId = null, string? metadataJson = null)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An audit event requires an owner.", nameof(ownerId));
        if (string.IsNullOrWhiteSpace(tool) || tool.Length > 120) throw new ArgumentException("Audit tool must contain 1 to 120 characters.", nameof(tool));
        if (string.IsNullOrWhiteSpace(action) || action.Length > 160) throw new ArgumentException("Audit action must contain 1 to 160 characters.", nameof(action));
        if (string.IsNullOrWhiteSpace(riskClass) || riskClass.Length > 40) throw new ArgumentException("Audit risk class must contain 1 to 40 characters.", nameof(riskClass));
        if (metadataJson?.Length > 8_000) throw new ArgumentException("Audit metadata must be 8,000 characters or fewer.", nameof(metadataJson));

        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Tool = tool;
        Action = action;
        RiskClass = riskClass;
        ApprovalId = approvalId;
        AgentRunId = agentRunId;
        Timestamp = DateTimeOffset.UtcNow;
        Success = success;
        MetadataJson = metadataJson;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid? AgentRunId { get; private set; }
    public string Tool { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string RiskClass { get; private set; } = string.Empty;
    public Guid? ApprovalId { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public bool Success { get; private set; }
    public string? MetadataJson { get; private set; }
}
