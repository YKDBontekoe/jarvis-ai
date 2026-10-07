using Jarvis.Application.Integrations;
using Jarvis.Application.Settings;
using Jarvis.Application.Usage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ChatCompletionOptions = OpenAI.Chat.ChatCompletionOptions;

namespace Jarvis.Agents.ModelProviders;

public enum ModelPurpose
{
    /// <summary>Interactive and background-task agent turns.</summary>
    Chat,
    /// <summary>Memory extraction, reranking, and other short structured jobs.</summary>
    Background,
    /// <summary>Reflection, dreaming, durable background tasks, and other deeper reasoning jobs.</summary>
    Reasoning,
    /// <summary>Image understanding such as OCR during file indexing.</summary>
    Vision
}

/// <summary>Resolves the owner's configured model provider for a purpose.</summary>
public interface IChatClientResolver
{
    Task<IChatClient> GetChatClientAsync(Guid ownerId, ModelPurpose purpose, CancellationToken cancellationToken,
        ModelSettings? overlay = null);

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
    private readonly Dictionary<(Guid, ModelPurpose, string, string?, string?, string?), IChatClient> _chatClients = [];
    private readonly Dictionary<Guid, EmbeddingModel?> _embeddingModels = [];

    public async Task<IChatClient> GetChatClientAsync(Guid ownerId, ModelPurpose purpose,
        CancellationToken cancellationToken, ModelSettings? overlay = null)
    {
        var ownerSettings = await GetSettingsAsync(ownerId, cancellationToken);
        var settings = overlay is null ? ownerSettings : Merge(ownerSettings, overlay);
        var cacheKey = (ownerId, purpose, settings.Provider, settings.ChatModel, settings.FastModel,
            settings.ReasoningEffort);
        if (_chatClients.TryGetValue(cacheKey, out var cached)) return cached;
        IChatClient client = codexClient;
        var provider = UsageProviders.Codex;
        if (settings.UsesOpenRouter && await GetOpenRouterKeyAsync(ownerId, cancellationToken) is { } apiKey)
        {
            var model = purpose switch
            {
                ModelPurpose.Background => settings.FastModel ?? settings.ChatModel,
                _ => settings.ChatModel
            };
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
        if ((purpose is ModelPurpose.Chat or ModelPurpose.Reasoning) && settings.ReasoningEffort is { } effort)
            client = new ReasoningEffortChatClient(client, effort);
        // Only the Codex client has process slots to share; other providers must not see the marker.
        if (purpose is ModelPurpose.Background && provider == UsageProviders.Codex)
            client = new BackgroundPriorityChatClient(client);
        client = new UsageRecordingChatClient(client, usage, prices, ownerId, provider, PurposeName(purpose), logger);
        _chatClients[cacheKey] = client;
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

    private static ModelSettings Merge(ModelSettings owner, ModelSettings overlay) => owner with
    {
        ChatModel = overlay.ChatModel ?? owner.ChatModel,
        FastModel = overlay.FastModel ?? owner.FastModel,
        ReasoningEffort = overlay.ReasoningEffort ?? owner.ReasoningEffort
    };

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
        ModelPurpose.Reasoning => UsagePurposes.Reasoning,
        ModelPurpose.Vision => UsagePurposes.Vision,
        _ => UsagePurposes.Background
    };

    private static string? ResolveCodexModelOverride(ModelSettings settings, ModelPurpose purpose) =>
        purpose switch
        {
            ModelPurpose.Chat => settings.ChatModel,
            ModelPurpose.Background => settings.FastModel ?? settings.ChatModel,
            ModelPurpose.Reasoning => settings.ChatModel,
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

    /// <summary>Marks calls as background work so the Codex process limiter keeps a slot free for chat.</summary>
    private sealed class BackgroundPriorityChatClient(IChatClient inner) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            inner.GetResponseAsync(messages, Mark(options), cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            inner.GetStreamingResponseAsync(messages, Mark(options), cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : inner.GetService(serviceType, serviceKey);

        public void Dispose() { }

        private static ChatOptions Mark(ChatOptions? options)
        {
            options ??= new ChatOptions();
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            options.AdditionalProperties[CodexCliChatClient.BackgroundPriorityProperty] = true;
            return options;
        }
    }

    /// <summary>Applies an owner-selected effort without changing the shared provider client's defaults.</summary>
    private sealed class ReasoningEffortChatClient(IChatClient inner, string effort) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            inner.GetResponseAsync(messages, WithEffort(options), cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            inner.GetStreamingResponseAsync(messages, WithEffort(options), cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : inner.GetService(serviceType, serviceKey);

        public void Dispose() { }

        private ChatOptions WithEffort(ChatOptions? options)
        {
            options ??= new ChatOptions();
            options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            options.AdditionalProperties["reasoning_effort"] = effort;
            var originalFactory = options.RawRepresentationFactory;
            options.RawRepresentationFactory = client =>
            {
                var raw = originalFactory?.Invoke(client) as ChatCompletionOptions ?? new ChatCompletionOptions();
#pragma warning disable SCME0001 // OpenRouter accepts reasoning_effort as an extra Chat Completions field.
                raw.Patch.Set("$.reasoning_effort"u8, effort);
#pragma warning restore SCME0001
                return raw;
            };
            return options;
        }
    }
}
