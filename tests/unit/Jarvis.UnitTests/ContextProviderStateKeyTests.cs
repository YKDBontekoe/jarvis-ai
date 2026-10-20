using System.Reflection;
using Jarvis.Agents;
using Jarvis.Agents.Browser;
using Jarvis.Agents.Expenses;
using Jarvis.Agents.Journal;
using Jarvis.Agents.Planner;
using Jarvis.Agents.Surfaces;
using Jarvis.Agents.WhatsApp;
using Jarvis.Application.Surfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ContextProviderStateKeyTests
{
    private static readonly Guid OwnerId = Guid.Parse("01996b8c-6000-7000-8000-000000000001");

    [Fact]
    public void Context_provider_type_names_are_unique()
    {
        var duplicates = typeof(JarvisAgentFactory).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && typeof(AIContextProvider).IsAssignableFrom(type))
            .GroupBy(type => type.Name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public async Task Sending_a_message_accepts_every_static_guidance_provider()
    {
        var context = new AgentBuildContext(OwnerId, ExecutingTaskId: null);
        AIContextProvider[] providers =
        [
            .. new ExpenseContextContributor().CreateProviders(context),
            .. new WhatsAppContextContributor().CreateProviders(context),
            .. new JournalContextContributor().CreateProviders(context),
            .. new PlannerContextContributor().CreateProviders(context),
            .. new BrowserContextContributor().CreateProviders(context),
            .. new SurfaceContextContributor(Fake<IUiSurfaceRepository>.Create()).CreateProviders(context)
        ];
        var keys = providers.SelectMany(provider => provider.StateKeys).ToArray();
        Assert.Equal(keys.Distinct(StringComparer.Ordinal).Count(), keys.Length);

        var agent = new ChatClientAgent(new EchoClient(), new ChatClientAgentOptions
        {
            AIContextProviders = providers
        });

        var response = await agent.RunAsync("hello");

        Assert.Contains("Expenses:", response.Text);
        Assert.Contains("WhatsApp:", response.Text);
        Assert.Contains("Journaling:", response.Text);
        Assert.Contains("Day planner:", response.Text);
        Assert.Contains("hello", response.Text);
    }

    [Theory]
    [InlineData("dismissed", "closed the card \"Which tone?\" instead of tapping it")]
    [InlineData("open", "\"Which tone?\" is still open")]
    [InlineData("completed", null)]
    public async Task The_card_guidance_says_what_happened_to_the_latest_card(string status, string? expected)
    {
        var conversationId = Guid.CreateVersion7();
        var surfaces = Fake<IUiSurfaceRepository>.Create(("ListForConversationAsync", args =>
            (Guid)args[0]! == OwnerId && (Guid)args[1]! == conversationId
                ? new[]
                {
                    new UiSurfaceRecord(Guid.CreateVersion7(), OwnerId, conversationId, "choice", "Which tone?", "{}",
                        status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                }
                : Array.Empty<UiSurfaceRecord>()));
        var context = new AgentBuildContext(OwnerId, ExecutingTaskId: null, conversationId);
        var agent = new ChatClientAgent(new EchoClient(), new ChatClientAgentOptions
        {
            AIContextProviders = [.. new SurfaceContextContributor(surfaces).CreateProviders(context)]
        });

        var text = (await agent.RunAsync("hello")).Text;

        Assert.Contains("Generative UI", text);
        if (expected is null)
        {
            Assert.DoesNotContain("closed the card", text);
            Assert.DoesNotContain("still open", text);
        }
        else
        {
            Assert.Contains(expected, text);
        }
    }

    private sealed class EchoClient : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                string.Join("\n", messages.Select(message => message.Text)))));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }
    }
}
