using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Application.Profiles;
using Jarvis.Domain.Conversations;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Memory;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Jarvis.IntegrationTests;

public sealed class MemoryCorrectionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateDbContext();
        await database.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    private static MemoryReviewService Review(JarvisDbContext database, out MemoryService memories)
    {
        var repository = new MemoryRepository(database);
        memories = new MemoryService(repository);
        return new MemoryReviewService(memories, repository, new ConversationStore(database),
            new NoProfiles());
    }

    [Fact]
    public async Task A_correction_changes_what_search_finds_and_keeps_the_old_version_as_history()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var review = Review(database, out var memories);
        var old = await memories.CreateAsync(owner, "routine", "Rosa visits on Wednesdays", 0.6f, 0.8f, null, false,
            CancellationToken.None, sourceType: "user");

        var result = await review.CorrectAsync(owner, old.Id, "Rosa visits on Thursdays now", CancellationToken.None);

        Assert.Equal(MemoryCorrectionOutcome.Corrected, result.Outcome);
        var now = await memories.SearchAsync(owner, "Rosa visits", CancellationToken.None);
        Assert.Equal([result.Memory!.Id], now.Select(hit => hit.Memory.Id));
        Assert.Equal("Rosa visits on Thursdays now", now[0].Memory.Content);
        Assert.Equal("user", result.Memory.SourceType);

        var history = await review.HistoryAsync(owner, result.Memory.Id, CancellationToken.None);
        var previous = Assert.Single(history);
        Assert.Equal(old.Id, previous.Id);
        Assert.Equal("Rosa visits on Wednesdays", previous.Content);
        Assert.NotNull(previous.ReplacedAt);
        // The replaced memory is not current any more, so it cannot be corrected or ended again.
        Assert.Equal(MemoryCorrectionOutcome.NotCurrent,
            (await review.CorrectAsync(owner, old.Id, "Rosa visits on Fridays", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Corrections_chain_and_other_owners_cannot_touch_or_read_them()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var review = Review(database, out var memories);
        var first = await memories.CreateAsync(owner, "fact", "The boiler is from 2010", 0.6f, 0.8f, null, false,
            CancellationToken.None, sourceType: "user");
        var second = (await review.CorrectAsync(owner, first.Id, "The boiler is from 2012", CancellationToken.None)).Memory!;
        var third = (await review.CorrectAsync(owner, second.Id, "The boiler is from 2014", CancellationToken.None)).Memory!;

        Assert.Equal(["The boiler is from 2012", "The boiler is from 2010"],
            (await review.HistoryAsync(owner, third.Id, CancellationToken.None)).Select(item => item.Content));
        Assert.Equal(MemoryCorrectionOutcome.NotFound,
            (await review.CorrectAsync(other, third.Id, "Stolen", CancellationToken.None)).Outcome);
        Assert.Equal(MemoryCorrectionOutcome.NotFound,
            (await review.ExpireAsync(other, third.Id, CancellationToken.None)).Outcome);
        Assert.Empty(await review.HistoryAsync(other, third.Id, CancellationToken.None));
        Assert.Empty(await review.GetReviewAsync(other, CancellationToken.None));
    }

    [Fact]
    public async Task Pinned_memories_are_refused_and_ending_one_removes_it_from_search()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var review = Review(database, out var memories);
        var pinned = await memories.CreateAsync(owner, "preference", "Always answer in Dutch", 0.9f, 0.95f, null, true,
            CancellationToken.None, sourceType: "user");
        var plain = await memories.CreateAsync(owner, "fact", "Lives near the harbour", 0.6f, 0.8f, null, false,
            CancellationToken.None, sourceType: "user");

        Assert.Equal(MemoryCorrectionOutcome.Pinned,
            (await review.CorrectAsync(owner, pinned.Id, "Answer in English", CancellationToken.None)).Outcome);
        Assert.Equal("Always answer in Dutch", (await memories.GetAsync(pinned.Id, owner, CancellationToken.None))!.Content);

        Assert.Equal(MemoryCorrectionOutcome.Corrected, (await review.ExpireAsync(owner, plain.Id, CancellationToken.None)).Outcome);
        Assert.Empty(await memories.SearchAsync(owner, "harbour", CancellationToken.None));
    }

    [Fact]
    public async Task The_review_lists_recently_learned_memories_with_their_source_chat()
    {
        var owner = Guid.CreateVersion7();
        await using var database = CreateDbContext();
        var review = Review(database, out var memories);
        var store = new ConversationStore(database);
        var chat = await store.CreateAsync(owner, "Garden talk", CancellationToken.None);
        var message = new Message(chat.Id, "user", "I planted tulips");
        await store.AddMessageAsync(message, CancellationToken.None);
        await memories.CreateAsync(owner, "fact", "Planted tulips this autumn", 0.6f, 0.9f, null, false,
            CancellationToken.None, sourceType: "conversation", sourceId: message.Id);
        await memories.CreateAsync(owner, "fact", "Wrote this myself", 0.6f, 0.9f, null, false,
            CancellationToken.None, sourceType: "user");

        var entry = Assert.Single(await review.GetReviewAsync(owner, CancellationToken.None));

        Assert.Equal(["recent"], entry.Reasons);
        Assert.Equal(chat.Id, entry.Source.ConversationId);
        Assert.Equal(message.Id, entry.Source.MessageId);
        Assert.Contains("Garden talk", entry.Source.Label);
    }

    /// <summary>The review only lists profile names; these tests have none.</summary>
    private sealed class NoProfiles : IAssistantProfileService
    {
        public Task<IReadOnlyList<AssistantProfileRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssistantProfileRecord>>([]);
        public Task<AssistantProfileRecord> EnsureDefaultAsync(Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AssistantProfileRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AssistantProfileRecord> CreateAsync(Guid ownerId, AssistantProfileDraft draft,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AssistantProfileRecord?> UpdateAsync(Guid id, Guid ownerId, AssistantProfileDraft draft,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ProfileBinding> CaptureBindingAsync(Guid ownerId, Guid? profileId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AssistantProfileSnapshot> ResolveSnapshotAsync(ProfileBinding? binding, Guid ownerId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ProfileScopeChange CompareScope(AssistantProfileSnapshot current, AssistantProfileSnapshot next) =>
            throw new NotSupportedException();
    }

    private JarvisDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options);
}
