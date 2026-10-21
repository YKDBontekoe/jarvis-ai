using System.Text.RegularExpressions;
using Jarvis.Application.Learning;

namespace Jarvis.Application.Improvements;

public static partial class CapabilityGapRules
{
    public const string FingerprintPrefix = "gap:";
    public const int WindowDays = 30;
    public const int MinReports = 2;
    public const int MinConversations = 2;
    public const int MaxFindings = 5;
    public const int MinSlugLength = 3;
    public const int MaxSlugLength = 48;

    /// <summary>A build that failed is tried again at most this many times in all, and not more than weekly.</summary>
    public const int MaxBuildAttempts = 3;

    public static readonly TimeSpan RetryAfter = TimeSpan.FromDays(7);

    public static string Fingerprint(string slug) => FingerprintPrefix + slug;

    /// <summary>
    /// Turns the model's name for a missing capability into a short label such as <c>live-train-times</c>. It is the only
    /// text a gap signal keeps, so anything that could carry personal data is refused: long runs of digits, an at sign,
    /// a URL, or more than six words. Returns null when nothing usable is left.
    /// </summary>
    public static string? NormalizeSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.Contains('@') || text.Contains("://", StringComparison.Ordinal) || Digits().IsMatch(text)) return null;
        var slug = Separators().Replace(text.ToLowerInvariant(), "-").Trim('-');
        if (slug.Split('-').Length > 6) return null;
        return slug.Length is >= MinSlugLength and <= MaxSlugLength && Slug().IsMatch(slug) ? slug : null;
    }

    /// <summary>The slug as words, for titles.</summary>
    public static string Readable(string slug) => slug.Replace('-', ' ');

    [GeneratedRegex(@"\d{4,}")]
    private static partial Regex Digits();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Slug();
}

/// <summary>Something Jarvis was asked for more than once and had no tool for.</summary>
public sealed record CapabilityGapFinding(string Slug, int Reports, int Conversations, DateTimeOffset LastSeen)
{
    public string Fingerprint => CapabilityGapRules.Fingerprint(Slug);
}

/// <summary>
/// Groups the <c>capability_gap</c> signals Jarvis recorded when it lacked a tool. Pure: it reads the label, the
/// conversation id and the time, nothing else.
/// </summary>
public static class CapabilityGapMinerEngine
{
    public static IReadOnlyList<CapabilityGapFinding> Mine(IReadOnlyList<LearningSignalRecord> signals,
        DateTimeOffset now)
    {
        var since = now.AddDays(-CapabilityGapRules.WindowDays);
        return signals
            .Where(signal => signal.Kind == LearningSignalKinds.CapabilityGap && signal.CreatedAt >= since &&
                             signal.CreatedAt <= now && CapabilityGapRules.NormalizeSlug(signal.Category) is not null)
            .GroupBy(signal => CapabilityGapRules.NormalizeSlug(signal.Category)!, StringComparer.Ordinal)
            .Select(group => new CapabilityGapFinding(group.Key, group.Count(),
                group.Select(signal => signal.ConversationId).Distinct().Count(), group.Max(signal => signal.CreatedAt)))
            .Where(finding => finding.Reports >= CapabilityGapRules.MinReports &&
                              finding.Conversations >= CapabilityGapRules.MinConversations)
            .OrderByDescending(finding => finding.Conversations).ThenByDescending(finding => finding.Reports)
            .ThenBy(finding => finding.Slug, StringComparer.Ordinal)
            .Take(CapabilityGapRules.MaxFindings)
            .ToList();
    }

    public static double Confidence(CapabilityGapFinding finding) =>
        Math.Min(0.9, 0.4 + 0.1 * finding.Conversations);
}

public static class CapabilityBuildStatuses
{
    /// <summary>Nothing was tried yet.</summary>
    public const string None = "none";

    /// <summary>A draft app was written and passed its test; accepting switches it on.</summary>
    public const string Built = "built";

    /// <summary>The last attempt did not produce working code.</summary>
    public const string Failed = "failed";

    /// <summary>The builder judged that an app cannot do this (no public API, or it is not a tool problem).</summary>
    public const string NotFeasible = "not_feasible";
}

/// <summary>
/// What a capability proposal carries. <see cref="CustomServerId"/> and <see cref="Sha256"/> name the exact draft that
/// passed its test, so accepting it switches on that code and nothing newer.
/// </summary>
public sealed record CapabilityPayload(
    string Slug,
    int Reports,
    int Conversations,
    string BuildStatus = CapabilityBuildStatuses.None,
    Guid? CustomServerId = null,
    string? Sha256 = null,
    string? AppName = null,
    IReadOnlyList<string>? Tools = null,
    IReadOnlyList<string>? Keys = null,
    string? Note = null,
    int Attempts = 0,
    DateTimeOffset? LastAttemptAt = null)
{
    public bool IsBuilt => BuildStatus == CapabilityBuildStatuses.Built && CustomServerId is not null &&
                           !string.IsNullOrWhiteSpace(Sha256);
}

/// <summary>How one unattended build went.</summary>
public sealed record CapabilityBuildResult(
    string Status,
    Guid? CustomServerId = null,
    string? Sha256 = null,
    string? AppName = null,
    IReadOnlyList<string>? Tools = null,
    IReadOnlyList<string>? Keys = null,
    string? Note = null);

/// <summary>
/// Writes and tests a self-built MCP server for a missing capability without anyone watching. It may save drafts and run
/// them in the isolated MCP runner, but never activates anything: switching an app on stays the owner's decision.
/// </summary>
public interface ICapabilityBuilder
{
    Task<CapabilityBuildResult> BuildAsync(Guid ownerId, CapabilityPayload gap, CancellationToken cancellationToken);
}

public interface ICapabilityEvolutionService
{
    /// <summary>
    /// Builds a draft app for the most wanted open capability gap when the owner switched autonomous evolution on.
    /// Returns how many drafts were built (0 or 1).
    /// </summary>
    Task<int> EvolveAsync(Guid ownerId, CancellationToken cancellationToken);
}

/// <summary>The words a capability proposal shows. The slug is the only user-derived text in them.</summary>
public static class CapabilityText
{
    public static string Title(CapabilityPayload payload) => payload.IsBuilt
        ? $"Switch on “{payload.AppName ?? CapabilityGapRules.Readable(payload.Slug)}”, an app Jarvis built for {CapabilityGapRules.Readable(payload.Slug)}"
        : $"Jarvis keeps lacking: {CapabilityGapRules.Readable(payload.Slug)}";

    public static string Evidence(CapabilityPayload payload)
    {
        var asked = $"Needed in {payload.Conversations} conversations ({payload.Reports} times) with no tool for it.";
        return payload.BuildStatus switch
        {
            CapabilityBuildStatuses.Built =>
                $"{asked} Jarvis wrote a draft app and it passed a test in the isolated runner" +
                (payload.Tools is { Count: > 0 } tools ? $" (tools: {string.Join(", ", tools.Take(6))})" : string.Empty) +
                (payload.Keys is { Count: > 0 } keys ? $". It needs keys: {string.Join(", ", keys)}" : string.Empty) +
                ". Accept to switch it on; review its code first under Apps.",
            CapabilityBuildStatuses.NotFeasible =>
                $"{asked} Jarvis could not build an app for it: {payload.Note ?? "no suitable API"}. Accept to note that you want it anyway.",
            CapabilityBuildStatuses.Failed =>
                $"{asked} Jarvis tried to build an app and will try again. Accept to ask Jarvis to prioritise it.",
            _ => $"{asked} Accept to ask Jarvis to build or connect an app for it."
        };
    }
}
