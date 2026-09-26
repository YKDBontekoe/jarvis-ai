namespace Jarvis.Application.Audit;

public sealed record AuditEventRecord(Guid Id, Guid? AgentRunId, string Tool, string Action,
    string RiskClass, Guid? ApprovalId, DateTimeOffset Timestamp, bool Success, string? MetadataJson);

public interface IAuditEventStore
{
    Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass,
        bool success, Guid? approvalId, string? metadataJson, CancellationToken cancellationToken,
        Guid? agentRunId = null);
    Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit, CancellationToken cancellationToken);
}

public static class AuditEventMapping
{
    public static AuditEventRecord ToRecord(this Jarvis.Domain.Audit.AuditEvent item) =>
        new(item.Id, item.AgentRunId, item.Tool, item.Action, item.RiskClass, item.ApprovalId,
            item.Timestamp, item.Success, item.MetadataJson);
}
