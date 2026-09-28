using Jarvis.Application.Integrations;
using Jarvis.Application.Settings;
using Jarvis.Application.Usage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents.ModelProviders;

public enum ModelPurpose
{
    /// <summary>Interactive and background-task agent turns.</summary>
    Chat,
    /// <summary>Memory extraction, reranking, reflection, and other short structured jobs.</summary>
    Background,
    /// <summary>Image understanding such as OCR during file indexing.</summary>
    Vision
}

/// <summary>Resolves the owner's configured model provider for a purpose.</summary>
public interface IChatClientResolver
{
    Task<IChatClient> GetChatClientAsync(Guid ownerId, ModelPurpose purpose, CancellationToken cancellationToken);

    /// <summary>Returns null when no embedding model is configured for the owner or the server.</summary>
    Task<EmbeddingModel?> GetEmbeddingModelAsync(Guid ownerId, CancellationToken cancellationToken);
}

public sealed record EmbeddingModel(string Name, IEmbeddingGenerator<string, Embedding<float>> Generator);

internal sealed class ChatClientResolver(
    IChatClient codexClient,
    IOwnerSettingsStore settingsStore,
    IIntegrationCredentialStore credentials,
    OpenAiCompatibleClientFactory clientFactory,
    IModelUsageRecorder usage,
    IModelPriceLookup prices,
    ILogger<ChatClientResolver> logger) : IChatClientResolver
{
    private readonly Dictionary<(Guid, ModelPurpose), IChatClient> _chatClients = [];
    private readonly Dictionary<Guid, EmbeddingModel?> _embeddingModels = [];

    public async Task<IChatClient> GetChatClientAsync(Guid ownerId, ModelPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (_chatClients.TryGetValue((ownerId, purpose), out var cached)) return cached;
        var settings = await GetSettingsAsync(ownerId, cancellationToken);
        IChatClient client = codexClient;
        var provider = UsageProviders.Codex;
        if (settings.UsesOpenRouter && await GetOpenRouterKeyAsync(ownerId, cancellationToken) is { } apiKey)
        {
            var model = purpose == ModelPurpose.Background ? settings.FastModel ?? settings.ChatModel : settings.ChatModel;
            if (model is not null)
            {
                client = clientFactory.CreateOpenRouterChatClient(apiKey, model);
                provider = UsageProviders.OpenRouter;
            }
        }
        else if (settings.UsesOpenRouter)
        {
            logger.LogWarning("Owner {OwnerId} selected OpenRouter without an API key; using Codex.", ownerId);
        }
        else if (ResolveCodexModelOverride(settings, purpose) is { } codexModel)
            client = new SelectedModelChatClient(codexClient, codexModel);
        client = new UsageRecordingChatClient(client, usage, prices, ownerId, provider, PurposeName(purpose), logger);
        _chatClients[(ownerId, purpose)] = client;
        return client;
    }

    public async Task<EmbeddingModel?> GetEmbeddingModelAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        if (_embeddingModels.TryGetValue(ownerId, out var cached)) return cached;
        var settings = await GetSettingsAsync(ownerId, cancellationToken);
        EmbeddingModel? model = null;
        if (settings.EmbeddingModel is { } embeddingModel &&
            await GetOpenRouterKeyAsync(ownerId, cancellationToken) is { } apiKey)
            model = new EmbeddingModel("openrouter:" + embeddingModel,
                RecordEmbeddings(clientFactory.CreateOpenRouterEmbeddingGenerator(apiKey, embeddingModel), ownerId,
                    embeddingModel, UsageProviders.OpenRouter));
        else if (clientFactory.CreateServerEmbeddingModel() is { } server)
            model = new EmbeddingModel(server.Name,
                RecordEmbeddings(server.Generator, ownerId, ServerModelId(server.Name), UsageProviders.Server));
        _embeddingModels[ownerId] = model;
        return model;
    }

    private async Task<ModelSettings> GetSettingsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settingsStore.GetAsync<ModelSettings>(ownerId, SettingsSections.Models, cancellationToken)
        ?? ModelSettings.Default;

    private UsageRecordingEmbeddingGenerator RecordEmbeddings(
        IEmbeddingGenerator<string, Embedding<float>> generator, Guid ownerId, string model, string provider) =>
        new(generator, usage, prices, ownerId, model, provider, logger);

    private static string ServerModelId(string name) =>
        name.StartsWith("server:", StringComparison.Ordinal) ? name["server:".Length..] : name;

    private static string PurposeName(ModelPurpose purpose) => purpose switch
    {
        ModelPurpose.Chat => UsagePurposes.Chat,
        ModelPurpose.Background => UsagePurposes.Background,
        ModelPurpose.Vision => UsagePurposes.Vision,
        _ => UsagePurposes.Background
    };

    private static string? ResolveCodexModelOverride(ModelSettings settings, ModelPurpose purpose) =>
        purpose switch
        {
            ModelPurpose.Chat => settings.ChatModel,
            ModelPurpose.Background => settings.FastModel ?? settings.ChatModel,
            _ => null
        };

    private async Task<string?> GetOpenRouterKeyAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var secrets = await credentials.GetSecretsAsync(ownerId, IntegrationCredentialProviders.OpenRouter,
            cancellationToken);
        return secrets is not null && secrets.TryGetValue(ModelSettings.OpenRouterKeySecret, out var key) &&
               !string.IsNullOrWhiteSpace(key)
            ? key
            : null;
    }

    /// <summary>Applies an owner-selected Codex model id without disposing the shared Codex client.</summary>
    private sealed class SelectedModelChatClient(IChatClient inner, string modelId) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            inner.GetResponseAsync(messages, WithModel(options), cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            inner.GetStreamingResponseAsync(messages, WithModel(options), cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : inner.GetService(serviceType, serviceKey);

        public void Dispose() { }

        private ChatOptions WithModel(ChatOptions? options)
        {
            options ??= new ChatOptions();
            options.ModelId = modelId;
            return options;
        }
    }
}
