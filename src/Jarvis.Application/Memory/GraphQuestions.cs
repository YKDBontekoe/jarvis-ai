using System.Globalization;
using System.Text;

namespace Jarvis.Application.Memory;

public sealed record GraphQuestionTurn(string Question, string Answer);

public sealed record AskGraphRequest(string? Question, IReadOnlyList<Guid>? FocusIds,
    IReadOnlyList<GraphQuestionTurn>? History);

/// <summary>A link the answer relied on, so the map can light it up.</summary>
public sealed record GraphAnswerLink(Guid From, Guid To, string Predicate);

/// <summary>
/// An answer drawn only from the owner's graph. <see cref="Grounded"/> is false when the model was unavailable and the
/// answer is a plain list of the facts found.
/// </summary>
public sealed record GraphAnswer(string Answer, IReadOnlyList<Guid> EntityIds, IReadOnlyList<GraphAnswerLink> Links,
    IReadOnlyList<string> Facts, IReadOnlyList<string> FollowUps, bool Grounded);

/// <summary>One fact handed to the model, numbered so the answer can cite it.</summary>
public sealed record GraphContextFact(int Id, Guid SubjectId, string Subject, string Predicate, Guid? ObjectId,
    string Object, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo)
{
    public string Sentence => $"{Subject} {Predicate.Replace('_', ' ')} {Object}".Trim();
}

public sealed record GraphModelAnswer(string Answer, IReadOnlyList<int> FactIds, IReadOnlyList<string> FollowUps);

/// <summary>Turns a question plus retrieved facts into an answer that cites them. Implemented in the agents layer.</summary>
public interface IGraphAnswerModel
{
    /// <returns>Null when no model could answer.</returns>
    Task<GraphModelAnswer?> AnswerAsync(Guid ownerId, string question, IReadOnlyList<GraphQuestionTurn> history,
        IReadOnlyList<GraphContextFact> facts, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IGraphQuestionService
{
    Task<GraphAnswer> AskAsync(Guid ownerId, string question, IReadOnlyList<Guid> focusIds,
        IReadOnlyList<GraphQuestionTurn> history, CancellationToken cancellationToken);
}

/// <summary>
/// Answers questions about the knowledge graph. Retrieval is deterministic: entities named in the question (and the
/// user for "I/my"), entities the app has in focus, their facts and their neighbours', the shortest paths between
/// them, and facts whose wording matches the question. The model only phrases an answer from those numbered facts.
/// </summary>
public sealed class GraphQuestionService(IKnowledgeGraphRepository graph, IGraphAnswerModel model,
    TimeProvider? timeProvider = null) : IGraphQuestionService
{
    public const int MaxQuestionLength = 500;
    public const int MaxFocus = 5;
    public const int MaxHistory = 4;
    private const int MaxFacts = 160;
    private const int MaxSeeds = 6;
    private const int MaxPathHops = 5;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private static readonly HashSet<string> SelfWords = new(StringComparer.OrdinalIgnoreCase)
        { "i", "me", "my", "mine", "myself", "ik", "mij", "me", "mijn", "you", "your" };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "who", "whom", "whose", "when", "where", "which", "does", "have", "with", "about", "know", "tell",
        "there", "their", "they", "this", "that", "from", "into", "than", "then", "were", "will", "would", "should",
        "could", "much", "many", "most", "more", "some", "your", "mine", "graph", "connected", "connection", "related",
        "wat", "wie", "waar", "wanneer", "welke", "hoe", "heeft", "hebben", "over", "mijn", "zijn", "van", "met"
    };

    public async Task<GraphAnswer> AskAsync(Guid ownerId, string question, IReadOnlyList<Guid> focusIds,
        IReadOnlyList<GraphQuestionTurn> history, CancellationToken cancellationToken)
    {
        question = question.Trim();
        var overview = await graph.GetOverviewAsync(ownerId, KnowledgeGraphLimits.MaxOverviewEntities,
            cancellationToken);
        var entities = overview.Entities.ToDictionary(x => x.Id);
        if (entities.Count == 0)
            return new GraphAnswer("Your knowledge graph is still empty, so there is nothing to answer from yet.",
                [], [], [], [], false);

        var user = overview.Entities
            .Where(IsUser)
            .OrderByDescending(x => x.RelationCount)
            .FirstOrDefault();
        var context = string.Join(' ', history.TakeLast(MaxHistory).Select(turn => turn.Question).Append(question));
        var seeds = Seeds(context, question, focusIds, overview.Entities, user);

        var facts = new FactCollector(entities);
        var neighbours = new List<Guid>();
        foreach (var seed in seeds)
        {
            foreach (var edge in overview.Edges.Where(x => x.From == seed || x.To == seed).Take(40))
            {
                facts.Add(edge);
                var other = edge.From == seed ? edge.To : edge.From;
                if (!seeds.Contains(other) && other != user?.Id) neighbours.Add(other);
            }
            foreach (var literal in overview.Literals.Where(x => x.EntityId == seed).Take(40)) facts.Add(literal);
        }

        // How seeds connect: shortest paths, not through the user unless the user is an end of the path.
        for (var i = 0; i < seeds.Count; i++)
        for (var j = i + 1; j < seeds.Count && j < 4; j++)
        foreach (var edge in ShortestPath(overview.Edges, seeds[i], seeds[j], user?.Id))
            facts.Add(edge);

        // Facts whose wording matches the question ("whose birthday is in June", "who works at ...").
        var words = Words(question).Where(word => word.Length >= 4 && !StopWords.Contains(word)).ToArray();
        if (words.Length > 0)
        {
            foreach (var literal in overview.Literals.Where(x => Matches(words, x.Predicate, x.Value)).Take(40))
                facts.Add(literal);
            foreach (var edge in overview.Edges.Where(x => Matches(words, x.Predicate, null)).Take(40))
                facts.Add(edge);
        }

        foreach (var neighbour in neighbours.Distinct().Take(12))
        {
            foreach (var edge in overview.Edges.Where(x => x.From == neighbour || x.To == neighbour).Take(6))
                facts.Add(edge);
            foreach (var literal in overview.Literals.Where(x => x.EntityId == neighbour).Take(4)) facts.Add(literal);
        }

        // Ended facts answer "used to" and "what changed" questions.
        foreach (var seed in seeds.Take(3))
        {
            var details = await graph.GetEntityAsync(ownerId, seed, cancellationToken);
            if (details is null) continue;
            foreach (var relation in details.History.Where(x => x.ValidTo is not null).Take(12)) facts.Add(relation);
        }

        var list = facts.Take(MaxFacts);
        var answer = list.Count == 0
            ? null
            : await model.AnswerAsync(ownerId, question, history.TakeLast(MaxHistory).ToArray(), list,
                clock.GetUtcNow(), cancellationToken);
        if (answer is null) return Fallback(list, seeds, entities);

        var byId = list.ToDictionary(x => x.Id);
        var cited = answer.FactIds.Where(byId.ContainsKey).Distinct().Select(id => byId[id]).ToArray();
        var entityIds = new List<Guid>();
        foreach (var fact in cited)
        {
            entityIds.Add(fact.SubjectId);
            if (fact.ObjectId is { } objectId) entityIds.Add(objectId);
        }
        if (entityIds.Count == 0) entityIds.AddRange(seeds);
        return new GraphAnswer(answer.Answer,
            entityIds.Distinct().Where(entities.ContainsKey).ToArray(),
            cited.Where(x => x.ObjectId is not null && x.ValidTo is null)
                .Select(x => new GraphAnswerLink(x.SubjectId, x.ObjectId!.Value, x.Predicate)).Distinct().ToArray(),
            cited.Select(Describe).ToArray(),
            answer.FollowUps.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Take(3).ToArray(),
            true);
    }

    private static bool IsUser(GraphEntityRecord entity) =>
        GraphNames.Key(entity.Name) == GraphNames.UserKey || entity.Name == "You";

    private static List<Guid> Seeds(string context, string question, IReadOnlyList<Guid> focusIds,
        IReadOnlyList<GraphEntityRecord> entities, GraphEntityRecord? user)
    {
        var seeds = new List<Guid>();
        var known = entities.ToDictionary(x => x.Id);
        foreach (var id in focusIds.Take(MaxFocus))
            if (known.ContainsKey(id)) seeds.Add(id);

        var lower = Normalize(context);
        var mentioned = entities
            .Where(entity => !IsUser(entity))
            .Select(entity => (entity, hit: entity.Aliases.Prepend(entity.Name)
                .Where(name => name.Trim().Length >= 3)
                .Select(name => ContainsWord(lower, Normalize(name)) ? name.Length : 0)
                .DefaultIfEmpty(0).Max()))
            .Where(x => x.hit > 0)
            // Longer names first, so "Arjan Bontekoe" wins over a separate "Arjan".
            .OrderByDescending(x => x.hit).ThenByDescending(x => x.entity.RelationCount)
            .Select(x => x.entity.Id);
        seeds.AddRange(mentioned);

        if (user is not null && Words(question).Any(SelfWords.Contains)) seeds.Add(user.Id);
        if (seeds.Count == 0 && user is not null) seeds.Add(user.Id);
        return seeds.Distinct().Take(MaxSeeds).ToList();
    }

    private static IReadOnlyList<GraphEdge> ShortestPath(IReadOnlyList<GraphEdge> edges, Guid from, Guid to,
        Guid? user)
    {
        if (from == to) return [];
        var adjacent = new Dictionary<Guid, List<GraphEdge>>();
        foreach (var edge in edges)
        {
            (adjacent.TryGetValue(edge.From, out var a) ? a : adjacent[edge.From] = []).Add(edge);
            (adjacent.TryGetValue(edge.To, out var b) ? b : adjacent[edge.To] = []).Add(edge);
        }
        var previous = new Dictionary<Guid, GraphEdge?> { [from] = null };
        var frontier = new Queue<(Guid Node, int Depth)>();
        frontier.Enqueue((from, 0));
        while (frontier.Count > 0)
        {
            var (node, depth) = frontier.Dequeue();
            if (node == to) break;
            if (depth >= MaxPathHops) continue;
            if (node != from && node == user) continue;
            foreach (var edge in adjacent.GetValueOrDefault(node) ?? [])
            {
                var other = edge.From == node ? edge.To : edge.From;
                if (previous.ContainsKey(other)) continue;
                previous[other] = edge;
                frontier.Enqueue((other, depth + 1));
            }
        }
        if (!previous.ContainsKey(to)) return [];
        var path = new List<GraphEdge>();
        for (var at = to; previous[at] is { } edge; at = edge.From == at ? edge.To : edge.From) path.Add(edge);
        path.Reverse();
        return path;
    }

    private static GraphAnswer Fallback(IReadOnlyList<GraphContextFact> facts, IReadOnlyList<Guid> seeds,
        IReadOnlyDictionary<Guid, GraphEntityRecord> entities)
    {
        var current = facts.Where(x => x.ValidTo is null).Take(8).ToArray();
        if (current.Length == 0)
            return new GraphAnswer("I couldn't find anything in your knowledge graph about that.",
                seeds.Where(entities.ContainsKey).ToArray(), [], [], [], false);
        var text = new StringBuilder("Here is what your knowledge graph says:");
        foreach (var fact in current) text.Append("\n• ").Append(fact.Sentence);
        return new GraphAnswer(text.ToString(),
            current.SelectMany(x => x.ObjectId is { } id ? new[] { x.SubjectId, id } : [x.SubjectId])
                .Concat(seeds).Distinct().Where(entities.ContainsKey).ToArray(),
            current.Where(x => x.ObjectId is not null)
                .Select(x => new GraphAnswerLink(x.SubjectId, x.ObjectId!.Value, x.Predicate)).Distinct().ToArray(),
            current.Select(Describe).ToArray(), [], false);
    }

    private static string Describe(GraphContextFact fact) => fact.ValidTo is { } ended
        ? $"{fact.Sentence} (until {ended:yyyy-MM-dd})"
        : fact.Sentence;

    private static bool Matches(string[] words, string predicate, string? value)
    {
        var text = Normalize(predicate.Replace('_', ' ') + " " + value);
        return words.Any(word => ContainsWord(text, Normalize(word)) ||
                                 (word.Length >= 5 && text.Contains(Normalize(word)[..^1], StringComparison.Ordinal)));
    }

    private static IEnumerable<string> Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0);

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static bool ContainsWord(string haystack, string needle)
    {
        if (needle.Length == 0) return false;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var end = index + needle.Length;
            var after = end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]);
            if (before && after) return true;
            index = haystack.IndexOf(needle, index + 1, StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>Numbers facts in the order found and drops duplicates.</summary>
    private sealed class FactCollector(IReadOnlyDictionary<Guid, GraphEntityRecord> entities)
    {
        private readonly List<GraphContextFact> facts = [];
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);

        public void Add(GraphEdge edge)
        {
            if (!entities.TryGetValue(edge.From, out var from) || !entities.TryGetValue(edge.To, out var to)) return;
            Add(edge.From, from.Name, edge.Predicate, edge.To, to.Name, edge.ValidFrom, null);
        }

        public void Add(GraphLiteral literal)
        {
            if (!entities.TryGetValue(literal.EntityId, out var subject)) return;
            Add(literal.EntityId, subject.Name, literal.Predicate, null, literal.Value, literal.ValidFrom, null);
        }

        public void Add(GraphRelationRecord relation) =>
            Add(relation.SubjectId, relation.SubjectName, relation.Predicate, relation.ObjectId,
                relation.ObjectName ?? relation.ObjectValue ?? "", relation.ValidFrom, relation.ValidTo);

        private void Add(Guid subjectId, string subject, string predicate, Guid? objectId, string value,
            DateTimeOffset validFrom, DateTimeOffset? validTo)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var key = $"{subjectId}|{predicate}|{objectId?.ToString() ?? value}|{validTo?.ToUnixTimeSeconds()}";
            if (!seen.Add(key)) return;
            facts.Add(new GraphContextFact(facts.Count + 1, subjectId, subject, predicate, objectId, value, validFrom,
                validTo));
        }

        public IReadOnlyList<GraphContextFact> Take(int count) => facts.Take(count).ToArray();
    }
}
