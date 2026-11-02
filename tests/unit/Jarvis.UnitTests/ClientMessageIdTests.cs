using Jarvis.Api.Conversations;
using Jarvis.Domain.Conversations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ClientMessageIdTests
{
    private static readonly Guid Chat = Guid.CreateVersion7();

    private static Message Sent(string content, string id) =>
        new(Chat, "user", content, clientMessageId: id, clientPayloadHash: ClientMessageIds.Hash(content, null));

    [Theory]
    [InlineData("a", true)]
    [InlineData("0f5c2b7e-6c64-4f0e-9c0f-1a2b3c4d5e6f", true)]
    [InlineData("outbox-1730000000-3", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("semi;colon", false)]
    [InlineData("emoji😀", false)]
    public void Ids_are_short_and_plain(string id, bool valid) =>
        Assert.Equal(valid, ClientMessageIds.IsValid(id));

    [Fact]
    public void An_id_longer_than_64_characters_is_refused() =>
        Assert.False(ClientMessageIds.IsValid(new string('a', 65)));

    [Fact]
    public void The_fingerprint_depends_on_text_and_attachments()
    {
        var plain = ClientMessageIds.Hash("hello", null);
        Assert.Equal(plain, ClientMessageIds.Hash("hello", null));
        Assert.NotEqual(plain, ClientMessageIds.Hash("hello!", null));
        Assert.NotEqual(plain, ClientMessageIds.Hash("hello", "[{\"fileId\":\"x\"}]"));
    }

    [Fact]
    public void An_unseen_id_is_a_new_send()
    {
        var decision = ClientMessageIds.Decide(null, ClientMessageIds.Hash("hi", null), []);
        Assert.IsType<ClientMessageIds.Replay.New>(decision);
    }

    [Fact]
    public void A_retry_after_the_reply_returns_that_reply_without_running_again()
    {
        var original = Sent("hi", "m1");
        var tool = new Message(Chat, "tool", "lookup");
        var reply = new Message(Chat, "assistant", "hello");
        var later = new Message(Chat, "user", "and another thing");

        var decision = ClientMessageIds.Decide(original, ClientMessageIds.Hash("hi", null),
            [original, tool, reply, later]);

        var answered = Assert.IsType<ClientMessageIds.Replay.Answered>(decision);
        Assert.Equal(reply.Id, answered.Reply.Id);
    }

    [Fact]
    public void A_retry_while_the_original_is_last_resumes_it()
    {
        var original = Sent("hi", "m1");
        var decision = ClientMessageIds.Decide(original, ClientMessageIds.Hash("hi", null), [original]);
        Assert.Equal(original.Id, Assert.IsType<ClientMessageIds.Replay.Resume>(decision).Original.Id);
    }

    [Fact]
    public void The_same_id_with_changed_content_is_a_conflict()
    {
        var original = Sent("hi", "m1");
        var decision = ClientMessageIds.Decide(original, ClientMessageIds.Hash("hi there", null), [original]);
        Assert.IsType<ClientMessageIds.Replay.Conflict>(decision);
    }

    [Fact]
    public void A_retry_after_the_conversation_moved_on_without_a_reply_is_never_run_again()
    {
        var original = Sent("hi", "m1");
        var later = new Message(Chat, "user", "next");
        var decision = ClientMessageIds.Decide(original, ClientMessageIds.Hash("hi", null), [original, later]);
        Assert.IsType<ClientMessageIds.Replay.Conflict>(decision);
    }
}
