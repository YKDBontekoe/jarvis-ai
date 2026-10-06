using Jarvis.Agents.Learning;
using Jarvis.Application.Conversations;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Application.Persona;
using Jarvis.Application.Settings;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ProfileLearningScopeTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000b0b0");
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Work = Guid.Parse("01996b8c-6000-7000-8000-0000000000a1");
    private static readonly Guid Home = Guid.Parse("01996b8c-6000-7000-8000-0000000000a2");

    [Fact]
    public void An_empty_batch_and_the_default_assistant_may_learn_everything()
    {
        var none = LearningScope.Of([]);
        var defaults = LearningScope.Of([MessageLearningScope.Default, MessageLearningScope.Default]);

        Assert.True(none.CanStoreMemories);
        Assert.Null(none.ProfileId);
        Assert.True(defaults.CanStoreMemories);
        Assert.True(defaults.AllowsPersona);
    }

    [Fact]
    public void A_batch_from_one_profile_stores_memories_under_that_profile()
    {
        var scope = LearningScope.Of([new MessageLearningScope(Work), new MessageLearningScope(Work)]);

        Assert.True(scope.CanStoreMemories);
        Assert.Equal(Work, scope.ProfileId);
    }

    [Fact]
    public void A_batch_that_mixes_profiles_stores_no_memories()
    {
        Assert.False(LearningScope.Of([new MessageLearningScope(Work), new MessageLearningScope(Home)]).CanStoreMemories);
        Assert.False(LearningScope.Of([new MessageLearningScope(Work), MessageLearningScope.Default]).CanStoreMemories);
    }

    [Fact]
    public void One_profile_that_forbids_remembering_or_persona_learning_blocks_the_whole_batch()
    {
        var noRemember = LearningScope.Of([MessageLearningScope.Default, new MessageLearningScope(null, AllowRemember: false)]);
        var noPersona = LearningScope.Of([MessageLearningScope.Default, new MessageLearningScope(null, AllowPersonaLearning: false)]);

        Assert.False(noRemember.CanStoreMemories);
        Assert.True(noRemember.AllowsPersona);
        Assert.True(noPersona.CanStoreMemories);
        Assert.False(noPersona.AllowsPersona);
    }

    private const string Reply = """
        {"persona":[{"category":"format","statement":"Keep replies short.","confidence":0.8}],
         "memories":[{"kind":"fact","content":"The user works on the Jarvis project.","importance":0.8,"confidence":0.95}]}
        """;

    private static async Task<(ReflectionOutcome Outcome, List<(string Content, Guid? ProfileId)> Stored, PersonaService Persona)>
        ReflectAsync(IReadOnlyDictionary<Guid, MessageLearningScope> scopes, params Message[] messages)
    {
        var settings = new InMemorySettingsStore();
        var persona = new PersonaService(settings);
        var stored = new List<(string Content, Guid? ProfileId)>();
        var service = new ReflectionService(
            TestHistory.Create(messages, scopes),
            Fake<IMessageFeedbackRepository>.Create(
                ("ListUnprocessedAsync", _ => (IReadOnlyList<MessageFeedbackRecord>)[])),
            persona, new InMemorySkillRepository(),
            Fake<IMemoryService>.Create(
                ("ListAsync", _ => (IReadOnlyList<MemoryRecord>)[]),
                ("CreateAsync", args =>
                {
                    stored.Add(((string)args[2]!, (Guid?)args[10]));
                    return new MemoryRecord(Guid.NewGuid(), Owner, (string)args[1]!, (string)args[2]!,
                        (float)args[3]!, (float)args[4]!, "conversation", null, Now, Now, null, false);
                })),
            new RecordingNotifications(), new NullAudit(),
            new FixedChatClientResolver(new Replying(Reply)), NullLogger<ReflectionService>.Instance);

        var (outcome, _) = await service.ReflectAsync(Owner, LearningSettings.Default, Now.AddDays(-1), default);
        return (outcome, stored, persona);
    }

    private static Message Said(string text) => new(Guid.NewGuid(), "user", text);

    [Fact]
    public async Task Reflection_files_a_memory_under_the_profile_the_chat_happened_in()
    {
        var message = Said("I work on the Jarvis project and like short replies.");

        var (outcome, stored, _) = await ReflectAsync(
            new Dictionary<Guid, MessageLearningScope> { [message.Id] = new(Work) }, message);

        Assert.Equal(1, outcome.MemoriesSaved);
        Assert.Equal(Work, Assert.Single(stored).ProfileId);
    }

    [Fact]
    public async Task Reflection_over_mixed_profiles_stores_no_memory_but_may_still_learn_persona()
    {
        var work = Said("I work on the Jarvis project.");
        var home = Said("Keep replies short at home too.");

        var (outcome, stored, persona) = await ReflectAsync(new Dictionary<Guid, MessageLearningScope>
        {
            [work.Id] = new(Work), [home.Id] = new(Home)
        }, work, home);

        Assert.Equal(0, outcome.MemoriesSaved);
        Assert.Empty(stored);
        Assert.Single((await persona.GetAsync(Owner, default)).TraitList);
    }

    [Fact]
    public async Task Reflection_respects_a_profile_that_does_not_remember_or_learn_persona()
    {
        var message = Said("I work on the Jarvis project and like short replies.");

        var (outcome, stored, persona) = await ReflectAsync(new Dictionary<Guid, MessageLearningScope>
        {
            [message.Id] = new(Work, AllowRemember: false, AllowPersonaLearning: false)
        }, message);

        Assert.Equal(0, outcome.MemoriesSaved);
        Assert.Empty(stored);
        Assert.Empty((await persona.GetAsync(Owner, default)).TraitList);
    }

    private sealed class Replying(string reply) : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
