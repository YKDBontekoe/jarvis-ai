using System.Text.Json;
using Jarvis.Agents.Learning;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Domain.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class MemoryQualityReviewTests
{
    [Fact]
    public void Findings_require_valid_scoped_ids_and_supported_shapes()
    {
        var memory = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var valid = new { kind = "contradiction", severity = "high", memoryIds = new[] { memory },
            entityIds = new[] { entity }, reason = "The dated update contradicts the older status.",
            recommendation = "Review the earlier setup status." };
        var unknown = valid with { memoryIds = new[] { Guid.NewGuid() } };
        var secret = valid with { reason = "ghp_abcdefghijklmnopqrstuvwxyz0123456789" };
        var invalid = valid with { kind = "execute_shell" };
        var result = MemoryQualityReviewService.Parse(JsonSerializer.Serialize(new { findings = new[]
            { valid, unknown, secret, invalid } }), new HashSet<Guid> { memory }, new HashSet<Guid> { entity });
        Assert.Single(result);
        Assert.Equal("contradiction", result[0].Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"findings\":null}")]
    public void Invalid_response_cannot_be_reported_as_a_successful_empty_review(string reply)
    {
        Assert.ThrowsAny<Exception>(() => MemoryQualityReviewService.Parse(reply,
            new HashSet<Guid>(), new HashSet<Guid>()));
    }

    [Fact]
    public async Task Review_reads_old_records_filters_other_owners_and_uses_explicit_codex_model()
    {
        var owner = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var old = new MemoryRecord(Guid.NewGuid(), owner, "fact", "An old, still-active statement.", .7f, .9f,
            "user", null, now.AddYears(-1), now.AddYears(-1), null, false);
        var other = old with { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), Content = "Other owner's fact." };
        var client = new CaptureClient("{\"findings\":[]}");
        var settings = new InMemorySettingsStore();
        var service = new MemoryQualityReviewService(new FixedChatClientResolver(client),
            Fake<IKnowledgeGraphRepository>.Create((nameof(IKnowledgeGraphRepository.GetOverviewAsync),
                _ => Task.FromResult(new GraphOverview([], [], []))),
                (nameof(IKnowledgeGraphRepository.ListEntitiesAsync),
                _ => Task.FromResult<IReadOnlyList<GraphEntityRecord>>([]))), settings,
            Options.Create(new MemoryReviewOptions()), NullLogger<MemoryQualityReviewService>.Instance);
        var result = await service.ReviewAsync(owner, [old, other], TestContext.Current.CancellationToken);
        Assert.Equal("completed", result!.Status);
        Assert.Equal(1, result.MemoryCount);
        Assert.Contains(old.Content, client.Prompt);
        Assert.DoesNotContain(other.Content, client.Prompt);
        Assert.Equal("gpt-6.1-sol", client.Options!.ModelId);
        Assert.Equal("high", client.Options.AdditionalProperties?["reasoning_effort"]);
        Assert.Equal(result, await settings.GetAsync<MemoryQualityReview>(owner,
            LearningSections.MemoryQualityReview, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Provider_failure_is_recorded_and_does_not_claim_a_clean_dataset()
    {
        var owner = Guid.NewGuid();
        var settings = new InMemorySettingsStore();
        var service = new MemoryQualityReviewService(new FixedChatClientResolver(new CaptureClient("bad reply")),
            Fake<IKnowledgeGraphRepository>.Create((nameof(IKnowledgeGraphRepository.GetOverviewAsync),
                _ => Task.FromResult(new GraphOverview([], [], []))),
                (nameof(IKnowledgeGraphRepository.ListEntitiesAsync),
                _ => Task.FromResult<IReadOnlyList<GraphEntityRecord>>([]))), settings,
            Options.Create(new MemoryReviewOptions()), NullLogger<MemoryQualityReviewService>.Instance);
        var result = await service.ReviewAsync(owner, [], TestContext.Current.CancellationToken);
        Assert.Equal("failed", result!.Status);
        Assert.True(result.Partial);
    }

    [Fact]
    public async Task Disabled_review_does_not_resolve_models_or_read_graph_data()
    {
        var service = new MemoryQualityReviewService(Fake<IChatClientResolver>.Create(),
            Fake<IKnowledgeGraphRepository>.Create(), new InMemorySettingsStore(),
            Options.Create(new MemoryReviewOptions { Enabled = false }),
            NullLogger<MemoryQualityReviewService>.Instance);
        Assert.Null(await service.ReviewAsync(Guid.NewGuid(), [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Unknown_ids_cannot_turn_an_invalid_review_into_a_clean_bill_of_health()
    {
        var reply = JsonSerializer.Serialize(new { findings = new[] { new { kind = "identity", severity = "high",
            memoryIds = new[] { Guid.NewGuid() }, entityIds = Array.Empty<Guid>(),
            reason = "A finding.", recommendation = "Review it." } } });
        Assert.Throws<JsonException>(() => MemoryQualityReviewService.Parse(reply,
            new HashSet<Guid>(), new HashSet<Guid>()));
    }

    private sealed class CaptureClient(string reply) : IChatClient
    {
        public string Prompt { get; private set; } = "";
        public ChatOptions? Options { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Prompt = string.Join("\n", messages.Select(m => m.Text)); Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type t, object? k = null) => null;
        public void Dispose() { }
    }
}
