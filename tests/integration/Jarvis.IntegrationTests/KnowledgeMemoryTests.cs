using Jarvis.Application.Memory;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;
using Jarvis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class KnowledgeMemoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Exclusive_facts_close_the_previous_value_and_keep_history()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var graph = new KnowledgeGraphRepository(database);
        var january = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var june = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await graph.MergeAsync(owner, [Fact("user", "lives_in", "Utrecht", january, exclusive: true),
            Fact("user", "likes", "jazz", january, exclusive: false)], null, CancellationToken.None);
        await graph.MergeAsync(owner, [Fact("Me", "lives_in", "Amsterdam", june, exclusive: true),
            Fact("user", "likes", "sourdough", june, exclusive: false),
            Fact("user", "lives_in", "Amsterdam", june, exclusive: true)], null, CancellationToken.None);

        var now = await graph.FindEntityAsync(owner, "user", null, CancellationToken.None);
        Assert.NotNull(now);
        Assert.Equal("You", now.Entity.Name);
        Assert.Equal(["Amsterdam", "jazz", "sourdough"],
            now.Current.Select(relation => relation.ObjectName!).Order());
        var moved = Assert.Single(now.History);
        Assert.Equal("Utrecht", moved.ObjectName);
        Assert.Equal(june, moved.ValidTo);

        var march = await graph.FindEntityAsync(owner, "user", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
        Assert.Contains(march!.Current, relation => relation.ObjectName == "Utrecht");
        Assert.DoesNotContain(march.Current, relation => relation.ObjectName == "Amsterdam");

        Assert.Null(await graph.FindEntityAsync(Guid.CreateVersion7(), "user", null, CancellationToken.None));
    }

    [Fact]
    public async Task Mentions_aliases_overview_and_superseded_memories_stay_consistent()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var graph = new KnowledgeGraphRepository(database);
        var memories = new MemoryRepository(database);
        var memory = await memories.CreateAsync(owner, "relationship", "My sister Anna works at Philips.", 0.7f, 0.9f,
            "user", null, null, false, CancellationToken.None);
        await graph.MergeAsync(owner,
        [
            new GraphFact("user", "person", "has_sister", "Anna", "person", true, false, DateTimeOffset.UtcNow, 0.9f),
            new GraphFact("Anna", "person", "works_at", "Philips", "organization", true, true, DateTimeOffset.UtcNow, 0.9f),
            new GraphFact("Anna", "person", "role", "engineer", null, false, true, DateTimeOffset.UtcNow, 0.8f),
            new GraphFact("Annie", "person", "nickname_of", "Anna", "person", true, false, DateTimeOffset.UtcNow, 0.5f)
        ], memory.Id, CancellationToken.None);

        var mentioned = await graph.FindMentionedAsync(owner, "What does anna do at philips?", 5, CancellationToken.None);
        Assert.Equal(["Anna", "Philips"], mentioned.Select(entity => entity.Name).Order());
        var overview = await graph.GetOverviewAsync(owner, 10, CancellationToken.None);
        var worksAt = Assert.Single(overview.Edges, edge => edge.Predicate == "works_at");
        Assert.True(worksAt.Confidence >= 0.9f);
        Assert.Contains(overview.Literals, literal => literal.Predicate == "role" && literal.Value == "engineer");

        await memories.ReplaceAsync(memory.Id, owner, "relationship", "My sister Anna now works at ASML.", 0.7f, 0.9f,
            "user", null, CancellationToken.None);
        var anna = await graph.FindEntityAsync(owner, "Anna", null, CancellationToken.None);
        Assert.DoesNotContain(anna!.Current, relation => relation.ObjectName == "Philips");
        Assert.Contains(anna.History, relation => relation.ObjectName == "Philips" && relation.ValidTo is not null);

        await memories.DeleteAsync(memory.Id, owner, CancellationToken.None);
        var afterDelete = await graph.FindEntityAsync(owner, "Anna", null, CancellationToken.None);
        Assert.Empty(afterDelete!.Current.Concat(afterDelete.History));
    }

    [Fact]
    public async Task Semantic_search_ranks_by_cosine_similarity_within_the_same_model()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var memories = new MemoryRepository(database);
        var index = new MemoryIndexRepository(database);
        var bread = await memories.CreateAsync(owner, "fact", "The user bakes sourdough.", 0.5f, 0.9f, "user", null,
            null, false, CancellationToken.None);
        var jazz = await memories.CreateAsync(owner, "fact", "The user likes jazz.", 0.5f, 0.9f, "user", null, null,
            false, CancellationToken.None);
        var otherModel = await memories.CreateAsync(owner, "fact", "Stored with another model.", 0.5f, 0.9f, "user",
            null, null, false, CancellationToken.None);
        await index.SetEmbeddingAsync(bread.Id, owner, new MemoryEmbedding("m1", Vector(1, 0)), CancellationToken.None);
        await index.SetEmbeddingAsync(jazz.Id, owner, new MemoryEmbedding("m1", Vector(0, 1)), CancellationToken.None);
        await index.SetEmbeddingAsync(otherModel.Id, owner, new MemoryEmbedding("m2", Vector(1, 0)),
            CancellationToken.None);

        var hits = await index.SearchSemanticAsync(owner, new MemoryEmbedding("m1", Vector(0.9f, 0.1f)), null, 0.3, 10,
            CancellationToken.None);

        Assert.Equal([bread.Id], hits.Select(hit => hit.Id));
        Assert.Equal(1, (await index.GetStatusAsync(owner, CancellationToken.None)).Active - 2);
        Assert.Single(await index.ListNeedingEmbeddingAsync(owner, "m1", 10, CancellationToken.None));
    }

    [Fact]
    public async Task Skills_keep_revisions_and_settings_round_trip_as_json()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var skills = new SkillRepository(database);
        var settings = new OwnerSettingsStore(database);
        var draft = new SkillDraft("weekly-review", "Run the Friday weekly review.", "1. List wins.\n2. List blockers.");

        var first = await skills.UpsertAsync(owner, draft, SkillSources.Learned, SkillStatuses.Active, true, "first",
            CancellationToken.None);
        var improved = await skills.UpsertAsync(owner, draft with { Instructions = "1. Wins.\n2. Blockers.\n3. Plan." },
            SkillSources.Learned, SkillStatuses.Active, true, "better", CancellationToken.None);
        await skills.SetLockedAsync(first!.Id, owner, true, CancellationToken.None);
        var blocked = await skills.UpsertAsync(owner, draft, SkillSources.Learned, SkillStatuses.Active, true, "blocked",
            CancellationToken.None);
        await settings.SaveAsync(owner, SettingsSections.Learning, new LearningSettings(true, 30), CancellationToken.None);
        await settings.SaveAsync(owner, SettingsSections.Learning, new LearningSettings(false, 45), CancellationToken.None);

        Assert.Equal(2, improved!.Version);
        Assert.Null(blocked);
        Assert.Equal([2, 1], (await skills.ListRevisionsAsync(first.Id, owner, CancellationToken.None))
            .Select(revision => revision.Version));
        Assert.Equal(new LearningSettings(false, 45),
            await settings.GetAsync<LearningSettings>(owner, SettingsSections.Learning, CancellationToken.None));
        Assert.Equal([owner], await settings.ListOwnersAsync(SettingsSections.Learning, CancellationToken.None));
    }

    private static GraphFact Fact(string subject, string predicate, string target, DateTimeOffset validFrom,
        bool exclusive) => new(subject, "person", predicate, target, "place", true, exclusive, validFrom, 0.9f);

    private static float[] Vector(float x, float y)
    {
        var vector = new float[1536];
        vector[0] = x;
        vector[1] = y;
        return vector;
    }

    private JarvisDbContext CreateDbContext() => new(new DbContextOptionsBuilder<JarvisDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
        .Options);
}
