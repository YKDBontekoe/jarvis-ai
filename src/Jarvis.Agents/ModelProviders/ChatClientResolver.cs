using Jarvis.Application.Integrations;
using Jarvis.Application.Settings;
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
        if (settings.UsesOpenRouter && await GetOpenRouterKeyAsync(ownerId, cancellationToken) is { } apiKey)
        {
            var model = purpose == ModelPurpose.Background ? settings.FastModel ?? settings.ChatModel : settings.ChatModel;
            if (model is not null) client = clientFactory.CreateOpenRouterChatClient(apiKey, model);
        }
        else if (settings.UsesOpenRouter)
        {
            logger.LogWarning("Owner {OwnerId} selected OpenRouter without an API key; using Codex.", ownerId);
        }
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
                clientFactory.CreateOpenRouterEmbeddingGenerator(apiKey, embeddingModel));
        model ??= clientFactory.CreateServerEmbeddingModel();
        _embeddingModels[ownerId] = model;
        return model;
    }

    private async Task<ModelSettings> GetSettingsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settingsStore.GetAsync<ModelSettings>(ownerId, SettingsSections.Models, cancellationToken)
        ?? ModelSettings.Default;

    private async Task<string?> GetOpenRouterKeyAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var secrets = await credentials.GetSecretsAsync(ownerId, IntegrationCredentialProviders.OpenRouter,
            cancellationToken);
        return secrets is not null && secrets.TryGetValue(ModelSettings.OpenRouterKeySecret, out var key) &&
               !string.IsNullOrWhiteSpace(key)
            ? key
            : null;
    }
}
