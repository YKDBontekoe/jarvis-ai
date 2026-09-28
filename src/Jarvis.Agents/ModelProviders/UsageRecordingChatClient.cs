using System.Runtime.CompilerServices;
using System.Diagnostics;
using Jarvis.Application.Usage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents.ModelProviders;

/// <summary>
/// Persists token counts for one owner without failing the model call when usage storage is unavailable.
/// </summary>
internal sealed class UsageRecordingChatClient(
    IChatClient inner,
    IModelUsageRecorder recorder,
    IModelPriceLookup prices,
    Guid ownerId,
    string provider,
    string purpose,
    ILogger logger) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = UsageOutcomes.Completed;
        ChatResponse? response = null;
        try
        {
            response = await inner.GetResponseAsync(messages, options, cancellationToken);
            return response;
        }
        catch (Exception exception)
        {
            outcome = Classify(exception, cancellationToken);
            throw;
        }
        finally
        {
            await PersistAsync(response?.Usage, response?.ModelId ?? options?.ModelId, outcome, started);
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = UsageOutcomes.Completed;
        UsageDetails? usage = null;
        var model = options?.ModelId;
        var enumerator = inner.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (Exception exception)
                {
                    outcome = Classify(exception, cancellationToken);
                    throw;
                }

                if (!moved) break;
                var update = enumerator.Current;
                if (update.Contents?.OfType<UsageContent>().LastOrDefault() is { } reported)
                    usage = reported.Details;
                if (!string.IsNullOrWhiteSpace(update.ModelId)) model = update.ModelId;
                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
            await PersistAsync(usage, model, outcome, started);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : inner.GetService(serviceType, serviceKey);

    public void Dispose() { }

    private async Task PersistAsync(UsageDetails? usage, string? model, string outcome, long started)
    {
        try
        {
            var tokens = ModelUsageReader.Read(usage);
            decimal? cost = null;
            if (outcome == UsageOutcomes.Completed && provider == UsageProviders.OpenRouter &&
                !string.IsNullOrWhiteSpace(model))
            {
                try
                {
                    var price = await prices.GetOpenRouterPriceAsync(model, CancellationToken.None);
                    cost = UsageCost.Estimate(tokens.InputTokens, tokens.OutputTokens, price);
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "OpenRouter price lookup failed for {Model}.", model);
                }
            }
            await recorder.RecordAsync(new ModelUsageDraft(
                ownerId, provider, purpose, Clean(model), tokens.InputTokens, tokens.OutputTokens,
                tokens.CachedInputTokens, tokens.ReasoningOutputTokens, cost,
                (int)Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 0, int.MaxValue),
                outcome, tokens.WebSearchActions), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not persist {Provider} usage.", provider);
        }
    }

    private static string Classify(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            return UsageOutcomes.Cancelled;
        if (exception is TimeoutException or OperationCanceledException) return UsageOutcomes.Timeout;
        return UsageOutcomes.Failed;
    }

    private static string? Clean(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var trimmed = model.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }
}

internal sealed class UsageRecordingEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner,
    IModelUsageRecorder recorder,
    IModelPriceLookup prices,
    Guid ownerId,
    string model,
    string provider,
    ILogger logger) : IEmbeddingGenerator<string, Embedding<float>>
{
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = UsageOutcomes.Completed;
        GeneratedEmbeddings<Embedding<float>>? generated = null;
        try
        {
            generated = await inner.GenerateAsync(values, options, cancellationToken);
            return generated;
        }
        catch (Exception exception)
        {
            outcome = exception is OperationCanceledException && cancellationToken.IsCancellationRequested
                ? UsageOutcomes.Cancelled
                : exception is TimeoutException or OperationCanceledException
                    ? UsageOutcomes.Timeout
                    : UsageOutcomes.Failed;
            throw;
        }
        finally
        {
            var usage = ModelUsageReader.Read(generated?.Usage);
            decimal? cost = null;
            if (outcome == UsageOutcomes.Completed && provider == UsageProviders.OpenRouter)
            {
                try
                {
                    cost = UsageCost.Estimate(usage.InputTokens, usage.OutputTokens,
                        await prices.GetOpenRouterPriceAsync(model, CancellationToken.None));
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "OpenRouter embedding price lookup failed for {Model}.", model);
                }
            }
            try
            {
                await recorder.RecordAsync(new ModelUsageDraft(
                    ownerId, provider, UsagePurposes.Embedding, model, usage.InputTokens, usage.OutputTokens,
                    usage.CachedInputTokens, usage.ReasoningOutputTokens, cost,
                    (int)Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 0, int.MaxValue),
                    outcome, 0), CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not persist embedding usage.");
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : inner.GetService(serviceType, serviceKey);

    public void Dispose() { }
}
