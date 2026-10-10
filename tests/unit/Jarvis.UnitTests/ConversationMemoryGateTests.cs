using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Application.Profiles;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Profiles;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ConversationMemoryGateTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000c0de");

    private static AssistantProfileSnapshot Snapshot(bool remember, bool contribute) =>
        new(Guid.Parse("01996b8c-6000-7000-8000-0000000000b1"), 1, "Work", null, null, null, null, true, false, [],
            false, [], null, null, null, null, MemoryScopes.All, true, contribute, true, remember, DateTimeOffset.UtcNow);

    private static async Task<List<(Guid SourceId, Guid? ProfileId, IReadOnlyList<MemoryExtractionTurn> Context)>>
        RunAsync(AssistantProfileSnapshot snapshot, Func<Guid, Guid, IReadOnlyList<Message>>? history = null)
    {
        var extracted = new List<(Guid, Guid?, IReadOnlyList<MemoryExtractionTurn>)>();
        var conversation = new Conversation(Owner, "Chat");
        var source = Guid.NewGuid();
        var messages = history?.Invoke(conversation.Id, source) ?? [];
        var gate = new ConversationMemoryGate(
            Fake<IConversationStore>.Create(
                ("GetAsync", _ => conversation),
                ("GetMessagePageAsync", _ => new MessagePage(messages, null, false))),
            Fake<IAssistantProfileService>.Create(("ResolveSnapshotAsync", _ => snapshot)),
            Fake<IConversationMemoryExtractor>.Create(("ExtractAndStoreAsync", args =>
            {
                extracted.Add(((Guid)args[1]!, (Guid?)args[4], (IReadOnlyList<MemoryExtractionTurn>)args[5]!));
                return Task.CompletedTask;
            })));

        await gate.ExtractAsync(Owner, conversation.Id, source, "I live in Amsterdam now.", default);
        return extracted;
    }

    [Fact]
    public async Task Passes_the_turns_before_the_source_message_as_context()
    {
        var extracted = await RunAsync(Snapshot(remember: true, contribute: true), (conversationId, source) =>
        [
            new Message(conversationId, "user", "Old question"),
            new Message(conversationId, "assistant", "Old answer"),
            new Message(conversationId, "tool", "{}"),
            new Message(conversationId, "user", "Earlier"),
            new Message(conversationId, "assistant", "Do you still live in Utrecht?"),
            new Message(conversationId, "user", "No, Amsterdam now.", source),
            new Message(conversationId, "assistant", "Noted.")
        ]);

        var context = Assert.Single(extracted).Context;
        Assert.Equal(["Old answer", "Earlier", "Do you still live in Utrecht?"],
            context.Skip(1).Select(turn => turn.Content));
        Assert.Equal(4, context.Count);
        Assert.DoesNotContain(context, turn => turn.Content is "Noted." or "No, Amsterdam now." or "{}");
    }

    [Fact]
    public async Task Gives_no_context_when_the_source_message_is_not_in_recent_history()
    {
        var extracted = await RunAsync(Snapshot(remember: true, contribute: true), (conversationId, _) =>
            [new Message(conversationId, "assistant", "Unrelated")]);

        Assert.Empty(Assert.Single(extracted).Context);
    }

    [Fact]
    public async Task A_profile_that_remembers_and_contributes_extracts_under_its_own_id()
    {
        var snapshot = Snapshot(remember: true, contribute: true);

        var extracted = await RunAsync(snapshot);

        Assert.Equal(snapshot.ProfileId, Assert.Single(extracted).ProfileId);
    }

    [Fact]
    public async Task A_profile_that_does_not_remember_extracts_nothing() =>
        Assert.Empty(await RunAsync(Snapshot(remember: false, contribute: true)));

    [Fact]
    public async Task A_profile_that_does_not_contribute_to_learning_extracts_nothing() =>
        Assert.Empty(await RunAsync(Snapshot(remember: true, contribute: false)));
}
