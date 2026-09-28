using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Usage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class UsageDashboardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Personalization_starts_new_and_rises_with_memory()
    {
        var empty = PersonalizationLevel.Score(new PersonalizationInputs(0, 0, 0, 0, false, false, 0));
        Assert.Equal(0, empty.Score);
        Assert.Equal("New", empty.Band);

        var familiar = PersonalizationLevel.Score(new PersonalizationInputs(10, 0, 0, 0, false, false, 0));
        Assert.InRange(familiar.Score, 35, 59);
        Assert.Equal("Familiar", familiar.Band);

        var close = PersonalizationLevel.Score(new PersonalizationInputs(10, 1, 3, 2, true, false, 1));
        Assert.Equal("Close", close.Band);
        Assert.Contains("10 active memories", close.Summary);

        var deep = PersonalizationLevel.Score(new PersonalizationInputs(80, 10, 8, 12, true, true, 20, 4));
        Assert.Equal(100, deep.Score);
        Assert.Equal("Deep", deep.Band);
        Assert.Equal(4, deep.SupersededMemories);
    }

    [Fact]
    public void OpenRouter_cost_uses_published_per_million_prices()
    {
        var price = new TokenPrice(3m, 15m);
        Assert.Equal(4.5m, UsageCost.Estimate(1_000_000, 100_000, price));
        Assert.Equal(0.0015m, UsageCost.Estimate(500, 0, price));
        Assert.Null(UsageCost.Estimate(10, 10, null));
        Assert.Null(UsageCost.Estimate(-1, 1, price));
    }

    [Fact]
    public void Period_today_starts_at_the_owner_local_midnight()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var start = UsagePeriods.Start(UsagePeriods.Today, Now, zone);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.FromHours(2)), start);
        Assert.Equal(Now.AddDays(-7), UsagePeriods.Start(UsagePeriods.SevenDays, Now, zone));
        Assert.Equal(DateTimeOffset.UnixEpoch, UsagePeriods.Start(UsagePeriods.All, Now, zone));
        Assert.False(UsagePeriods.TryNormalize("year", out _));
        Assert.True(UsagePeriods.TryNormalize(null, out var period));
        Assert.Equal(UsagePeriods.SevenDays, period);
    }

    [Fact]
    public void Composer_splits_providers_and_leaves_codex_unpriced()
    {
        var dashboard = UsageDashboardComposer.Compose(new UsageComposeInput(
            Now, UsagePeriods.SevenDays, "UTC",
            [
                new UsageModelAggregate(UsageProviders.Codex, UsagePurposes.Chat, "gpt-5.4", 2, 2,
                    1_000, 400, 100, 50, 0, 0, 3, 2_000),
                new UsageModelAggregate(UsageProviders.OpenRouter, UsagePurposes.Background, "anthropic/claude", 1, 1,
                    2_000_000, 0, 0, 0, 6m, 0, 0, 800),
                new UsageModelAggregate(UsageProviders.OpenRouter, UsagePurposes.Embedding, "openai/embed", 4, 3,
                    100, 0, 0, 0, 0, 1, 0, 400)
            ],
            [
                new UsagePoint(UsageProviders.Codex, 1_400, 0, Now.AddHours(-2)),
                new UsagePoint(UsageProviders.OpenRouter, 2_000_000, 6m, Now.AddDays(-1))
            ],
            [
                new ProviderLifetime(UsageProviders.Codex, 5, 4_000, 1_000, 0, 0, 7),
                new ProviderLifetime(UsageProviders.OpenRouter, 9, 3_000_000, 10, 6m, 1, 0)
            ],
            Activity(),
            PersonalizationLevel.Score(new PersonalizationInputs(0, 0, 0, 0, false, false, 0))));

        Assert.Equal(1_400, dashboard.Codex.TotalTokens);
        Assert.Null(dashboard.Codex.EstimatedCostUsd);
        Assert.Equal(5, dashboard.Codex.LifetimeCalls);
        Assert.Equal(3, dashboard.Codex.WebSearchActions);
        Assert.Contains("ChatGPT", dashboard.Codex.CostNote);
        Assert.Equal(6m, dashboard.OpenRouter.EstimatedCostUsd);
        Assert.Equal(1, dashboard.OpenRouter.UnpricedCalls);
        Assert.Contains("could not be priced", dashboard.OpenRouter.CostNote);
        Assert.Equal(7, dashboard.Activity.WebSearches.Total);
        Assert.Equal(3, dashboard.Activity.WebSearches.InPeriod);
        Assert.Equal(2, dashboard.Models.Count(row => row.Provider == UsageProviders.OpenRouter));
        Assert.Contains(dashboard.Daily, day => day.Day == new DateOnly(2026, 9, 28) && day.CodexTokens == 1_400);
        Assert.Null(dashboard.Other);
    }

    [Fact]
    public void Composer_caps_the_all_time_chart_at_62_days()
    {
        var points = Enumerable.Range(0, 100)
            .Select(day => new UsagePoint(UsageProviders.Codex, 10, 0, Now.AddDays(-day)))
            .ToArray();
        var dashboard = UsageDashboardComposer.Compose(new UsageComposeInput(
            Now, UsagePeriods.All, "UTC", [], points, [], Activity(),
            PersonalizationLevel.Score(new PersonalizationInputs(0, 0, 0, 0, false, false, 0))));

        Assert.Equal(62, dashboard.Daily.Count);
        Assert.Equal(new DateOnly(2026, 9, 28), dashboard.Daily[^1].Day);
        Assert.Contains("62 days", dashboard.ChartNote);
    }

    [Fact]
    public async Task Recording_client_stores_openrouter_tokens_and_estimated_cost()
    {
        var recorder = new ListRecorder();
        var usage = new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 2_000 };
        usage.AdditionalCounts ??= [];
        usage.AdditionalCounts["cachedInputTokens"] = 25;
        usage.AdditionalCounts["webSearchActions"] = 2;
        var client = new UsageRecordingChatClient(
            new UsageClient(usage), recorder, new FixedPrice(new TokenPrice(3m, 15m)),
            Guid.CreateVersion7(), UsageProviders.OpenRouter, UsagePurposes.Chat, NullLogger.Instance);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("ok", response.Text);
        var draft = recorder.Drafts.Single();
        Assert.Equal(1_000_000, draft.InputTokens);
        Assert.Equal(2_000, draft.OutputTokens);
        Assert.Equal(25, draft.CachedInputTokens);
        Assert.Equal(2, draft.WebSearchActions);
        Assert.Equal(3.03m, draft.EstimatedCostUsd);
        Assert.Equal("anthropic/claude", draft.Model);
        Assert.Equal(UsageOutcomes.Completed, draft.Outcome);
    }

    [Fact]
    public async Task Recording_client_keeps_a_failed_call_without_a_price()
    {
        var recorder = new ListRecorder();
        var client = new UsageRecordingChatClient(
            new UsageClient(null, fail: true), recorder, new FixedPrice(new TokenPrice(3m, 15m)),
            Guid.CreateVersion7(), UsageProviders.Codex, UsagePurposes.Vision, NullLogger.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

        var draft = recorder.Drafts.Single();
        Assert.Equal(UsageOutcomes.Failed, draft.Outcome);
        Assert.Equal(0, draft.InputTokens);
        Assert.Null(draft.EstimatedCostUsd);
        Assert.Equal(UsagePurposes.Vision, draft.Purpose);
    }

    [Fact]
    public async Task Recording_client_reads_usage_from_the_stream()
    {
        var recorder = new ListRecorder();
        var usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 4 };
        var client = new UsageRecordingChatClient(
            new UsageClient(usage, streamUsage: true), recorder, new FixedPrice(null),
            Guid.CreateVersion7(), UsageProviders.Codex, UsagePurposes.Chat, NullLogger.Instance);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            updates.Add(update);

        Assert.NotEmpty(updates);
        Assert.Equal(16, recorder.Drafts.Single().InputTokens + recorder.Drafts.Single().OutputTokens);
        Assert.Null(recorder.Drafts.Single().EstimatedCostUsd);
    }

    [Fact]
    public void Reader_prefers_dedicated_cached_counts_over_duplicate_additional_counts()
    {
        var usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 0, TotalTokenCount = 10 };
        usage.CachedInputTokenCount = 4;
        usage.AdditionalCounts ??= [];
        usage.AdditionalCounts["cachedInputTokens"] = 99;
        usage.AdditionalCounts["reasoningOutputTokens"] = 3;

        var read = ModelUsageReader.Read(usage);

        Assert.Equal(4, read.CachedInputTokens);
        Assert.Equal(3, read.ReasoningOutputTokens);
    }

    private static UsageActivity Activity() => new(
        new CountWindow(4, 10), new CountWindow(4, 9), new CountWindow(1, 3), new CountWindow(1, 2),
        new CountWindow(1, 8), new CountWindow(0, 1), new CountWindow(0, 2), new CountWindow(0, 5),
        new CountWindow(0, 4), new CountWindow(1, 1), new CountWindow(0, 2), new CountWindow(0, 1),
        new CountWindow(0, 0), new CountWindow(0, 0), new CountWindow(1, 3), new CountWindow(0, 0),
        Now.AddDays(-1), 3);

    private sealed class ListRecorder : IModelUsageRecorder
    {
        public List<ModelUsageDraft> Drafts { get; } = [];
        public Task RecordAsync(ModelUsageDraft draft, CancellationToken cancellationToken)
        {
            Drafts.Add(draft);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedPrice(TokenPrice? price) : IModelPriceLookup
    {
        public Task<TokenPrice?> GetOpenRouterPriceAsync(string modelId, CancellationToken cancellationToken) =>
            Task.FromResult(price);
    }

    private sealed class UsageClient(UsageDetails? usage, bool fail = false, bool streamUsage = false) : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (fail) throw new InvalidOperationException("nope");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                ModelId = "anthropic/claude",
                Usage = usage
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var contents = new List<AIContent> { new TextContent("hi") };
            if (streamUsage && usage is not null) contents.Add(new UsageContent(usage));
            yield return new ChatResponseUpdate(ChatRole.Assistant, contents) { ModelId = "gpt-5.4" };
            await Task.CompletedTask;
        }
    }
}
