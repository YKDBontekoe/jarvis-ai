using Jarvis.Application.Approvals;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Approvals;
using Jarvis.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ToolApprovalStore(JarvisDbContext db) : IToolApprovalStore
{
    private static readonly TimeSpan ResumeRecoveryAge = TimeSpan.FromMinutes(15);

    public async Task<ToolApprovalCreateResult> CreateAsync(Guid ownerId, Guid conversationId, string requestId,
        string toolCallId, string toolName, string argumentsJson, Guid? taskId, CancellationToken cancellationToken)
    {
        var existing = await FindIdempotentAsync(ownerId, requestId, toolCallId, cancellationToken);
        if (existing is not null)
            return new ToolApprovalCreateResult(
                EnsureSameRequest(existing, conversationId, taskId, toolName, argumentsJson).ToRecord(), false, null);

        var approval = new ToolApproval(ownerId, conversationId, requestId, toolCallId, toolName, argumentsJson, taskId);
        var auditEvent = new AuditEvent(ownerId, toolName, "approval.requested", "high", true,
            approval.Id, metadataJson: JsonSerializer.Serialize(new { approvalId = approval.Id, conversationId }));
        var notification = new Notification(Guid.CreateVersion7(), ownerId, "approval.required",
            "Approval needed", $"Jarvis is waiting for approval to run {toolName}.", approval.Id);
        db.ToolApprovals.Add(approval);
        db.AuditEvents.Add(auditEvent);
        db.Notifications.Add(notification);
        var pushDeliveries = await PushDeliveryQueue.QueueAsync(db, notification, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            db.Entry(approval).State = EntityState.Detached;
            db.Entry(auditEvent).State = EntityState.Detached;
            db.Entry(notification).State = EntityState.Detached;
            foreach (var delivery in pushDeliveries)
                db.Entry(delivery).State = EntityState.Detached;
            existing = await FindIdempotentAsync(ownerId, requestId, toolCallId, cancellationToken);
            if (existing is null) throw;
            return new ToolApprovalCreateResult(
                EnsureSameRequest(existing, conversationId, taskId, toolName, argumentsJson).ToRecord(), false, null);
        }
        return new ToolApprovalCreateResult(approval.ToRecord(), true, notification.Id);
    }

    private Task<ToolApproval?> FindIdempotentAsync(Guid ownerId, string requestId, string toolCallId,
        CancellationToken cancellationToken) =>
        db.ToolApprovals.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId &&
            x.RequestId == requestId && x.ToolCallId == toolCallId, cancellationToken);

    private static ToolApproval EnsureSameRequest(ToolApproval existing, Guid conversationId, Guid? taskId,
        string toolName, string argumentsJson)
    {
        if (existing.ConversationId != conversationId ||
            existing.TaskId != taskId ||
            !string.Equals(existing.ToolName, toolName, StringComparison.Ordinal) ||
            !JsonNode.DeepEquals(JsonNode.Parse(existing.ArgumentsJson), JsonNode.Parse(argumentsJson)))
            throw new InvalidOperationException("A tool approval idempotency key was reused with different request data.");
        return existing;
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.GetBaseException() is PostgresException
        {
            SqlState: "23505",
            ConstraintName: "ux_tool_approvals_idempotency"
        };

    public async Task<ToolApprovalRecord?> GetActionableAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var staleBefore = DateTimeOffset.UtcNow - ResumeRecoveryAge;
        return (await db.ToolApprovals.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == id && x.OwnerId == ownerId && (x.Status == "pending" ||
                ((x.Status == "approved" || x.Status == "rejected") &&
                 (x.ResumeStatus == "pending" || x.ResumeStatus == "failed" ||
                  (x.ResumeStatus == "running" && x.ResumeStartedAt < staleBefore)))) &&
            (x.TaskId == null || db.Tasks.Any(task => task.Id == x.TaskId && task.Status == "needs_approval")),
            cancellationToken))?.ToRecord();
    }

    public Task<IReadOnlyList<ToolApprovalRecord>> ListActionableAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        MaterializeActionableAsync(
            OrderedActionable(ActionableQuery(ownerId)).Take(100), cancellationToken);

    public Task<IReadOnlyList<ToolApprovalRecord>> ListActionableForConversationAsync(Guid ownerId,
        Guid conversationId, CancellationToken cancellationToken) =>
        MaterializeActionableAsync(
            OrderedActionable(ActionableQuery(ownerId).Where(x => x.ConversationId == conversationId)),
            cancellationToken);

    private IQueryable<ToolApproval> ActionableQuery(Guid ownerId)
    {
        var staleBefore = DateTimeOffset.UtcNow - ResumeRecoveryAge;
        return db.ToolApprovals.AsNoTracking().Where(x => x.OwnerId == ownerId &&
            (x.Status == "pending" ||
             ((x.Status == "approved" || x.Status == "rejected") &&
              (x.ResumeStatus == "pending" || x.ResumeStatus == "failed" ||
               (x.ResumeStatus == "running" && x.ResumeStartedAt < staleBefore)))) &&
            (x.TaskId == null || db.Tasks.Any(task => task.Id == x.TaskId && task.Status == "needs_approval")));
    }

    private static IQueryable<ToolApproval> OrderedActionable(IQueryable<ToolApproval> query) =>
        query.OrderBy(x => x.Status == "pending" ? 0 : 1).ThenBy(x => x.CreatedAt);

    private static async Task<IReadOnlyList<ToolApprovalRecord>> MaterializeActionableAsync(
        IQueryable<ToolApproval> query, CancellationToken cancellationToken) =>
        (await query.ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToList();

    public Task<bool> HasPendingForTaskAsync(Guid taskId, Guid ownerId, CancellationToken cancellationToken) =>
        db.ToolApprovals.AsNoTracking().AnyAsync(x => x.TaskId == taskId && x.OwnerId == ownerId &&
            x.Status == "pending", cancellationToken);

    public async Task<ToolApprovalRecord?> DecideAsync(Guid id, Guid ownerId, bool approved, CancellationToken cancellationToken)
    {
        var status = approved ? "approved" : "rejected";
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.ToolApprovals.Where(x => x.Id == id && x.OwnerId == ownerId && x.Status == "pending")
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.ResumeStatus, "pending")
                .SetProperty(x => x.Approved, (bool?)approved)
                .SetProperty(x => x.DecidedAt, DateTimeOffset.UtcNow), cancellationToken);
        if (changed == 0) return null;
        var approval = await db.ToolApprovals.AsNoTracking().SingleAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        db.AuditEvents.Add(new AuditEvent(ownerId, approval.ToolName,
            approved ? "approval.approved" : "approval.rejected", "high", true, approval.Id,
            metadataJson: JsonSerializer.Serialize(new { approvalId = approval.Id, conversationId = approval.ConversationId })));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return approval.ToRecord();
    }

    public async Task<bool> TryStartResumeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var staleBefore = now - ResumeRecoveryAge;
        var changed = await db.ToolApprovals.Where(x => x.Id == id && x.OwnerId == ownerId &&
            (x.Status == "approved" || x.Status == "rejected") &&
            (x.ResumeStatus == "pending" || x.ResumeStatus == "failed" ||
             (x.ResumeStatus == "running" && x.ResumeStartedAt < staleBefore)))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ResumeStatus, "running")
                .SetProperty(x => x.ResumeStartedAt, now), cancellationToken);
        return changed == 1;
    }

    public async Task HeartbeatResumeAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await db.ToolApprovals.Where(x => x.Id == id && x.OwnerId == ownerId &&
                (x.Status == "approved" || x.Status == "rejected") && x.ResumeStatus == "running")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ResumeStartedAt, DateTimeOffset.UtcNow),
                cancellationToken);
    }

    public async Task CancelIncompleteForTaskAsync(Guid taskId, Guid ownerId, CancellationToken cancellationToken)
    {
        var approvals = await db.ToolApprovals.Where(x =>
                x.TaskId == taskId && x.OwnerId == ownerId &&
                (x.Status == "pending" ||
                 ((x.Status == "approved" || x.Status == "rejected") &&
                  x.ResumeStatus != "completed" && x.ResumeStatus != "cancelled")))
            .ToListAsync(cancellationToken);
        if (approvals.Count == 0) return;
        var approvalIds = approvals.Select(x => x.Id).ToArray();
        foreach (var approval in approvals)
        {
            await db.Entry(approval).ReloadAsync(cancellationToken);
            approval.Cancel();
            approval.AbortResume();
        }
        await ApprovalInboxCleanup.RemoveAsync(db, approvalIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task MarkResumeCompletedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        SetResumeStatusAsync(id, ownerId, "completed", "approval.resume_completed", true, cancellationToken);

    public Task MarkResumeFailedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        SetResumeStatusAsync(id, ownerId, "failed", "approval.resume_failed", false, cancellationToken);

    private async Task SetResumeStatusAsync(Guid id, Guid ownerId, string status, string auditAction,
        bool succeeded, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.ToolApprovals.Where(x => x.Id == id && x.OwnerId == ownerId &&
                (x.Status == "approved" || x.Status == "rejected") && x.ResumeStatus == "running")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ResumeStatus, status), cancellationToken);
        if (changed == 0) return;

        var approval = await db.ToolApprovals.AsNoTracking()
            .SingleAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        db.AuditEvents.Add(new AuditEvent(ownerId, approval.ToolName, auditAction, "high", succeeded,
            approval.Id, metadataJson: JsonSerializer.Serialize(new
            {
                approvalId = approval.Id,
                conversationId = approval.ConversationId,
                taskId = approval.TaskId
            })));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
