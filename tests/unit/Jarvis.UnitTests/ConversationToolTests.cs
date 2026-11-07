using Jarvis.Agents.Conversations;
using Jarvis.Application.Conversations;
using Jarvis.Domain.Conversations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ConversationToolTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Chat = Guid.NewGuid();

    private sealed class User : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private static (ConversationAgentTools Tools, List<(Guid Id, Guid Owner, string? Title, bool? Pinned)> Calls) Create(
        bool exists = true)
    {
        var calls = new List<(Guid, Guid, string?, bool?)>();
        var store = Fake<IConversationStore>.Create(("UpdateAsync", args =>
        {
            calls.Add(((Guid)args[0]!, (Guid)args[1]!, (string?)args[2], (bool?)args[3]));
            return exists ? new Conversation(Owner, (string?)args[2] ?? "Chat") : null;
        }));
        return (new ConversationAgentTools(store, new User(), Chat), calls);
    }

    [Fact]
    public async Task Renaming_changes_only_this_chat_for_this_owner_and_only_the_title()
    {
        var (tools, calls) = Create();

        var reply = await tools.RenameThisChatAsync("  Trip planning\n ");

        Assert.Equal("This chat is now called \"Trip planning\".", reply);
        Assert.Equal((Chat, Owner, "Trip planning", (bool?)null), Assert.Single(calls));
    }

    [Fact]
    public async Task A_blank_title_changes_nothing_and_a_long_one_is_cut()
    {
        var (tools, calls) = Create();

        Assert.Equal("What should the chat be called?", await tools.RenameThisChatAsync("  \n "));
        Assert.Empty(calls);
        await tools.RenameThisChatAsync(new string('a', 500));
        Assert.Equal(ConversationAgentTools.MaxTitleLength, calls[0].Title!.Length);
    }

    [Theory]
    [InlineData(true, "Pinned this chat.")]
    [InlineData(false, "Unpinned this chat.")]
    public async Task Pinning_and_unpinning_leave_the_title_alone(bool pinned, string expected)
    {
        var (tools, calls) = Create();

        Assert.Equal(expected, await tools.PinThisChatAsync(pinned));
        Assert.Equal((Chat, Owner, (string?)null, (bool?)pinned), Assert.Single(calls));
    }

    [Fact]
    public async Task A_chat_that_is_not_the_owners_is_reported_missing()
    {
        var (tools, _) = Create(exists: false);

        Assert.Equal("I could not find this chat.", await tools.RenameThisChatAsync("x"));
        Assert.Equal("I could not find this chat.", await tools.PinThisChatAsync());
    }

    [Fact]
    public void The_tools_exist_only_in_a_chat_not_in_a_background_task()
    {
        var contributor = new ConversationToolContributor(Fake<IConversationStore>.Create(), new User());

        Assert.Equal(["RenameThisChat", "PinThisChat"],
            contributor.GetTools(new Jarvis.Agents.AgentBuildContext(Owner, null, Chat)).Select(t => t.Name));
        Assert.Empty(contributor.GetTools(new Jarvis.Agents.AgentBuildContext(Owner, null)));
        Assert.Empty(contributor.GetTools(new Jarvis.Agents.AgentBuildContext(Owner, Guid.NewGuid(), Chat)));
    }
}
