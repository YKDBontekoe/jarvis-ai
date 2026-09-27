using System.Text.Json;
using Jarvis.Agents.ModelProviders;
using Jarvis.Application.Integrations;
using Jarvis.Application.Settings;
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

        var withoutKey = CreateResolver(codex, settings, credentials);
        Assert.Same(codex, await withoutKey.GetChatClientAsync(Owner, ModelPurpose.Chat, CancellationToken.None));

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
        IIntegrationCredentialStore credentials) =>
        new(codex, settings, credentials,
            new OpenAiCompatibleClientFactory(new ConfigurationBuilder().Build(), NullLoggerFactory.Instance),
            NullLogger<ChatClientResolver>.Instance);

    private sealed class NamedClient(string name) : IChatClient
    {
        public string Name => name;
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, name)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
