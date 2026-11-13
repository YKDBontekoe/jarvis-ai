using System.Globalization;
using System.Text;

namespace Jarvis.Application.Discover;

/// <summary>
/// Finds collisions: two things of different kinds (a saved article and a journal entry, a memory and a note) written
/// at least two weeks apart that share words rare across everything the owner keeps. Rare shared words are what make
/// a link non-obvious; common ones ("work", "today") are ignored. Pure.
/// </summary>
public static class CollisionEngine
{
    public const int MinGapDays = 14;
    public const int MinShared = 2;

    /// <summary>A word that appears in more than this share of items is too common to link anything.</summary>
    public const double MaxDocumentShare = 0.08;

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "about", "after", "again", "also", "always", "because", "been", "before", "being", "between", "both", "could",
        "doing", "during", "each", "even", "every", "first", "from", "going", "good", "great", "have", "having",
        "here", "into", "just", "know", "like", "little", "long", "made", "make", "many", "maybe", "more", "most",
        "much", "must", "need", "never", "next", "only", "other", "over", "really", "same", "should", "since", "some",
        "still", "such", "take", "than", "that", "their", "them", "then", "there", "these", "they", "thing",
        "things", "think", "this", "those", "through", "time", "today", "tomorrow", "very", "want", "week", "well",
        "were", "what", "when", "where", "which", "while", "will", "with", "without", "work", "would", "year",
        "yesterday", "your", "user", "owner", "jarvis", "prefers", "likes", "feel", "felt", "day", "days",
        // Dutch
        "maar", "voor", "naar", "door", "over", "zijn", "hebben", "heeft", "werd", "worden", "niet", "ook", "nog",
        "deze", "die", "dat", "een", "het", "van", "met", "wat", "waar", "wanneer", "vandaag", "morgen", "gisteren",
        "omdat", "toen", "alleen", "altijd", "veel", "weer", "even", "goed", "echt", "kunnen", "moet", "wilde"
    };

    public static Collision? Find(IReadOnlyList<CollisionItem> items, IReadOnlySet<string> seen)
    {
        if (items.Count < 2) return null;
        var words = items.Select(i => Words(i.Title + " " + i.Text)).ToArray();
        var postings = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < words.Length; index++)
            foreach (var word in words[index])
                (postings.TryGetValue(word, out var list) ? list : postings[word] = []).Add(index);

        // Small collections still need room: a word in up to five items can link two of them.
        var maxDocs = Math.Max(5, (int)Math.Floor(items.Count * MaxDocumentShare));
        var scores = new Dictionary<(int, int), (double Score, List<string> Shared)>();
        foreach (var (word, docs) in postings)
        {
            if (docs.Count < 2 || docs.Count > maxDocs) continue;
            var weight = Math.Log((double)items.Count / docs.Count);
            for (var a = 0; a < docs.Count; a++)
            for (var b = a + 1; b < docs.Count; b++)
            {
                var (left, right) = (items[docs[a]], items[docs[b]]);
                if (left.Kind == right.Kind || Math.Abs((left.At - right.At).TotalDays) < MinGapDays) continue;
                var key = (docs[a], docs[b]);
                var entry = scores.TryGetValue(key, out var found) ? found : (0d, new List<string>());
                entry.Item2.Add(word);
                scores[key] = (entry.Item1 + weight, entry.Item2);
            }
        }

        var best = scores
            .Where(s => s.Value.Shared.Count >= MinShared)
            .Select(s => (Pair: s.Key, s.Value.Score, s.Value.Shared,
                Key: PairKey(items[s.Key.Item1], items[s.Key.Item2])))
            .Where(s => !seen.Contains(s.Key))
            .OrderByDescending(s => s.Score).ThenBy(s => s.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        if (best.Key is null) return null;

        var (first, second) = (items[best.Pair.Item1], items[best.Pair.Item2]);
        if (first.At > second.At) (first, second) = (second, first);
        var shared = best.Shared.OrderBy(w => postings[w].Count).ThenBy(w => w, StringComparer.Ordinal).Take(4)
            .ToArray();
        return new Collision(best.Key, Side(first, shared), Side(second, shared), shared,
            $"Help me see a connection between my {Noun(first.Kind)} “{first.Title}” " +
            $"({first.At:d MMM yyyy}) and my {Noun(second.Kind)} “{second.Title}” ({second.At:d MMM yyyy}). " +
            $"Both mention {Join(shared)}. What might one say about the other?");
    }

    public static string PairKey(CollisionItem a, CollisionItem b)
    {
        var (x, y) = string.CompareOrdinal(a.Id.ToString("N"), b.Id.ToString("N")) <= 0 ? (a, b) : (b, a);
        return x.Id.ToString("N")[..12] + y.Id.ToString("N")[..12];
    }

    /// <summary>Distinct lower-case words of at least five letters, accents removed, without stop words.</summary>
    public static IReadOnlySet<string> Words(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetter(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 5 && !Stop.Contains(w))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static CollisionSide Side(CollisionItem item, IReadOnlyList<string> shared) =>
        new(item.Kind, item.Id, item.Title, Excerpt(item.Text, shared), DateOnly.FromDateTime(item.At.UtcDateTime));

    /// <summary>A short window of the text around the first shared word, so the link is visible.</summary>
    public static string Excerpt(string text, IReadOnlyList<string> shared)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= 180) return flat;
        var lower = flat.ToLowerInvariant();
        var at = shared.Select(w => lower.IndexOf(w, StringComparison.Ordinal)).Where(i => i >= 0)
            .DefaultIfEmpty(0).Min();
        var start = Math.Max(0, at - 60);
        var length = Math.Min(180, flat.Length - start);
        return (start > 0 ? "…" : "") + flat.Substring(start, length).Trim() +
               (start + length < flat.Length ? "…" : "");
    }

    private static string Noun(string kind) => kind switch
    {
        "library" => "saved piece",
        "journal" => "journal entry",
        "memory" => "note about me",
        _ => kind
    };

    private static string Join(IReadOnlyList<string> words) =>
        words.Count == 1 ? $"“{words[0]}”"
        : string.Join(", ", words.Take(words.Count - 1).Select(w => $"“{w}”")) + $" and “{words[^1]}”";
}
