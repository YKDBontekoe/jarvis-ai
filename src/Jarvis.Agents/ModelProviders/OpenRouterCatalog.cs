using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jarvis.Application.Usage;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.ModelProviders;

public sealed record OpenRouterModel(string Id, string Name, int? ContextLength, decimal? PromptPricePerMillion,
    decimal? CompletionPricePerMillion, bool SupportsTools, bool SupportsImages);

public sealed record ModelConnectionTest(bool Ok, string Provider, string? Model, long LatencyMs, string? Error);

/// <summary>Reads OpenRouter's public model list for the settings picker and verifies owner model settings.</summary>
public sealed class OpenRouterCatalog(HttpClient http, OpenAiCompatibleClientFactory factory) : IModelPriceLookup
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private IReadOnlyList<OpenRouterModel> _models = [];
    private DateTimeOffset _expiresAt;

    public async Task<IReadOnlyList<OpenRouterModel>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var models = await GetModelsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(search)) return models.Take(300).ToArray();
        var terms = search.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return models.Where(model => terms.All(term =>
                model.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                model.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Take(100).ToArray();
    }

    public async Task<TokenPrice?> GetOpenRouterPriceAsync(string modelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;
        try
        {
            var match = (await GetModelsAsync(cancellationToken)).FirstOrDefault(model =>
                model.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
            return match is null || (match.PromptPricePerMillion is null && match.CompletionPricePerMillion is null)
                ? null
                : new TokenPrice(match.PromptPricePerMillion, match.CompletionPricePerMillion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    public static async Task<ModelConnectionTest> TestAsync(IChatClient client, string provider, string? model,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Reply with the single word OK.")],
                new ChatOptions { MaxOutputTokens = 16 }, timeout.Token);
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return string.IsNullOrWhiteSpace(response.Text)
                ? new ModelConnectionTest(false, provider, response.ModelId ?? model, elapsed, "The model returned no text.")
                : new ModelConnectionTest(true, provider, response.ModelId ?? model, elapsed, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var message = exception is OperationCanceledException ? "The model did not answer within 45 seconds." : exception.Message;
            return new ModelConnectionTest(false, provider, model, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                message.Length > 300 ? message[..300] : message);
        }
    }

    private async Task<IReadOnlyList<OpenRouterModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        if (_expiresAt > DateTimeOffset.UtcNow) return _models;
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (_expiresAt > DateTimeOffset.UtcNow) return _models;
            using var response = await http.GetAsync(new Uri(factory.OpenRouterBaseUrl, "models"), cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            _models = Parse(document.RootElement);
            _expiresAt = DateTimeOffset.UtcNow + CacheLifetime;
            return _models;
        }
        finally
        {
            _refresh.Release();
        }
    }

    internal static IReadOnlyList<OpenRouterModel> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        var models = new List<OpenRouterModel>();
        foreach (var item in data.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? id : id;
            int? context = item.TryGetProperty("context_length", out var contextElement) &&
                           contextElement.TryGetInt32(out var contextLength) ? contextLength : null;
            decimal? prompt = null, completion = null;
            if (item.TryGetProperty("pricing", out var pricing))
            {
                prompt = PerMillion(pricing, "prompt");
                completion = PerMillion(pricing, "completion");
            }
            var parameters = item.TryGetProperty("supported_parameters", out var supported) &&
                             supported.ValueKind == JsonValueKind.Array
                ? supported.EnumerateArray().Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal)
                : [];
            var images = item.TryGetProperty("architecture", out var architecture) &&
                         architecture.TryGetProperty("input_modalities", out var modalities) &&
                         modalities.ValueKind == JsonValueKind.Array &&
                         modalities.EnumerateArray().Any(value => value.GetString() == "image");
            models.Add(new OpenRouterModel(id, name, context, prompt, completion, parameters.Contains("tools"), images));
        }
        return models.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static decimal? PerMillion(JsonElement pricing, string property) =>
        pricing.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var perToken)
            ? decimal.Round(perToken * 1_000_000m, 4)
            : null;
}
