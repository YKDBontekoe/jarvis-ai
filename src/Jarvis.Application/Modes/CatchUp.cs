using Jarvis.Application.Notifications;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Modes;

/// <summary>Which kinds of held notifications the owner wants in the catch-up. Approvals and problems are always in.</summary>
public sealed record CatchUpPreferences(List<string>? Excluded = null)
{
    public bool IsExcluded(string domain) => Excluded?.Contains(domain) == true &&
        !NotificationCategories.Find(domain).Locked;
}

public sealed record CatchUpItem(string Title, int Count, DateTimeOffset At);

public sealed record CatchUpGroup(string Domain, string Title, bool Locked, bool Included, int Count,
    IReadOnlyList<CatchUpItem> Items);

public sealed record CatchUpView(IReadOnlyList<CatchUpGroup> Groups, int Total, bool StillHolding, DateTimeOffset? Since,
    string Summary);

public interface ICatchUpService
{
    Task<CatchUpView> GetAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Includes or leaves out a domain. Locked domains cannot be left out. Returns null for an unknown domain.</summary>
    Task<(CatchUpView? View, string? Error)> SetDomainAsync(Guid ownerId, string domain, bool included,
        CancellationToken cancellationToken);

    /// <summary>Clears what was already summed up. Anything still held stays.</summary>
    Task ClearAsync(Guid ownerId, CancellationToken cancellationToken);
}

/// <summary>
/// "While you were away", in the app: what a quiet mode held back, grouped by kind. The notifications themselves are
/// untouched; this only groups what is already known, and leaves out the kinds the owner chose not to recap.
/// </summary>
public sealed class CatchUpService(IOwnerSettingsStore settings, IModeService modes, TimeProvider? timeProvider = null)
    : ICatchUpService
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<CatchUpView> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var state = await settings.GetAsync<PushDigestState>(ownerId, SettingsSections.PushDigest, cancellationToken)
                    ?? new PushDigestState();
        var prefs = await LoadAsync(ownerId, cancellationToken);
        var now = clock.GetUtcNow();
        var recent = state.Last is { } last && state.LastAt is { } at && now - at <= KeepFor ? last : [];
        var items = state.Items.Concat(recent).DistinctBy(x => x.NotificationId).ToArray();
        var holding = state.Items.Count > 0 &&
                      !await modes.ShouldPushAsync(ownerId, PushDigestService.DigestType, cancellationToken);

        var groups = NotificationCategories.All.Select(category =>
        {
            var mine = items.Where(i => NotificationCategories.For(i.Type) == category.Id).ToArray();
            var included = !prefs.IsExcluded(category.Id);
            var lines = included
                ? mine.GroupBy(i => i.Title.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0)
                    .Select(g => new CatchUpItem(g.Key, g.Count(), g.Max(i => i.At))).OrderByDescending(x => x.At).ToArray()
                : [];
            return new CatchUpGroup(category.Id, category.Title, category.Locked, included, mine.Length, lines);
        }).Where(g => g.Count > 0 || !g.Included).ToArray();

        var shown = groups.Where(g => g.Included).Sum(g => g.Count);
        var left = groups.Where(g => !g.Included).Sum(g => g.Count);
        var summary = shown == 0 && left == 0
            ? "Nothing was held back."
            : holding
                ? $"{shown} held back until your mode lets pushes through."
                : $"{shown} came in while pushes were quiet." + (left > 0 ? $" {left} more are left out of this recap but still in your notifications." : "");
        return new CatchUpView(groups, shown, holding, items.Length == 0 ? null : items.Min(i => i.At), summary);
    }

    public async Task<(CatchUpView? View, string? Error)> SetDomainAsync(Guid ownerId, string domain, bool included,
        CancellationToken cancellationToken)
    {
        if (!NotificationCategories.IsKnown(domain)) return (null, null);
        if (!included && NotificationCategories.Find(domain).Locked)
            return (null, "Approvals and problems always stay in the catch-up.");
        var prefs = await LoadAsync(ownerId, cancellationToken);
        var excluded = (prefs.Excluded ?? []).Where(d => d != domain).ToList();
        if (!included) excluded.Add(domain);
        await settings.SaveAsync(ownerId, SettingsSections.CatchUp, new CatchUpPreferences(excluded), cancellationToken);
        return (await GetAsync(ownerId, cancellationToken), null);
    }

    public async Task ClearAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var state = await settings.GetAsync<PushDigestState>(ownerId, SettingsSections.PushDigest, cancellationToken)
                    ?? new PushDigestState();
        await settings.SaveAsync(ownerId, SettingsSections.PushDigest, state with { Last = null, LastAt = null },
            cancellationToken);
    }

    private async Task<CatchUpPreferences> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<CatchUpPreferences>(ownerId, SettingsSections.CatchUp, cancellationToken)
        ?? new CatchUpPreferences();
}
