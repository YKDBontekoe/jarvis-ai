using Jarvis.Application.Approvals;
using Jarvis.Domain.Knowing;
using Jarvis.Domain.Memory;

namespace Jarvis.Application.Knowing;

/// <summary>Pure arithmetic for "Train Jarvis": which memories to ask about, and the score.</summary>
public static class TrainingEngine
{
    public static bool IsActive(MemoryRecord memory, DateTimeOffset now) =>
        memory.SupersededById is null && (memory.ValidUntil is not { } until || until > now);

    /// <summary>Active, unpinned memories Jarvis inferred, least sure and oldest first, not asked about lately.</summary>
    public static IReadOnlyList<MemoryRecord> Candidates(IEnumerable<MemoryRecord> memories,
        IEnumerable<MemoryReview> reviews, DateTimeOffset now)
    {
        var recent = reviews.Where(r => r.ReviewedAt >= now.AddDays(-KnowingRules.ReviewCooldownDays))
            .Select(r => r.MemoryId).ToHashSet();
        return memories
            .Where(m => IsActive(m, now) && !m.IsPinned && !KnowingRules.SkippedKinds.Contains(m.Kind) &&
                        !recent.Contains(m.Id) && !string.IsNullOrWhiteSpace(m.Content))
            .OrderBy(m => m.Confidence).ThenBy(m => m.UpdatedAt)
            .ToArray();
    }

    public static TrainingStats Stats(IReadOnlyList<MemoryReview> reviews, int reviewableLeft, DateOnly today,
        Func<DateTimeOffset, DateOnly> localDate)
    {
        var right = reviews.Count(r => r.Verdict == MemoryVerdicts.Right);
        var wrong = reviews.Count(r => r.Verdict == MemoryVerdicts.Wrong);
        var outdated = reviews.Count(r => r.Verdict == MemoryVerdicts.Outdated);
        var days = reviews.Select(r => localDate(r.ReviewedAt)).ToHashSet();
        return new TrainingStats(reviews.Count, right, wrong, outdated,
            reviews.Count == 0 ? null : (int)Math.Round(100.0 * right / reviews.Count),
            reviews.Count(r => localDate(r.ReviewedAt) == today), KnowingRules.DailyGoal, Streak(days, today),
            reviewableLeft);
    }

    /// <summary>Consecutive days with at least one answer, ending today (or yesterday while today is still open).</summary>
    public static int Streak(IReadOnlySet<DateOnly> days, DateOnly today)
    {
        var day = days.Contains(today) ? today : today.AddDays(-1);
        var count = 0;
        while (days.Contains(day))
        {
            count++;
            day = day.AddDays(-1);
        }

        return count;
    }
}

/// <summary>
/// The map of the unknown: for the entities Jarvis knows best, which basic facts are still missing. People should
/// have a birthday, work and home; projects a status and a deadline. Pure.
/// </summary>
public static class GapEngine
{
    public const int EntitiesConsidered = 30;

    private sealed record Expectation(string Predicate, string[] Satisfiers, Func<string, string> Question);

    private static readonly Dictionary<string, Expectation[]> Expected = new(StringComparer.Ordinal)
    {
        ["person"] =
        [
            new("birthday", ["birthday", "born_on"], n => $"When is {n}’s birthday?"),
            new("job_title", ["job_title", "works_at", "studies_at"], n => $"What does {n} do for work?"),
            new("lives_in", ["lives_in"], n => $"Where does {n} live?")
        ],
        ["project"] =
        [
            new("status", ["status"], n => $"Where does {n} stand right now?"),
            new("deadline", ["deadline"], n => $"Is there a deadline for {n}?")
        ],
        ["pet"] =
        [
            new("birthday", ["birthday", "age"], n => $"How old is {n}?")
        ]
    };

    public static (IReadOnlyList<KnowledgeGap> Gaps, IReadOnlyList<GapEntity> Entities) Find(
        Memory.GraphOverview graph, IReadOnlySet<string> skipped)
    {
        var predicates = graph.Edges.Select(e => (e.From, e.Predicate))
            .Concat(graph.Literals.Select(l => (From: l.EntityId, l.Predicate)))
            .GroupBy(x => x.From)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Predicate).ToHashSet(StringComparer.Ordinal));

        var gaps = new List<KnowledgeGap>();
        var entities = new List<GapEntity>();
        foreach (var entity in graph.Entities
                     .Where(e => Expected.ContainsKey(e.Type) && !IsOwner(e.Name))
                     .OrderByDescending(e => e.RelationCount).ThenBy(e => e.Name, StringComparer.Ordinal)
                     .Take(EntitiesConsidered))
        {
            var has = predicates.GetValueOrDefault(entity.Id) ?? [];
            var known = 0;
            var expected = Expected[entity.Type];
            foreach (var expectation in expected)
            {
                if (expectation.Satisfiers.Any(has.Contains))
                {
                    known++;
                    continue;
                }

                // A gap the owner skipped stays unknown but is never asked again.
                if (skipped.Contains(Key(entity.Id, expectation.Predicate))) continue;
                gaps.Add(new KnowledgeGap(entity.Id, entity.Name, entity.Type, expectation.Predicate,
                    expectation.Question(entity.Name)));
            }

            entities.Add(new GapEntity(entity.Id, entity.Name, entity.Type, known, expected.Length));
        }

        return (gaps, entities);
    }

    public static bool IsKnownPredicate(string type, string predicate) =>
        Expected.TryGetValue(type, out var expected) && expected.Any(e => e.Predicate == predicate);

    public static string Key(Guid entityId, string predicate) => entityId.ToString("N") + ":" + predicate;

    /// <summary>The graph's node for the owner themselves is not someone to ask about.</summary>
    private static bool IsOwner(string name) =>
        name.Trim().ToLowerInvariant() is "user" or "you" or "me" or "the user" or "owner" or "ik" or "jij";

    /// <summary>What kind of node the answer is: a place to live, an organization to work at, otherwise a value.</summary>
    public static (string? ObjectType, bool IsEntity) AnswerShape(string predicate) => predicate switch
    {
        "lives_in" => ("place", true),
        _ => (null, false)
    };
}

/// <summary>
/// The trust ladder, from the owner's own approval decisions. Each run of approvals in a row in one category climbs;
/// a decline starts it again. Nothing here changes what Jarvis may do: handing a category over is the owner's choice
/// and goes through the standing approvals.
/// </summary>
public static class TrustEngine
{
    public const int LearningAt = 3;
    public const int TrustedAt = 10;
    public static readonly TimeSpan HandOverFor = TimeSpan.FromDays(90);

    /// <summary>Categories that are never offered for handing over, whatever the streak.</summary>
    private static readonly string[] NeverOffered = ["coding.", "integrations.", "agents.", "people.remove"];

    public static bool MayHandOver(string category) =>
        !NeverOffered.Any(prefix => category.StartsWith(prefix, StringComparison.Ordinal));

    public static IReadOnlyList<TrustRung> Climb(IEnumerable<DecidedApproval> decisions,
        IReadOnlyList<StandingApprovalGrant> grants, DateTimeOffset now)
    {
        var rungs = new List<TrustRung>();
        foreach (var group in decisions
                     .Select(d => (Decision: d, Category: ApprovalCategories.Resolve(d.ToolName, d.ArgumentsJson)))
                     .Where(x => x.Category.CanRemember)
                     .GroupBy(x => x.Category.Key, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(x => x.Decision.DecidedAt).ToArray();
            var streak = 0;
            foreach (var item in ordered) streak = item.Decision.Approved ? streak + 1 : 0;
            var grant = grants.FirstOrDefault(g => g.Category == group.Key && g.IsActive(now));
            var level = grant is not null ? TrustLevels.HandsFree
                : streak >= TrustedAt ? TrustLevels.Trusted
                : streak >= LearningAt ? TrustLevels.Learning
                : TrustLevels.New;
            rungs.Add(new TrustRung(group.Key, ordered[^1].Category.Label, level, streak,
                ordered.Count(x => x.Decision.Approved), ordered.Count(x => !x.Decision.Approved),
                streak >= TrustedAt ? TrustedAt : streak >= LearningAt ? TrustedAt : LearningAt,
                grant is null && streak >= TrustedAt && MayHandOver(group.Key),
                ordered[^1].Decision.DecidedAt, grant?.ExpiresAt));
        }

        return rungs.OrderByDescending(r => Rank(r.Level)).ThenByDescending(r => r.Streak)
            .ThenBy(r => r.Label, StringComparer.Ordinal).ToArray();
    }

    private static int Rank(string level) => level switch
    {
        TrustLevels.HandsFree => 3,
        TrustLevels.Trusted => 2,
        TrustLevels.Learning => 1,
        _ => 0
    };
}
