using System.Text;

namespace Jarvis.Application.Search;

public static class SearchRanking
{
    private const double ExactTitleBoost = 0.35;
    private const double PinnedBoost = 0.2;
    private const double RecencyHalfLifeDays = 14;

    public static double Combine(double providerRelevance, string title, string query, bool isPinned,
        DateTimeOffset? timestamp)
    {
        var text = TextRelevance(title, query);
        var blended = 0.55 * NormalizeProviderScore(providerRelevance) + 0.45 * text;
        if (IsExactTitleMatch(title, query)) blended += ExactTitleBoost;
        if (isPinned) blended += PinnedBoost;
        blended += RecencyBoost(timestamp);
        return blended;
    }

    public static string NormalizeQuery(string query) =>
        query.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    public static bool MatchesQuery(string haystack, string normalizedQuery)
    {
        if (normalizedQuery.Length == 0) return false;
        return NormalizeForMatch(haystack).Contains(normalizedQuery, StringComparison.Ordinal);
    }

    public static double TextRelevance(string title, string query)
    {
        var normalizedQuery = NormalizeQuery(query);
        if (normalizedQuery.Length == 0) return 0;
        var normalizedTitle = NormalizeForMatch(title);
        if (normalizedTitle.Length == 0) return 0;
        if (normalizedTitle.Equals(normalizedQuery, StringComparison.Ordinal)) return 1;
        if (normalizedTitle.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 0.85;
        if (normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal)) return 0.65;
        return 0;
    }

    public static bool IsExactTitleMatch(string title, string query) =>
        string.Equals(NormalizeForMatch(title), NormalizeQuery(query), StringComparison.Ordinal);

    public static string TruncateSummary(string? value, int maxLength = 240)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + "…";
    }

    private static double NormalizeProviderScore(double score) => score switch
    {
        <= 0 => 0,
        >= 1 => 1,
        _ => score
    };

    private static double RecencyBoost(DateTimeOffset? timestamp)
    {
        if (timestamp is null) return 0;
        var ageDays = Math.Max(0, (DateTimeOffset.UtcNow - timestamp.Value).TotalDays);
        return 0.15 * Math.Exp(-ageDays / RecencyHalfLifeDays);
    }

    private static string NormalizeForMatch(string value) =>
        NormalizeQuery(value).ToLowerInvariant();
}
