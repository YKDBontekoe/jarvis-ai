using System.Text.Json;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Integrations;
using Jarvis.Application.Settings;
using Jarvis.Application.Usage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ModelProviderTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000abcd");

    [Fact]
    public void Normalize_defaults_to_codex_and_trims_models()
    {
        var settings = new ModelSettings(" CODEX ", " gpt-5 ", null, "").Normalize();

        Assert.Equal(ModelSettings.Codex, settings.Provider);
        Assert.Equal("gpt-5", settings.ChatModel);
        Assert.Null(settings.EmbeddingModel);
    }

    [Theory]
    [InlineData("ollama", "llama3")]
    [InlineData("openrouter", null)]
    [InlineData("openrouter", "bad model id")]
    [InlineData("openrouter", "anthropic/claude;drop")]
    public void Normalize_rejects_unknown_providers_and_invalid_models(string provider, string? chatModel)
    {
        Assert.Throws<ArgumentException>(() => new ModelSettings(provider, chatModel).Normalize());
    }

    [Fact]
    public async Task Resolver_uses_codex_until_openrouter_has_a_key_and_model()
    {
        var codex = new NamedClient("codex");
        var settings = new InMemorySettingsStore();
        var credentials = new InMemoryCredentialStore();
        await settings.SaveAsync(Owner, SettingsSections.Models,
            new ModelSettings(ModelSettings.OpenRouter, "anthropic/claude-sonnet-4.5", "google/gemini-2.5-flash"),
            CancellationToken.None);

        var recorder = new RecordingUsageRecorder();
        var withoutKey = CreateResolver(codex, settings, credentials, recorder);
        var fallback = await withoutKey.GetChatClientAsync(Owner, ModelPurpose.Chat, CancellationToken.None);
        var fallbackResponse = await fallback.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        Assert.Equal("codex", fallbackResponse.Text);
        Assert.Equal(UsageProviders.Codex, recorder.Drafts.Single().Provider);

        await credentials.SaveSecretAsync(Owner, IntegrationCredentialProviders.OpenRouter,
            ModelSettings.OpenRouterKeySecret, "sk-or-test", CancellationToken.None);
        var withKey = CreateResolver(codex, settings, credentials);
        var chat = await withKey.GetChatClientAsync(Owner, ModelPurpose.Chat, CancellationToken.None);
        var background = await withKey.GetChatClientAsync(Owner, ModelPurpose.Background, CancellationToken.None);

        Assert.NotSame(codex, chat);
        Assert.NotSame(chat, background);
        Assert.Same(chat, await withKey.GetChatClientAsync(Owner, ModelPurpose.Chat, CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_returns_no_embedding_model_without_owner_or_server_configuration()
    {
        var resolver = CreateResolver(new NamedClient("codex"), new InMemorySettingsStore(), new InMemoryCredentialStore());

        Assert.Null(await resolver.GetEmbeddingModelAsync(Owner, CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_applies_owner_codex_chat_model_without_openrouter()
    {
        var codex = new NamedClient("codex");
        var settings = new InMemorySettingsStore();
        await settings.SaveAsync(Owner, SettingsSections.Models,
            new ModelSettings(ModelSettings.Codex, ChatModel: "gpt-5.2"), CancellationToken.None);

        var capturing = new CapturingClient(codex);
        var client = await CreateResolver(capturing, settings, new InMemoryCredentialStore())
            .GetChatClientAsync(Owner, ModelPurpose.Chat, CancellationToken.None);

        Assert.NotSame(codex, client);
        _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        Assert.Equal("gpt-5.2", capturing.LastModelId);
    }

    [Fact]
    public async Task Resolver_prefixes_embedding_model_names_by_source()
    {
        var settings = new InMemorySettingsStore();
        var credentials = new InMemoryCredentialStore();
        await settings.SaveAsync(Owner, SettingsSections.Models,
            new ModelSettings(ModelSettings.Codex, EmbeddingModel: "openai/text-embedding-3-small"), CancellationToken.None);
        await credentials.SaveSecretAsync(Owner, IntegrationCredentialProviders.OpenRouter,
            ModelSettings.OpenRouterKeySecret, "sk-or-test", CancellationToken.None);

        var model = await CreateResolver(new NamedClient("codex"), settings, credentials)
            .GetEmbeddingModelAsync(Owner, CancellationToken.None);

        Assert.Equal("openrouter:openai/text-embedding-3-small", model?.Name);
    }

    [Fact]
    public async Task Resolver_uses_the_owner_codex_chat_model_for_conversations()
    {
        var codex = new NamedClient("codex");
        var settings = new InMemorySettingsStore();
        await settings.SaveAsync(Owner, SettingsSections.Models,
            new ModelSettings(ModelSettings.Codex, "gpt-5.4"), CancellationToken.None);

        var resolver = CreateResolver(codex, settings, new InMemoryCredentialStore());
        var chat = await resolver.GetChatClientAsync(Owner, ModelPurpose.Chat, CancellationToken.None);
        await foreach (var _ in chat.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")])) { }

        Assert.Equal("gpt-5.4", codex.LastOptions?.ModelId);

        var background = await resolver.GetChatClientAsync(Owner, ModelPurpose.Background, CancellationToken.None);
        Assert.NotSame(codex, background);
        _ = await background.GetResponseAsync([new ChatMessage(ChatRole.User, "bg")]);
        Assert.Equal("gpt-5.4", codex.LastOptions?.ModelId);
    }

    [Fact]
    public void Resize_pads_short_vectors_and_renormalizes_truncated_vectors()
    {
        var padded = FixedDimensionEmbeddingGenerator.Resize([1f, 2f], 4);
        Assert.Equal([1f, 2f, 0f, 0f], padded);

        var truncated = FixedDimensionEmbeddingGenerator.Resize([3f, 4f, 12f], 2);
        Assert.Equal(0.6f, truncated[0], 3);
        Assert.Equal(0.8f, truncated[1], 3);
    }

    [Fact]
    public void Catalog_parses_pricing_tools_and_image_support()
    {
        using var document = JsonDocument.Parse("""
            {"data":[
              {"id":"z/model","name":"Zed","context_length":8192,"pricing":{"prompt":"0.000003","completion":"0.000015"},
               "supported_parameters":["tools","temperature"],"architecture":{"input_modalities":["text","image"]}},
              {"id":"a/basic","name":"Alpha","pricing":{"prompt":"0"}},
              {"name":"missing id"}
            ]}
            """);

        var models = OpenRouterCatalog.Parse(document.RootElement);

        Assert.Equal(["a/basic", "z/model"], models.Select(model => model.Id));
        var zed = models[1];
        Assert.Equal(3m, zed.PromptPricePerMillion);
        Assert.Equal(15m, zed.CompletionPricePerMillion);
        Assert.True(zed.SupportsTools);
        Assert.True(zed.SupportsImages);
        Assert.False(models[0].SupportsTools);
    }

    private static ChatClientResolver CreateResolver(IChatClient codex, IOwnerSettingsStore settings,
        IIntegrationCredentialStore credentials, RecordingUsageRecorder? recorder = null) =>
        new(codex, settings, credentials,
            new OpenAiCompatibleClientFactory(new ConfigurationBuilder().Build(), NullLoggerFactory.Instance),
            recorder ?? new RecordingUsageRecorder(), new NoPrices(),
            NullLogger<ChatClientResolver>.Instance);

    private sealed class RecordingUsageRecorder : IModelUsageRecorder
    {
        public List<ModelUsageDraft> Drafts { get; } = [];

        public Task RecordAsync(ModelUsageDraft draft, CancellationToken cancellationToken)
        {
            Drafts.Add(draft);
            return Task.CompletedTask;
        }
    }

    private sealed class NoPrices : IModelPriceLookup
    {
        public Task<TokenPrice?> GetOpenRouterPriceAsync(string modelId, CancellationToken cancellationToken) =>
            Task.FromResult<TokenPrice?>(null);
    }

    private sealed class NamedClient(string name) : IChatClient
    {
        public string Name => name;
        public ChatOptions? LastOptions { get; private set; }
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, name)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Empty();
            static async IAsyncEnumerable<ChatResponseUpdate> Empty()
            {
                yield break;
            }
        }
    }

    private sealed class CapturingClient(IChatClient inner) : IChatClient
    {
        public string? LastModelId { get; private set; }

        public void Dispose() => inner.Dispose();
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            inner.GetService(serviceType, serviceKey);
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastModelId = options?.ModelId;
            return inner.GetResponseAsync(messages, options, cancellationToken);
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            inner.GetStreamingResponseAsync(messages, options, cancellationToken);
    }
}
