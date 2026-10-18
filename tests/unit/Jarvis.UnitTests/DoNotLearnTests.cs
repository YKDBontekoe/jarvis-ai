using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Application.Profiles;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class DoNotLearnTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fd51");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fd52");

    [Fact]
    public void A_topic_matches_whole_words_ignoring_case_and_accents()
    {
        string[] topics = ["my health", "Café Noir"];

        Assert.True(DoNotLearnRules.Mentions("Talking about MY HEALTH today.", topics));
        Assert.True(DoNotLearnRules.Mentions("I met her at cafe noir, again", topics));
        Assert.False(DoNotLearnRules.Mentions("My healthy habits", topics));
        Assert.False(DoNotLearnRules.Mentions("nothing relevant", topics));
        Assert.False(DoNotLearnRules.Mentions(null, topics));
        Assert.False(DoNotLearnRules.Mentions("my health", null));
    }

    [Fact]
    public async Task The_list_is_validated_cleaned_deduplicated_and_private()
    {
        var service = new DoNotLearnService(new InMemorySettingsStore());

        Assert.Null(await service.SetAsync(Owner, [new string('x', 61)], default));
        Assert.Null(await service.SetAsync(Owner, Enumerable.Range(0, 31).Select(i => $"topic {i}").ToArray(), default));
        var saved = await service.SetAsync(Owner, ["  Health  ", "health", "", "Money   matters"], default);
        Assert.Equal(["Health", "Money matters"], saved);
        Assert.Equal(["Health", "Money matters"], await service.ListAsync(Owner, default));
        Assert.Empty(await service.ListAsync(Other, default));
        Assert.True(await service.MentionsAsync(Owner, "about my health", default));
        Assert.False(await service.MentionsAsync(Other, "about my health", default));
    }

    [Fact]
    public async Task A_message_that_mentions_a_topic_is_not_looked_at_for_memories_at_all()
    {
        var extracted = new List<string>();
        var extractor = Fake<IConversationMemoryExtractor>.Create(("ExtractAndStoreAsync", args =>
        {
            extracted.Add((string)args[2]!);
            return Task.CompletedTask;
        }));
        var store = new InMemorySettingsStore();
        var doNotLearn = new DoNotLearnService(store);
        await doNotLearn.SetAsync(Owner, ["my health"], default);
        var snapshot = new AssistantProfileSnapshot(Guid.NewGuid(), 1, "Default", null, null, null, null, true, false, [], false, [], null,
            null, null, null, "all", true, true, true, true, DateTimeOffset.UtcNow);
        var gate = new ConversationMemoryGate(
            Fake<IConversationStore>.Create(("GetAsync", _ => null)),
            Fake<IAssistantProfileService>.Create(("ResolveSnapshotAsync", _ => snapshot)),
            extractor, doNotLearn);

        await gate.ExtractAsync(Owner, Guid.NewGuid(), Guid.NewGuid(), "I want to talk about my health", default);
        await gate.ExtractAsync(Owner, Guid.NewGuid(), Guid.NewGuid(), "I prefer tea in the morning", default);
        await gate.ExtractAsync(Other, Guid.NewGuid(), Guid.NewGuid(), "I want to talk about my health", default);

        Assert.Equal(["I prefer tea in the morning", "I want to talk about my health"], extracted.Order().ToArray());
    }
}
