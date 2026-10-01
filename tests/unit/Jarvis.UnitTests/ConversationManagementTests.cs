using Jarvis.Domain.Conversations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ConversationManagementTests
{
    [Fact]
    public void Rename_collapses_whitespace_and_keeps_the_conversation_in_place()
    {
        var conversation = new Conversation(Guid.CreateVersion7(), "New conversation");
        var updatedAt = conversation.UpdatedAt;

        conversation.Rename("  Weekend \n trip  ");

        Assert.Equal("Weekend trip", conversation.Title);
        Assert.Equal(updatedAt, conversation.UpdatedAt);
    }

    [Fact]
    public void Renamed_conversation_is_not_retitled_by_the_first_message()
    {
        var conversation = new Conversation(Guid.CreateVersion7(), "New conversation");
        conversation.Rename("Budget");

        conversation.SetTitleFromFirstMessage("What did I spend on groceries?");

        Assert.Equal("Budget", conversation.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_empty_titles(string title)
    {
        var conversation = new Conversation(Guid.CreateVersion7(), "Keep me");

        Assert.Throws<ArgumentException>(() => conversation.Rename(title));
        Assert.Equal("Keep me", conversation.Title);
    }

    [Fact]
    public void Rename_rejects_titles_over_the_limit()
    {
        var conversation = new Conversation(Guid.CreateVersion7(), "Keep me");

        Assert.Throws<ArgumentException>(() => conversation.Rename(new string('a', 201)));
    }

    [Fact]
    public void Pinning_twice_keeps_the_first_pin_time_and_unpin_clears_it()
    {
        var conversation = new Conversation(Guid.CreateVersion7(), "Pinned");

        conversation.SetPinned(true);
        var pinnedAt = conversation.PinnedAt;
        conversation.SetPinned(true);

        Assert.NotNull(pinnedAt);
        Assert.Equal(pinnedAt, conversation.PinnedAt);
        conversation.SetPinned(false);
        Assert.Null(conversation.PinnedAt);
    }
}
