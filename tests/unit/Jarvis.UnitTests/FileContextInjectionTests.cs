using Jarvis.Agents;
using Jarvis.Application.Files;
using Jarvis.Infrastructure.Files;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class FileContextInjectionTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000cccc");
    private static readonly Guid Conversation = Guid.Parse("01996b8c-6000-7000-8000-00000000dddd");
    private static readonly Guid AttachedFile = Guid.Parse("01996b8c-6000-7000-8000-00000000eeee");

    [Fact]
    public async Task Attached_sources_add_the_relevant_passages_before_the_turn_and_record_citations()
    {
        var strong = Hit(0.8, "The tenant may end the lease with one month notice.");
        var weak = Hit(0.1, "Unrelated appendix.");
        IReadOnlyCollection<Guid>? searchedScope = null;
        var search = Fake<IFileSearchService>.Create(("SearchAsync", args =>
        {
            searchedScope = (IReadOnlyCollection<Guid>?)args[3];
            return (IReadOnlyList<FileSearchHit>)[strong, weak];
        }));
        var citations = new FileCitationCollector();

        var seen = await RunTurnAsync(search, citations, attached: true, "How do I end my lease?");

        Assert.Contains("one month notice", seen);
        Assert.Contains(strong.ChunkId.ToString(), seen);
        Assert.DoesNotContain("Unrelated appendix", seen);
        Assert.Equal([AttachedFile], searchedScope);
        Assert.Equal(strong.ChunkId, Assert.Single(citations.Drain()).ChunkId);
    }

    [Fact]
    public async Task Conversations_without_sources_never_search_files()
    {
        var search = Fake<IFileSearchService>.Create();

        var seen = await RunTurnAsync(search, new FileCitationCollector(), attached: false, "How do I end my lease?");

        Assert.DoesNotContain("attached reference files", seen);
    }

    [Fact]
    public async Task A_failing_search_still_lists_the_sources()
    {
        var search = Fake<IFileSearchService>.Create(("SearchAsync", _ =>
            Task.FromException<IReadOnlyList<FileSearchHit>>(new InvalidOperationException("database down"))));

        var seen = await RunTurnAsync(search, new FileCitationCollector(), attached: true, "How do I end my lease?");

        Assert.Contains("attached reference files", seen);
        Assert.DoesNotContain("Passages from the attached sources", seen);
    }

    private static async Task<string> RunTurnAsync(IFileSearchService search, IFileCitationCollector citations,
        bool attached, string message)
    {
        var sources = attached
            ? new ConversationFileSources([new ConversationFileAttachment(Conversation, AttachedFile, Owner,
                DateTimeOffset.UtcNow)], [])
            : new ConversationFileSources([], []);
        var contextRepository = Fake<IConversationFileContextRepository>.Create(("GetSourcesAsync", _ => sources));
        var scope = Fake<IConversationFileScopeService>.Create(("ResolveSearchFileIdsAsync", _ =>
            (IReadOnlyCollection<Guid>?)(attached ? [AttachedFile] : null)));
        var contributor = new FileContextContributor(contextRepository, search, scope,
            Fake<IDocumentCollectionRepository>.Create(), citations);
        using var client = new RecordingClient();
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            AIContextProviders = contributor.CreateProviders(new AgentBuildContext(Owner, null, Conversation)).ToArray()
        });

        await agent.RunAsync(message, await agent.CreateSessionAsync());
        return client.Seen;
    }

    private static FileSearchHit Hit(double score, string content) =>
        new(AttachedFile, "lease.pdf", Guid.NewGuid(), 0, content, score, 0, content.Length, 2, content);

    private sealed class RecordingClient : IChatClient
    {
        public string Seen { get; private set; } = string.Empty;
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Seen = string.Join("\n", messages.Select(message => message.Text));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
