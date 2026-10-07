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

    private static async Task<List<(Guid SourceId, Guid? ProfileId)>> RunAsync(AssistantProfileSnapshot snapshot)
    {
        var extracted = new List<(Guid, Guid?)>();
        var conversation = new Conversation(Owner, "Chat");
        var gate = new ConversationMemoryGate(
            Fake<IConversationStore>.Create(("GetAsync", _ => conversation)),
            Fake<IAssistantProfileService>.Create(("ResolveSnapshotAsync", _ => snapshot)),
            Fake<IConversationMemoryExtractor>.Create(("ExtractAndStoreAsync", args =>
            {
                extracted.Add(((Guid)args[1]!, (Guid?)args[4]));
                return Task.CompletedTask;
            })));

        var source = Guid.NewGuid();
        await gate.ExtractAsync(Owner, conversation.Id, source, "I live in Amsterdam now.", default);
        return extracted;
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
