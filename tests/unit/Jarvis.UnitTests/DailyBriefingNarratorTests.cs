using Jarvis.Agents;
using Jarvis.Application.Persona;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class DailyBriefingNarratorTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000b00b");

    [Fact]
    public async Task Rewrite_returns_model_sentences()
    {
        var settings = new InMemorySettingsStore();
        await settings.SaveAsync(Owner, SettingsSections.Persona, new PersonaProfile(
            CustomInstructions: "Keep it short.", PreferredName: "Youri"), CancellationToken.None);
        var narrator = new DailyBriefingNarrator(new FixedChatClientResolver(new StaticClient("Quiet morning.")),
            new PersonaService(settings), NullLogger<DailyBriefingNarrator>.Instance);
        var facts = new DailyBriefingFacts(new DateOnly(2030, 1, 16), "UTC",
            [new DailyBriefingItem("Trash", "07:30")], []);

        var text = await narrator.NarrateAsync(Owner, facts, CancellationToken.None);

        Assert.Equal("Quiet morning.", text);
        var body = DailyBriefingComposer.Combine(text, DailyBriefingComposer.Compose(facts));
        Assert.StartsWith("Quiet morning.", body);
        Assert.Contains("Trash", body);
    }

    [Fact]
    public async Task Rewrite_failure_returns_null_so_facts_stay()
    {
        var narrator = new DailyBriefingNarrator(new FixedChatClientResolver(new ThrowingClient()),
            new PersonaService(new InMemorySettingsStore()), NullLogger<DailyBriefingNarrator>.Instance);

        Assert.Null(await narrator.NarrateAsync(Owner, new DailyBriefingFacts(new DateOnly(2030, 1, 16), "UTC", [], []),
            CancellationToken.None));
    }

    private sealed class StaticClient(string text) : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
            await Task.CompletedTask;
        }
    }

    private sealed class ThrowingClient : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("model down");
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("model down");
    }
}
