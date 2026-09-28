using Jarvis.Agents.Learning;
using Jarvis.Application.Conversations;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Application.Persona;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class DreamingTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000d11d");
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Jaccard_treats_near_paraphrases_as_related_and_noise_as_distinct()
    {
        Assert.True(DreamingRanker.Jaccard(
            "The user lives in Amsterdam.", "The user lives in Amsterdam now.") >= DreamingRanker.RelatedJaccard);
        Assert.True(DreamingRanker.Jaccard("lives in Amsterdam", "xyz") < DreamingRanker.MentionJaccard);
        Assert.Equal("theuserlivesinamsterdam", DreamingRanker.Canonical("The user lives in Amsterdam."));
    }

    [Fact]
    public void Deep_ranking_promotes_repeated_recent_facts_and_rejects_one_offs()
    {
        var repeated = Candidate(signals: 3, sources: 3, days: 4, importance: 0.9f, confidence: 0.95f);
        var oneOff = Candidate(signals: 1, sources: 1, days: 0, importance: 0.4f, confidence: 0.5f);

        var repeatedScore = DreamingRanker.Score(repeated, Now);
        var oneOffScore = DreamingRanker.Score(oneOff, Now);

        Assert.True(repeatedScore >= DreamingRanker.MinScore);
        Assert.True(DreamingRanker.PassesPromotionGate(repeated, repeatedScore));
        Assert.False(DreamingRanker.PassesPromotionGate(oneOff, oneOffScore));
    }

    [Fact]
    public void Light_sleep_clusters_duplicate_memories_and_counts_chat_mentions()
    {
        var first = Memory("fact", "The user lives in Amsterdam.", 0.8f, Now.AddDays(-10));
        var duplicate = Memory("fact", "The user lives in Amsterdam", 0.7f, Now.AddDays(-1));
        var messages = new[]
        {
            User("The user lives in Amsterdam still.", Now.AddDays(-2)),
            User("The user lives in Amsterdam still.", Now.AddHours(-5))
        };

        var staged = DreamingRanker.Stage([first, duplicate], messages, [
            new MemoryRecallRecord(first.Id, 2, 2, Now.AddHours(-1))
        ], Now);

        var home = Assert.Single(staged, item => item.Kind == "fact");
        Assert.Equal(first.Id, home.MemoryId);
        Assert.Contains(duplicate.Id, home.DuplicateIds);
        Assert.True(home.SignalCount >= 4);
        Assert.True(home.LightBoost > 0);
    }

    [Theory]
    [InlineData(2, 60)]
    [InlineData(3, 1440)]
    [InlineData(23, 240)]
    public void Next_dream_is_clamped_until_the_configured_local_hour(int utcHour, int expected)
    {
        var now = new DateTimeOffset(2026, 9, 27, utcHour, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, DreamingClock.MinutesUntilNext(now, 3, "UTC"));
    }

    [Fact]
    public void Dreaming_hour_is_bounded()
    {
        Assert.Throws<ArgumentException>(() => new LearningSettings(DreamingHour: 24).Normalize());
    }

    [Fact]
    public void Parse_tolerates_fences_and_ignores_garbage()
    {
        var parsed = DreamingService.Parse("""
            ```json
            {"themes":["travel"],"persona":[{"category":"tone","statement":"Be direct and skip small talk.","confidence":0.9}],
             "memories":[{"action":"merge","kind":"fact","content":"The user lives in Amsterdam.","importance":0.8,"confidence":0.95}],
             "facts":[{"subject":"user","subjectType":"person","predicate":"lives_in","object":"Amsterdam","objectIsEntity":true,"exclusive":true}],
             "diary":"A quiet night of sorting."}
            ```
            """);
        Assert.Equal("travel", Assert.Single(parsed.Themes!));
        Assert.Equal("Be direct and skip small talk.", Assert.Single(parsed.Persona!).Statement);
        Assert.Equal("merge", Assert.Single(parsed.Memories!).Action);
        Assert.Equal("Amsterdam", Assert.Single(parsed.Facts!).Object);
        Assert.Null(DreamingService.Parse("I could not dream.").Memories);
    }

    [Fact]
    public async Task Sweep_merges_rewrites_tone_and_facts_and_skips_secrets()
    {
        var settings = new InMemorySettingsStore();
        await settings.SaveAsync(Owner, SettingsSections.Learning, LearningSettings.Default, default);
        var persona = new PersonaService(settings);
        var store = new DreamMemoryStore();
        var existing = store.Add(Owner, "fact", "The user lives in Amsterdam.", 0.7f, 0.9f, Now.AddDays(-20));
        store.Add(Owner, "fact", "The user lives in Amsterdam", 0.6f, 0.85f, Now.AddDays(-2));
        var graph = new RecordingGraph();
        var notifications = new RecordingNotifications();
        var reply = """
            {"themes":["home"],
             "persona":[{"category":"tone","statement":"Be direct and skip small talk.","confidence":0.9},
                        {"category":"tone","statement":"Use token ghp_abcdefghijklmnopqrstuvwxyz0123456789","confidence":0.9}],
             "memories":[{"action":"merge","kind":"fact","content":"The user lives in Amsterdam.","importance":0.85,"confidence":0.95,"targetMemoryId":"TARGET"},
                         {"action":"add","kind":"fact","content":"Weak one-off guess.","importance":0.4,"confidence":0.4}],
             "facts":[{"subject":"user","subjectType":"person","predicate":"lives_in","object":"Amsterdam","objectIsEntity":true,"exclusive":true},
                      {"subject":"user","predicate":"token","object":"ghp_abcdefghijklmnopqrstuvwxyz0123456789"}],
             "diary":"I folded the day's notes into a quieter map of home."}
            """.Replace("TARGET", existing.Id.ToString());
        var service = Create(settings, persona, store, graph, notifications, reply);

        var outcome = await service.SweepAsync(Owner, force: true, default);

        Assert.False(outcome.Skipped);
        Assert.Equal(1, outcome.Merged);
        Assert.Equal(1, outcome.Deduplicated);
        Assert.Equal(1, outcome.PersonaUpdated);
        Assert.Equal(1, outcome.FactsMerged);
        Assert.Equal(0, outcome.Promoted);
        Assert.Equal("Be direct and skip small talk.", Assert.Single((await persona.GetAsync(Owner, default)).TraitList).Statement);
        Assert.Equal("lives_in", Assert.Single(graph.Facts).Predicate);
        Assert.Contains(notifications.Created, item => item.Type == "learning.dreamed");
        Assert.Contains(outcome.Diary, entry => entry.Phase == "diary");
        Assert.Contains("merged", outcome.Summary);
        Assert.Contains("duplicate", outcome.Summary);
    }

    [Fact]
    public async Task Sweep_skips_the_model_when_there_is_nothing_to_stage()
    {
        var client = new CountingReplyClient("{}");
        var settings = new InMemorySettingsStore();
        var service = Create(settings, new PersonaService(settings), new DreamMemoryStore(), new RecordingGraph(),
            new RecordingNotifications(), client);

        var outcome = await service.SweepAsync(Owner, force: true, default);

        Assert.True(outcome.Skipped);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task Forced_sweep_is_rate_limited_without_force()
    {
        var settings = new InMemorySettingsStore();
        await settings.SaveAsync(Owner, LearningSections.DreamingState,
            new DreamingState(Now.AddMinutes(-2), "deep", "Already ran."), default);
        var clock = new FrozenClock(Now);
        var service = Create(settings, new PersonaService(settings), new DreamMemoryStore(), new RecordingGraph(),
            new RecordingNotifications(), "{}", clock: clock);

        var skipped = await service.SweepAsync(Owner, force: false, default);
        Assert.True(skipped.Skipped);
        Assert.Contains("recently", skipped.Summary);
    }

    [Fact]
    public void Recall_tracker_hashes_queries_and_counts_unique_lookups()
    {
        var tracker = new MemoryRecallTracker();
        var memoryId = Guid.CreateVersion7();
        tracker.Record(Owner, memoryId, "Where do I live?");
        tracker.Record(Owner, memoryId, "where  do I live?");
        tracker.Record(Owner, memoryId, "Amsterdam home");

        var snapshot = Assert.Single(tracker.Snapshot(Owner));
        Assert.Equal(3, snapshot.Hits);
        Assert.Equal(2, snapshot.UniqueQueries);
        Assert.Equal(MemoryRecallTracker.Hash("Where do I live?"), MemoryRecallTracker.Hash("where  do I live?"));
    }

    private static DreamingService Create(InMemorySettingsStore settings, PersonaService persona,
        DreamMemoryStore memories, RecordingGraph graph, RecordingNotifications notifications, string reply,
        FrozenClock? clock = null) =>
        Create(settings, persona, memories, graph, notifications, new CountingReplyClient(reply), clock);

    private static DreamingService Create(InMemorySettingsStore settings, PersonaService persona,
        DreamMemoryStore memories, RecordingGraph graph, RecordingNotifications notifications,
        CountingReplyClient client, FrozenClock? clock = null) =>
        new(Fake<IConversationHistory>.Create(("ListRecentMessagesAsync", _ => (IReadOnlyList<Message>)[])),
            memories, persona, graph, settings, notifications, new NullAudit(), new FixedChatClientResolver(client),
            new MemoryRecallTracker(), NullLogger<DreamingService>.Instance, clock ?? new FrozenClock(Now));

    private static DreamCandidate Candidate(int signals, int sources, int days, float importance, float confidence) =>
        new("userlivesinamsterdam", "fact", "The user lives in Amsterdam.", Guid.NewGuid(), null, false,
            importance, confidence, Now.AddDays(-days), Now, signals, sources, days,
            importance * confidence, DreamingRanker.Richness("fact", "The user lives in Amsterdam."), 0.04, 0.04);

    private static MemoryRecord Memory(string kind, string content, float importance, DateTimeOffset at) =>
        new(Guid.NewGuid(), Owner, kind, content, importance, 0.9f, "conversation", null, at, at, null, false);

    private static Message User(string content, DateTimeOffset _) =>
        new(Guid.NewGuid(), "user", content);

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingReplyClient(string reply) : IChatClient
    {
        public int Calls { get; private set; }
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class DreamMemoryStore : IMemoryService
    {
        public List<MemoryRecord> Items { get; } = [];

        public MemoryRecord Add(Guid ownerId, string kind, string content, float importance, float confidence,
            DateTimeOffset at)
        {
            var record = new MemoryRecord(Guid.NewGuid(), ownerId, kind, content, importance, confidence, "conversation",
                null, at, at, null, false);
            Items.Add(record);
            return record;
        }

        public Task<MemoryRecord> CreateAsync(Guid ownerId, string kind, string content, float importance, float confidence,
            DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken, string sourceType = "user",
            Guid? sourceId = null)
        {
            var record = new MemoryRecord(Guid.NewGuid(), ownerId, kind, content, importance, confidence, sourceType,
                sourceId, Now, Now, validUntil, isPinned);
            Items.Add(record);
            return Task.FromResult(record);
        }

        public Task<MemoryRecord?> ReplaceAsync(Guid existingId, Guid ownerId, string kind, string content,
            float importance, float confidence, CancellationToken cancellationToken, string sourceType = "conversation",
            Guid? sourceId = null)
        {
            var index = Items.FindIndex(item => item.Id == existingId && item.OwnerId == ownerId && !item.IsPinned);
            if (index < 0) return Task.FromResult<MemoryRecord?>(null);
            var previous = Items[index];
            Items[index] = previous with { ValidUntil = Now, UpdatedAt = Now };
            var replacement = new MemoryRecord(Guid.NewGuid(), ownerId, kind, content, importance, confidence, sourceType,
                sourceId ?? previous.SourceId, Now, Now, null, false);
            Items.Add(replacement);
            return Task.FromResult<MemoryRecord?>(replacement);
        }

        public Task<MemoryRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(item => item.Id == id && item.OwnerId == ownerId));

        public Task<IReadOnlyList<MemoryRecord>> ListAsync(Guid ownerId, string? kind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>(Items.Where(item => item.OwnerId == ownerId &&
                (kind is null || item.Kind == kind) && (item.ValidUntil is null || item.ValidUntil > Now)).ToArray());

        public Task<IReadOnlyList<MemoryRecord>> ListPinnedAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>(Items.Where(item => item.IsPinned).ToArray());

        public Task DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            Items.RemoveAll(item => item.Id == id && item.OwnerId == ownerId);
            return Task.CompletedTask;
        }

        public Task<MemoryRecord?> UpdateAsync(Guid id, Guid ownerId, string kind, string content, float importance,
            float confidence, DateTimeOffset? validUntil, bool isPinned, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MemorySearchHit>> SearchAsync(Guid ownerId, string query,
            CancellationToken cancellationToken, string? kind = null) =>
            Task.FromResult<IReadOnlyList<MemorySearchHit>>([]);
    }

    private sealed class RecordingGraph : IKnowledgeGraphRepository
    {
        public List<GraphFact> Facts { get; } = [];

        public Task<int> MergeAsync(Guid ownerId, IReadOnlyList<GraphFact> facts, Guid? sourceMemoryId,
            CancellationToken cancellationToken)
        {
            Facts.AddRange(facts);
            return Task.FromResult(facts.Count);
        }

        public Task<IReadOnlyList<GraphEntityRecord>> ListEntitiesAsync(Guid ownerId, string? search, int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GraphEntityRecord>>([]);
        public Task<GraphEntityDetails?> GetEntityAsync(Guid ownerId, Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult<GraphEntityDetails?>(null);
        public Task<GraphEntityDetails?> FindEntityAsync(Guid ownerId, string name, DateTimeOffset? asOf,
            CancellationToken cancellationToken) => Task.FromResult<GraphEntityDetails?>(null);
        public Task<IReadOnlyList<GraphEntityRecord>> FindMentionedAsync(Guid ownerId, string text, int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GraphEntityRecord>>([]);
        public Task<GraphOverview> GetOverviewAsync(Guid ownerId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new GraphOverview([], [], []));
        public Task<bool> DeleteEntityAsync(Guid ownerId, Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
