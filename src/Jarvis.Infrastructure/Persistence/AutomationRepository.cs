using System.Text.Json;
using Jarvis.Application.Automations;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Automations;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class AutomationRuleRepository(JarvisDbContext db) : IAutomationRuleRepository
{
    public async Task<AutomationRuleRecord> CreateAsync(Guid ownerId, string name, AutomationRuleDefinition definition,
        CancellationToken cancellationToken)
    {
        var json = AutomationDefinitionJson.Serialize(definition);
        var rule = new AutomationRule(ownerId, name.Trim(), json, definition.SchemaVersion);
        db.AutomationRules.Add(rule);
        db.AuditEvents.Add(new AuditEvent(ownerId, "automations", "rule.created", "low", true,
            metadataJson: JsonSerializer.Serialize(new { resourceId = rule.Id, trigger = definition.Trigger.Kind })));
        await db.SaveChangesAsync(cancellationToken);
        return rule.ToRecord();
    }

    public async Task<AutomationRuleRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.AutomationRules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken))?.ToRecord();

    public async Task<AutomationRuleRecord?> GetForExecutionAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.AutomationRules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<AutomationRuleRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.AutomationRules.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.UpdatedAt).Take(200).ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToList();

    public async Task<AutomationRuleRecord> UpdateDraftAsync(Guid id, Guid ownerId, string name,
        AutomationRuleDefinition definition, CancellationToken cancellationToken)
    {
        var rule = await db.AutomationRules.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken) ?? throw new KeyNotFoundException();
        rule.UpdateDraft(name.Trim(), AutomationDefinitionJson.Serialize(definition), definition.SchemaVersion);
        await db.SaveChangesAsync(cancellationToken);
        return rule.ToRecord();
    }

    public async Task<AutomationRuleRecord?> EnableAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var rule = await db.AutomationRules.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (rule is null) return null;
        rule.Enable();
        db.AuditEvents.Add(new AuditEvent(ownerId, "automations", "rule.enabled", "low", true,
            metadataJson: JsonSerializer.Serialize(new { resourceId = rule.Id })));
        await db.SaveChangesAsync(cancellationToken);
        return rule.ToRecord();
    }

    public async Task<AutomationRuleRecord?> DisableAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var rule = await db.AutomationRules.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (rule is null) return null;
        rule.Disable();
        db.AuditEvents.Add(new AuditEvent(ownerId, "automations", "rule.disabled", "low", true,
            metadataJson: JsonSerializer.Serialize(new { resourceId = rule.Id })));
        await db.SaveChangesAsync(cancellationToken);
        return rule.ToRecord();
    }

    public async Task<IReadOnlyList<AutomationRuleRecord>> ListPendingScheduleDispatchAsync(
        CancellationToken cancellationToken) =>
        (await db.AutomationRules.AsNoTracking()
            .Where(x => x.Status == AutomationRuleStatuses.Enabled && x.ScheduleDispatchedAt == null)
            .OrderBy(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToList();

    public async Task<int> RequeueStaleSchedulesAsync(DateTimeOffset utcNow, CancellationToken cancellationToken) =>
        await db.AutomationRules.Where(x => x.Status == AutomationRuleStatuses.Enabled &&
                x.ScheduleDispatchedAt != null &&
                x.ScheduleDispatchedAt.Value.AddMinutes(AutomationRule.ScheduleStaleGraceMinutes) < utcNow)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.ScheduleDispatchedAt, (DateTimeOffset?)null),
                cancellationToken);

    public async Task MarkScheduleDispatchedAsync(Guid id, CancellationToken cancellationToken)
    {
        var rule = await db.AutomationRules.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (rule is null || rule.Status != AutomationRuleStatuses.Enabled) return;
        rule.MarkScheduleDispatched();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateScheduleStateAsync(Guid id, DateTimeOffset? nextRunAt, int? cooldownMinutes,
        CancellationToken cancellationToken)
    {
        var rule = await db.AutomationRules.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (rule is null) return;
        rule.RecordRunScheduled(nextRunAt);
        if (cooldownMinutes is > 0) rule.ApplyCooldown(cooldownMinutes.Value);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountActiveRunsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        db.AutomationRuns.CountAsync(x => x.OwnerId == ownerId &&
            (x.Status == AutomationRunStatuses.Running || x.Status == AutomationRunStatuses.WaitingApproval),
            cancellationToken);
}

public sealed class AutomationRunRepository(JarvisDbContext db) : IAutomationRunRepository
{
    public async Task<(AutomationRunRecord Run, bool Created)> TryStartAsync(AutomationTriggerFireInput input,
        CancellationToken cancellationToken)
    {
        if (await HasRecentRunAsync(input.RuleId, input.IdempotencyKey, cancellationToken))
        {
            var existing = await db.AutomationRuns.AsNoTracking()
                .Where(x => x.RuleId == input.RuleId && x.IdempotencyKey == input.IdempotencyKey)
                .OrderByDescending(x => x.StartedAt).FirstAsync(cancellationToken);
            return (existing.ToRecord(), false);
        }

        var workflowId = $"jarvis-automation-run-{Guid.CreateVersion7():N}";
        var run = new AutomationRun(input.RuleId, input.OwnerId, workflowId, input.IdempotencyKey,
            input.TriggerKind, SanitizeReason(input.TriggerReason), input.TestRun);
        db.AutomationRuns.Add(run);
        db.AuditEvents.Add(new AuditEvent(input.OwnerId, "automations", input.TestRun ? "run.test" : "run.started",
            "low", true, metadataJson: JsonSerializer.Serialize(new
            {
                resourceId = run.Id,
                ruleId = input.RuleId,
                trigger = input.TriggerKind
            })));
        await db.SaveChangesAsync(cancellationToken);
        return (run.ToRecord(), true);
    }

    public async Task<AutomationRunRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.AutomationRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<AutomationRunRecord>> ListForRuleAsync(Guid ruleId, Guid ownerId, int limit,
        CancellationToken cancellationToken) =>
        (await db.AutomationRuns.AsNoTracking().Where(x => x.RuleId == ruleId && x.OwnerId == ownerId)
            .OrderByDescending(x => x.StartedAt).Take(limit).ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToList();

    public async Task CompleteAsync(Guid runId, string actionResultsJson, CancellationToken cancellationToken)
    {
        var run = await db.AutomationRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null) return;
        run.Complete(actionResultsJson);
        db.AuditEvents.Add(new AuditEvent(run.OwnerId, "automations", "run.completed", "low", true,
            metadataJson: JsonSerializer.Serialize(new { resourceId = run.Id, ruleId = run.RuleId })));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(Guid runId, string? summary, string actionResultsJson, CancellationToken cancellationToken)
    {
        var run = await db.AutomationRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null) return;
        run.Fail(SanitizeReason(summary), actionResultsJson);
        db.AuditEvents.Add(new AuditEvent(run.OwnerId, "automations", "run.failed", "medium", false,
            metadataJson: JsonSerializer.Serialize(new { resourceId = run.Id, ruleId = run.RuleId })));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkWaitingApprovalAsync(Guid runId, Guid approvalId, CancellationToken cancellationToken)
    {
        var run = await db.AutomationRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null) return;
        run.MarkWaitingApproval(approvalId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasRecentRunAsync(Guid ruleId, string idempotencyKey, CancellationToken cancellationToken) =>
        db.AutomationRuns.AnyAsync(x => x.RuleId == ruleId && x.IdempotencyKey == idempotencyKey, cancellationToken);

    private static string SanitizeReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "triggered";
        var trimmed = value.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}
