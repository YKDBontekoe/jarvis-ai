using System.ClientModel;
using System.ClientModel.Primitives;
using Jarvis.Agents.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Jarvis.Agents.ModelProviders;

/// <summary>Builds OpenAI-compatible clients for OpenRouter and an optional server-wide embedding endpoint.</summary>
public sealed class OpenAiCompatibleClientFactory(IConfiguration configuration, ILoggerFactory loggerFactory)
{
    public const int EmbeddingDimensions = 1536;

    public Uri OpenRouterBaseUrl { get; } = new(
        (configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1").TrimEnd('/') + "/");

    public IChatClient CreateOpenRouterChatClient(string apiKey, string model) =>
        SentryChatInstrumentation.Instrument(
                CreateOpenRouterClient(apiKey).GetChatClient(model).AsIChatClient(),
                configuration.GetValue("Sentry:RecordAiContent", false))
            .AsBuilder()
            .ConfigureOptions(options => options.ModelId = model)
            .UseOpenTelemetry(loggerFactory, sourceName: "Jarvis.OpenRouterChatClient",
                configure: telemetry => telemetry.EnableSensitiveData = false)
            .Build();

    public IEmbeddingGenerator<string, Embedding<float>> CreateOpenRouterEmbeddingGenerator(string apiKey,
        string model) =>
        new FixedDimensionEmbeddingGenerator(CreateOpenRouterClient(apiKey).GetEmbeddingClient(model)
            .AsIEmbeddingGenerator(), EmbeddingDimensions);

    public EmbeddingModel? CreateServerEmbeddingModel()
    {
        var baseUrl = configuration["Embeddings:BaseUrl"];
        var model = configuration["Embeddings:Model"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model)) return null;
        var apiKey = configuration["Embeddings:ApiKey"];
        var client = new OpenAIClient(new ApiKeyCredential(string.IsNullOrWhiteSpace(apiKey) ? "unused" : apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(baseUrl.TrimEnd('/') + "/") });
        return new EmbeddingModel("server:" + model,
            new FixedDimensionEmbeddingGenerator(client.GetEmbeddingClient(model).AsIEmbeddingGenerator(),
                EmbeddingDimensions));
    }

    private OpenAIClient CreateOpenRouterClient(string apiKey)
    {
        var options = new OpenAIClientOptions { Endpoint = OpenRouterBaseUrl, NetworkTimeout = TimeSpan.FromMinutes(5) };
        options.AddPolicy(new AttributionHeadersPolicy(configuration["OpenRouter:AppUrl"] ?? "https://github.com/YKDBontekoe/jarvis-ai",
            configuration["OpenRouter:AppTitle"] ?? "Jarvis"), PipelinePosition.PerCall);
        return new OpenAIClient(new ApiKeyCredential(apiKey), options);
    }

    /// <summary>Adds OpenRouter's optional app attribution headers.</summary>
    private sealed class AttributionHeadersPolicy(string referer, string title) : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            Apply(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline,
            int currentIndex)
        {
            Apply(message);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }

        private void Apply(PipelineMessage message)
        {
            message.Request.Headers.Set("HTTP-Referer", referer);
            message.Request.Headers.Set("X-Title", title);
        }
    }
}

/// <summary>
/// Stores every embedding at the fixed pgvector width. Shorter vectors are zero-padded, which preserves cosine
/// similarity between vectors from the same model; longer vectors are truncated and re-normalized.
/// </summary>
internal sealed class FixedDimensionEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner, int dimensions)
    : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner)
{
    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var generated = await base.GenerateAsync(values, options, cancellationToken);
        var resized = new GeneratedEmbeddings<Embedding<float>>(generated.Select(embedding =>
            new Embedding<float>(Resize(embedding.Vector.Span, dimensions))
            {
                ModelId = embedding.ModelId,
                CreatedAt = embedding.CreatedAt
            }))
        {
            Usage = generated.Usage
        };
        return resized;
    }

    internal static float[] Resize(ReadOnlySpan<float> vector, int dimensions)
    {
        var result = new float[dimensions];
        vector[..Math.Min(vector.Length, dimensions)].CopyTo(result);
        if (vector.Length <= dimensions) return result;
        var norm = Math.Sqrt(result.Sum(value => (double)value * value));
        if (norm > 0)
            for (var index = 0; index < result.Length; index++) result[index] = (float)(result[index] / norm);
        return result;
    }
}
