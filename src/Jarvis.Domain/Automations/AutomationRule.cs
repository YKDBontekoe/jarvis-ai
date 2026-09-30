namespace Jarvis.Domain.Automations;

public sealed class AutomationRule
{
    private AutomationRule() { }

    public AutomationRule(Guid ownerId, string name, string definitionJson, int schemaVersion)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        Name = name;
        DefinitionJson = definitionJson;
        SchemaVersion = schemaVersion;
        Status = AutomationRuleStatuses.Draft;
        ScheduleWorkflowId = $"jarvis-automation-{Id:N}";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; }
    public string DefinitionJson { get; private set; } = string.Empty;
    public string Status { get; private set; } = AutomationRuleStatuses.Draft;
    public string ScheduleWorkflowId { get; private set; } = string.Empty;
    public DateTimeOffset? ScheduleDispatchedAt { get; private set; }
    public DateTimeOffset? LastRunAt { get; private set; }
    public DateTimeOffset? NextRunAt { get; private set; }
    public DateTimeOffset? CooldownUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? ConversationId { get; private set; }

    public const int ScheduleStaleGraceMinutes = 30;

    public void AttachConversation(Guid conversationId)
    {
        if (conversationId == Guid.Empty)
            throw new ArgumentException("A conversation id is required.", nameof(conversationId));
        ConversationId = conversationId;
    }

    public void UpdateDraft(string name, string definitionJson, int schemaVersion)
    {
        if (Status != AutomationRuleStatuses.Draft && Status != AutomationRuleStatuses.Disabled)
            throw new InvalidOperationException("Only draft or disabled rules can be edited in place.");
        Name = name;
        DefinitionJson = definitionJson;
        SchemaVersion = schemaVersion;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Enable()
    {
        if (Status == AutomationRuleStatuses.Enabled) return;
        if (Status == AutomationRuleStatuses.Draft)
            Status = AutomationRuleStatuses.Enabled;
        else if (Status == AutomationRuleStatuses.Disabled)
            Status = AutomationRuleStatuses.Enabled;
        else
            throw new InvalidOperationException("This rule cannot be enabled.");
        ScheduleDispatchedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Disable()
    {
        if (Status != AutomationRuleStatuses.Enabled) return;
        Status = AutomationRuleStatuses.Disabled;
        NextRunAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkScheduleDispatched() => ScheduleDispatchedAt ??= DateTimeOffset.UtcNow;

    public bool IsScheduleStale(DateTimeOffset utcNow) =>
        Status == AutomationRuleStatuses.Enabled && ScheduleDispatchedAt is not null &&
        ScheduleDispatchedAt.Value.AddMinutes(ScheduleStaleGraceMinutes) < utcNow &&
        (NextRunAt is null || NextRunAt > utcNow.AddHours(1));

    public void RecordRunScheduled(DateTimeOffset? nextRunAt)
    {
        LastRunAt = DateTimeOffset.UtcNow;
        NextRunAt = nextRunAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordNextRun(DateTimeOffset? nextRunAt) => NextRunAt = nextRunAt;

    public void ApplyCooldown(int cooldownMinutes)
    {
        if (cooldownMinutes <= 0) return;
        CooldownUntil = DateTimeOffset.UtcNow.AddMinutes(cooldownMinutes);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsInCooldown(DateTimeOffset utcNow) =>
        CooldownUntil is not null && CooldownUntil > utcNow;
}

public static class AutomationRuleStatuses
{
    public const string Draft = "draft";
    public const string Enabled = "enabled";
    public const string Disabled = "disabled";
}
