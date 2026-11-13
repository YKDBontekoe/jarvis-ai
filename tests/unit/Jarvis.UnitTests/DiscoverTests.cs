using Jarvis.Application.Discover;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class DiscoverTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static CollisionItem Item(string kind, string title, string text, int daysAgo) =>
        new(kind, Guid.NewGuid(), title, text, Now.AddDays(-daysAgo));

    /// <summary>Filler so rare words are rare: many items about everyday things.</summary>
    private static IEnumerable<CollisionItem> Filler(int count) => Enumerable.Range(0, count)
        .Select(i => Item(i % 2 == 0 ? "memory" : "journal", $"Note {i}",
            $"Went shopping and cooked dinner number{i} with friends", i * 3));

    [Fact]
    public void A_collision_links_different_kinds_far_apart_by_rare_shared_words()
    {
        var clip = Item("library", "The patience of sourdough", "Fermentation teaches patience; a starter needs feeding",
            200);
        var entry = Item("journal", "Journal, 2 Nov", "Starting the new team felt like fermentation: patience, feeding it",
            10);
        var sameKind = Item("library", "Bread basics", "fermentation and patience in baking", 150);
        var tooClose = Item("memory", "Bakes on Sundays", "fermentation patience", 9);

        var collision = CollisionEngine.Find([clip, entry, sameKind, tooClose, .. Filler(40)], new HashSet<string>());

        Assert.NotNull(collision);
        Assert.Equal(clip.Id, collision.Left.Id);
        Assert.Equal(entry.Id, collision.Right.Id);
        Assert.Contains("fermentation", collision.Shared);
        Assert.Contains("patience", collision.Shared);
        Assert.Contains("“fermentation”", collision.Prompt);
        Assert.StartsWith("Help me see a connection between my saved piece", collision.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_seen_collision_is_not_offered_again_and_common_words_link_nothing()
    {
        var clip = Item("library", "Patience", "fermentation patience", 200);
        var entry = Item("journal", "Journal", "fermentation patience", 10);
        var key = CollisionEngine.PairKey(clip, entry);

        Assert.Null(CollisionEngine.Find([clip, entry, .. Filler(40)], new HashSet<string> { key }));
        // "shopping", "cooked" and "dinner" are everywhere, so the filler never collides with itself.
        Assert.Null(CollisionEngine.Find(Filler(40).ToArray(), new HashSet<string>()));
        Assert.Equal(key, CollisionEngine.PairKey(entry, clip));
    }

    [Fact]
    public void Words_drop_short_and_stop_words_and_accents()
    {
        Assert.Equal(["croissant", "paris", "vienne"],
            CollisionEngine.Words("Today a café in Paris, croissant! with the user, Vienné").Order());
    }

    [Fact]
    public void An_excerpt_centres_on_the_shared_word()
    {
        var text = new string('a', 300) + " fermentation " + new string('b', 300);

        var excerpt = CollisionEngine.Excerpt(text, ["fermentation"]);

        Assert.Contains("fermentation", excerpt);
        Assert.StartsWith("…", excerpt, StringComparison.Ordinal);
        Assert.EndsWith("…", excerpt, StringComparison.Ordinal);
        Assert.True(excerpt.Length <= 182);
    }

    [Fact]
    public void Duel_scores_count_only_the_duel_days()
    {
        var from = new DateOnly(2026, 11, 9);
        var to = new DateOnly(2026, 11, 15);
        DateOnly[] days = [from.AddDays(-1), from, from, from.AddDays(3), to, to.AddDays(1)];

        Assert.Equal(4, DuelScoring.Count(DuelMetrics.HabitCheckIns, from, to, days, [], []));
        Assert.Equal(3, DuelScoring.Count(DuelMetrics.JournalDays, from, to, [], days, []));
        Assert.Equal(4, DuelScoring.Count(DuelMetrics.PromisesKept, from, to, [], [], days));
        Assert.Equal(0, DuelScoring.Count("steps", from, to, days, days, days));
    }

    [Fact]
    public void A_duel_has_an_outcome_only_once_it_is_over_and_both_scores_are_in()
    {
        Assert.Null(DuelScoring.Outcome(DuelStatuses.Active, 5, 3));
        Assert.Null(DuelScoring.Outcome(DuelStatuses.Finished, 5, null));
        Assert.Equal("won", DuelScoring.Outcome(DuelStatuses.Finished, 5, 3));
        Assert.Equal("lost", DuelScoring.Outcome(DuelStatuses.Finished, 2, 3));
        Assert.Equal("tied", DuelScoring.Outcome(DuelStatuses.Finished, 3, 3));
    }

    [Fact]
    public void Duel_codes_are_eight_unambiguous_characters_and_tolerate_typing()
    {
        var code = DuelScoring.NewCode();

        Assert.Equal(DuelRules.CodeLength, code.Length);
        Assert.DoesNotContain(code, c => c is '0' or 'O' or '1' or 'I');
        Assert.Equal("ABCD2345", DuelScoring.NormalizeCode(" abcd-2345 "));
        Assert.Null(DuelScoring.NormalizeCode("ABC"));
        Assert.Equal(DuelRoles.Joiner, DuelRoles.Other(DuelRoles.Challenger));
    }
}
