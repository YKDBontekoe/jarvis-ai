using System.Text.Json;
using Jarvis.Application.Conversations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class MessageAttachmentTests
{
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3];

    [Fact]
    public async Task Stored_sessions_drop_photo_bytes_and_still_load()
    {
        var client = new RecordingClient();
        var agent = new ChatClientAgent(client);
        var session = await agent.CreateSessionAsync();
        await agent.RunAsync(new ChatMessage(ChatRole.User,
            [new TextContent("What plant is this?"), new DataContent(Png, "image/png")]), session);
        var serialized = (await agent.SerializeSessionAsync(session)).GetRawText();
        Assert.Contains("data:image/png", serialized);

        var stripped = AgentSessionJson.StripImageData(serialized);

        Assert.DoesNotContain("data:image", stripped);
        Assert.Contains("What plant is this?", stripped);
        using var document = JsonDocument.Parse(stripped);
        var restored = await agent.DeserializeSessionAsync(document.RootElement);
        await agent.RunAsync("And how often should I water it?", restored);
        var history = client.LastMessages;
        Assert.DoesNotContain(history.SelectMany(message => message.Contents), content => content is DataContent);
        Assert.Contains(history.SelectMany(message => message.Contents).OfType<TextContent>(),
            text => text.Text == AgentSessionJson.RemovedImagePlaceholder);
    }

    [Fact]
    public void Sessions_without_photos_are_returned_unchanged()
    {
        const string json = """{"messages":[{"contents":[{"$type":"text","text":"data:image/png is a prefix"}]}]}""";

        Assert.Equal(json, AgentSessionJson.StripImageData(json));
    }

    [Fact]
    public void Attachments_round_trip_and_tolerate_bad_json()
    {
        var attachments = new[] { new MessageAttachment(Guid.CreateVersion7(), "leaf.jpg", "image/jpeg") };

        var json = MessageAttachments.Serialize(attachments);

        Assert.Equal(attachments, MessageAttachments.Parse(json));
        Assert.Null(MessageAttachments.Serialize([]));
        Assert.Empty(MessageAttachments.Parse("{oops"));
        Assert.Equal("Shared a photo.", MessageAttachments.DefaultContent(1));
        Assert.Equal("Shared 3 photos.", MessageAttachments.DefaultContent(3));
        Assert.True(MessageAttachments.IsSupportedImage("image/webp"));
        Assert.False(MessageAttachments.IsSupportedImage("image/gif"));
    }

    private sealed class RecordingClient : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToArray();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A fern.")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
