using Jarvis.Application.Search;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Memory;

/// <summary>Topics the owner never wants Jarvis to learn from. Only what the owner typed.</summary>
public sealed record DoNotLearnList(List<string>? Topics = null);

public static class DoNotLearnRules
{
    public const int MaxTopics = 30;
    public const int MaxTopicLength = 60;

    /// <summary>Folds case and accents so "Café" and "cafe" are the same topic.</summary>
    public static string Fold(string text) => SearchRanking.NormalizeQuery(text);

    public static string? Clean(string? topic)
    {
        var clean = topic is null ? null : string.Join(' ', topic.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrEmpty(clean) || clean.Length > MaxTopicLength ? null : clean;
    }

    /// <summary>True when the text mentions any topic as whole words, ignoring case and accents.</summary>
    public static bool Mentions(string? text, IEnumerable<string>? topics)
    {
        if (string.IsNullOrWhiteSpace(text) || topics is null) return false;
        var haystack = " " + Fold(text) + " ";
        return topics.Select(Fold).Where(t => t.Length > 0).Any(t => haystack.Contains(" " + t + " ", StringComparison.Ordinal) ||
                                                                      haystack.Contains(" " + t + ".", StringComparison.Ordinal) ||
                                                                      haystack.Contains(" " + t + ",", StringComparison.Ordinal));
    }
}

public interface IDoNotLearnService
{
    Task<IReadOnlyList<string>> ListAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Replaces the list. Null result means a topic was too long or there were too many.</summary>
    Task<IReadOnlyList<string>?> SetAsync(Guid ownerId, IReadOnlyList<string>? topics, CancellationToken cancellationToken);

    Task<bool> MentionsAsync(Guid ownerId, string? text, CancellationToken cancellationToken);
}

public sealed class DoNotLearnService(IOwnerSettingsStore settings) : IDoNotLearnService
{
    public async Task<IReadOnlyList<string>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await settings.GetAsync<DoNotLearnList>(ownerId, SettingsSections.DoNotLearn, cancellationToken))?.Topics ?? [];

    public async Task<IReadOnlyList<string>?> SetAsync(Guid ownerId, IReadOnlyList<string>? topics,
        CancellationToken cancellationToken)
    {
        var cleaned = new List<string>();
        foreach (var topic in topics ?? [])
        {
            if (string.IsNullOrWhiteSpace(topic)) continue;
            if (DoNotLearnRules.Clean(topic) is not { } clean) return null;
            if (!cleaned.Any(x => DoNotLearnRules.Fold(x) == DoNotLearnRules.Fold(clean))) cleaned.Add(clean);
        }
        if (cleaned.Count > DoNotLearnRules.MaxTopics) return null;
        await settings.SaveAsync(ownerId, SettingsSections.DoNotLearn, new DoNotLearnList(cleaned), cancellationToken);
        return cleaned;
    }

    public async Task<bool> MentionsAsync(Guid ownerId, string? text, CancellationToken cancellationToken) =>
        DoNotLearnRules.Mentions(text, await ListAsync(ownerId, cancellationToken));
}
