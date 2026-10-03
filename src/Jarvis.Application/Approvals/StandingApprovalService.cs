using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Approvals;

public enum StandingApprovalGrantResult
{
    Granted,
    NotAllowed,
    TooMany
}

public sealed record StandingApprovalGrant(string Category, string Label, DateTimeOffset GrantedAt);

public sealed record StandingApprovalSettings(IReadOnlyList<StandingApprovalGrant>? Grants)
{
    public static StandingApprovalSettings Empty { get; } = new([]);
}

/// <summary>
/// Owner-scoped categories of tool actions that Jarvis may approve without asking again.
/// Stored with the owner's other settings. Profiles cannot grant or revoke these.
/// </summary>
public interface IStandingApprovalService
{
    Task<IReadOnlyList<StandingApprovalGrant>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<bool> IsGrantedAsync(Guid ownerId, string categoryKey, CancellationToken cancellationToken);
    Task<StandingApprovalGrantResult> GrantAsync(Guid ownerId, ApprovalCategory category,
        CancellationToken cancellationToken);
    Task<bool> RevokeAsync(Guid ownerId, string categoryKey, CancellationToken cancellationToken);
    Task RecordAutomaticUseAsync(Guid ownerId, string toolName, ApprovalCategory category, Guid? conversationId,
        CancellationToken cancellationToken);
}

public sealed class StandingApprovalService(IOwnerSettingsStore settings, IAuditEventStore audit) : IStandingApprovalService
{
    public const int MaxGrants = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<StandingApprovalGrant>> ListAsync(Guid ownerId,
        CancellationToken cancellationToken)
    {
        var grants = await LoadAsync(ownerId, cancellationToken);
        return grants
            .OrderBy(grant => grant.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(grant => grant.Category, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<bool> IsGrantedAsync(Guid ownerId, string categoryKey, CancellationToken cancellationToken)
    {
        if (!ApprovalCategories.IsSafeKey(categoryKey)) return false;
        var grants = await LoadAsync(ownerId, cancellationToken);
        return grants.Any(grant => grant.Category == categoryKey);
    }

    public async Task<StandingApprovalGrantResult> GrantAsync(Guid ownerId, ApprovalCategory category,
        CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner is required.", nameof(ownerId));
        if (!category.CanRemember || !ApprovalCategories.IsSafeKey(category.Key))
            return StandingApprovalGrantResult.NotAllowed;

        var grants = await LoadAsync(ownerId, cancellationToken);
        var existing = grants.FindIndex(grant => grant.Category == category.Key);
        if (existing < 0 && grants.Count >= MaxGrants) return StandingApprovalGrantResult.TooMany;

        var label = category.Label.Trim();
        if (label.Length > 80) label = label[..80].TrimEnd();
        var stored = new StandingApprovalGrant(category.Key, label, DateTimeOffset.UtcNow);
        if (existing >= 0) grants[existing] = stored;
        else grants.Add(stored);

        await settings.SaveAsync(ownerId, SettingsSections.StandingApprovals, new StandingApprovalSettings(grants),
            cancellationToken);
        await TryAuditAsync(ownerId, category.Key, "approval.category_granted",
            new { category = category.Key, label }, cancellationToken);
        return StandingApprovalGrantResult.Granted;
    }

    public async Task<bool> RevokeAsync(Guid ownerId, string categoryKey, CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner is required.", nameof(ownerId));
        if (!ApprovalCategories.IsSafeKey(categoryKey)) return false;
        var grants = await LoadAsync(ownerId, cancellationToken);
        if (grants.RemoveAll(grant => grant.Category == categoryKey) == 0) return false;
        await settings.SaveAsync(ownerId, SettingsSections.StandingApprovals, new StandingApprovalSettings(grants),
            cancellationToken);
        await TryAuditAsync(ownerId, categoryKey, "approval.category_revoked",
            new { category = categoryKey }, cancellationToken);
        return true;
    }

    public Task RecordAutomaticUseAsync(Guid ownerId, string toolName, ApprovalCategory category,
        Guid? conversationId, CancellationToken cancellationToken) =>
        TryAuditAsync(ownerId, toolName, "approval.auto_approved",
            new { category = category.Key, conversationId }, cancellationToken);

    private async Task<List<StandingApprovalGrant>> LoadAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAsync<StandingApprovalSettings>(ownerId, SettingsSections.StandingApprovals,
            cancellationToken);
        var grants = new List<StandingApprovalGrant>();
        if (stored?.Grants is null) return grants;
        foreach (var grant in stored.Grants)
        {
            if (grant is null || !ApprovalCategories.IsSafeKey(grant.Category) ||
                string.IsNullOrWhiteSpace(grant.Label))
                continue;
            var label = grant.Label.Trim();
            if (label.Length > 80) label = label[..80].TrimEnd();
            grants.RemoveAll(existing => existing.Category == grant.Category);
            grants.Add(new StandingApprovalGrant(grant.Category, label, grant.GrantedAt));
        }

        return grants;
    }

    private async Task TryAuditAsync(Guid ownerId, string tool, string action, object metadata,
        CancellationToken cancellationToken)
    {
        var name = tool.Trim();
        if (name.Length > 120) name = name[..120];
        if (name.Length == 0) name = "standing_approval";
        try
        {
            await audit.AppendAsync(ownerId, name, action, "high", true, null,
                JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }
}
