using Jarvis.Application.Audit;
using Jarvis.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class AuditEventStore(JarvisDbContext db) : IAuditEventStore
{
    public async Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action,
        string riskClass, bool success, Guid? approvalId, string? metadataJson,
        CancellationToken cancellationToken, Guid? agentRunId = null)
    {
        var auditEvent = new AuditEvent(ownerId, tool, action, riskClass, success, approvalId,
            agentRunId, metadataJson);
        db.AuditEvents.Add(auditEvent);
        await db.SaveChangesAsync(cancellationToken);
        return auditEvent.ToRecord();
    }

    public async Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
        CancellationToken cancellationToken) =>
        (await db.AuditEvents.AsNoTracking().Where(item => item.OwnerId == ownerId)
            .OrderByDescending(item => item.Timestamp).Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken)).Select(item => item.ToRecord()).ToArray();
}
