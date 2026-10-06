using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Approvals;

public enum StandingApprovalGrantResult
{
    Granted,
    NotAllowed,
    TooMany,
    InvalidTerms
}

public static class StandingApprovalScopes
{
    /// <summary>The grant applies everywhere, including chats the owner is watching.</summary>
    public const string All = "all";

    /// <summary>The grant applies only to background task and automation runs.</summary>
    public const string Tasks = "tasks";

    public static bool IsValid(string? scope) => scope is null or All or Tasks;
}

/// <summary>
/// Optional limits on a standing grant: how long it lasts and where it applies. Without them a grant is permanent
/// and applies everywhere, as before.
/// </summary>
public sealed record GrantTerms(TimeSpan? Lifetime = null, string? Scope = null)
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(365);

    public bool IsValid =>
        StandingApprovalScopes.IsValid(Scope) && (Lifetime is null || (Lifetime > TimeSpan.Zero && Lifetime <= MaxLifetime));
}

public sealed record StandingApprovalGrant(string Category, string Label, DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt = null, string? Scope = null)
{
    public bool IsActive(DateTimeOffset now) => ExpiresAt is not { } expires || expires > now;

    /// <summary>True when the grant covers this kind of run.</summary>
    public bool Covers(bool backgroundTask) => Scope != StandingApprovalScopes.Tasks || backgroundTask;
}

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
    Task<bool> IsGrantedAsync(Guid ownerId, string categoryKey, CancellationToken cancellationToken,
        bool backgroundTask = false);
    Task<StandingApprovalGrantResult> GrantAsync(Guid ownerId, ApprovalCategory category,
        CancellationToken cancellationToken, GrantTerms? terms = null);
    Task<bool> RevokeAsync(Guid ownerId, string categoryKey, CancellationToken cancellationToken);
    Task RecordAutomaticUseAsync(Guid ownerId, string toolName, ApprovalCategory category, Guid? conversationId,
        CancellationToken cancellationToken);
}

public sealed class StandingApprovalService(IOwnerSettingsStore settings, IAuditEventStore audit,
    TimeProvider? timeProvider = null) : IStandingApprovalService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
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

    public async Task<bool> IsGrantedAsync(Guid ownerId, string categoryKey, CancellationToken cancellationToken,
        bool backgroundTask = false)
    {
        if (!ApprovalCategories.IsSafeKey(categoryKey)) return false;
        var grants = await LoadAsync(ownerId, cancellationToken);
        return grants.Any(grant => grant.Category == categoryKey && grant.Covers(backgroundTask));
    }

    public async Task<StandingApprovalGrantResult> GrantAsync(Guid ownerId, ApprovalCategory category,
        CancellationToken cancellationToken, GrantTerms? terms = null)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner is required.", nameof(ownerId));
        if (!category.CanRemember || !ApprovalCategories.IsSafeKey(category.Key))
            return StandingApprovalGrantResult.NotAllowed;
        if (terms is { IsValid: false }) return StandingApprovalGrantResult.InvalidTerms;

        var grants = await LoadAsync(ownerId, cancellationToken);
        var existing = grants.FindIndex(grant => grant.Category == category.Key);
        if (existing < 0 && grants.Count >= MaxGrants) return StandingApprovalGrantResult.TooMany;

        var label = category.Label.Trim();
        if (label.Length > 80) label = label[..80].TrimEnd();
        var now = _clock.GetUtcNow();
        var scope = terms?.Scope is null or StandingApprovalScopes.All ? null : terms.Scope;
        var stored = new StandingApprovalGrant(category.Key, label, now,
            terms?.Lifetime is { } lifetime ? now + lifetime : null, scope);
        if (existing >= 0) grants[existing] = stored;
        else grants.Add(stored);

        await settings.SaveAsync(ownerId, SettingsSections.StandingApprovals, new StandingApprovalSettings(grants),
            cancellationToken);
        await TryAuditAsync(ownerId, category.Key, "approval.category_granted",
            new { category = category.Key, label, expiresAt = stored.ExpiresAt, scope = stored.Scope }, cancellationToken);
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
        var now = _clock.GetUtcNow();
        foreach (var grant in stored.Grants)
        {
            // Expired grants are dropped here, so the next save also clears them from storage.
            if (grant is null || !ApprovalCategories.IsSafeKey(grant.Category) ||
                string.IsNullOrWhiteSpace(grant.Label) || !grant.IsActive(now) ||
                !StandingApprovalScopes.IsValid(grant.Scope))
                continue;
            var label = grant.Label.Trim();
            if (label.Length > 80) label = label[..80].TrimEnd();
            grants.RemoveAll(existing => existing.Category == grant.Category);
            grants.Add(new StandingApprovalGrant(grant.Category, label, grant.GrantedAt, grant.ExpiresAt, grant.Scope));
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
