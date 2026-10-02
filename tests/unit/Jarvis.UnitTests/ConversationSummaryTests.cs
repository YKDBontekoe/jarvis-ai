using Jarvis.Agents;
using Jarvis.Application.Conversations;
using Jarvis.Application.Persona;
using Jarvis.Application.Settings;
using Jarvis.Domain.Conversations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ConversationSummaryTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000c0de");
    private static readonly Guid Conversation = Guid.Parse("01996b8c-6000-7000-8000-00000000c0df");

    [Fact]
    public void Parse_reads_fenced_json_and_cleans_items()
    {
        var summary = ConversationSummaries.Parse("""
            Here you go:
            ```json
            {"summary": " We planned the trip. ", "key_points": ["Train at 9", "", 3, "Train at 9"],
             "action_items": ["- Book the hotel", "Pack the charger"]}
            ```
            """, 6);

        Assert.NotNull(summary);
        Assert.Equal("We planned the trip.", summary.Summary);
        Assert.Equal(["Train at 9"], summary.KeyPoints);
        Assert.Equal(["Book the hotel", "Pack the charger"], summary.ActionItems);
        Assert.Equal(6, summary.MessageCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("{\"key_points\": []}")]
    [InlineData("{\"summary\": \"   \"}")]
    [InlineData("{\"summary\": ")]
    public void Parse_rejects_answers_without_a_summary(string? text) =>
        Assert.Null(ConversationSummaries.Parse(text, 2));

    [Fact]
    public void Parse_caps_list_lengths()
    {
        var items = string.Join(",", Enumerable.Range(1, 20).Select(index => $"\"Item {index}\""));
        var summary = ConversationSummaries.Parse(
            $$"""{"summary": "S", "key_points": [{{items}}], "action_items": [{{items}}]}""", 2);

        Assert.NotNull(summary);
        Assert.Equal(ConversationSummaries.MaxKeyPoints, summary.KeyPoints.Count);
        Assert.Equal(ConversationSummaries.MaxActionItems, summary.ActionItems.Count);
    }

    [Fact]
    public void Transcript_keeps_speakers_in_order_and_skips_other_roles()
    {
        var messages = Messages(("user", "Hi"), ("tool", "secret tool output"), ("assistant", "Hello"),
            ("user", "  "));

        var transcript = ConversationSummaries.BuildTranscript(messages);

        Assert.Equal("User: Hi\n\nJarvis: Hello", transcript);
        Assert.Equal(2, ConversationSummaries.CountSpeakerMessages(messages));
    }

    [Fact]
    public void Transcript_keeps_newest_messages_when_too_long()
    {
        var messages = Messages(("user", "first " + new string('a', 80)), ("assistant", "second"),
            ("user", "third"));

        var transcript = ConversationSummaries.BuildTranscript(messages, maxCharacters: 40);

        Assert.StartsWith("[Earlier messages left out]", transcript);
        Assert.DoesNotContain("first", transcript);
        Assert.EndsWith("Jarvis: second\n\nUser: third", transcript);
    }

    [Fact]
    public async Task Summarizer_sends_transcript_and_returns_parsed_summary()
    {
        var client = new RecordingClient("""{"summary": "Short chat.", "key_points": [], "action_items": ["Call Sam"]}""");
        var summarizer = new ConversationSummarizer(new FixedChatClientResolver(client),
            new PersonaService(new InMemorySettingsStore()), NullLogger<ConversationSummarizer>.Instance);

        var summary = await summarizer.SummarizeAsync(Owner,
            Messages(("user", "Remind me to call Sam"), ("assistant", "Sure, when?")), CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal(["Call Sam"], summary.ActionItems);
        Assert.Equal(2, summary.MessageCount);
        Assert.Contains(ConversationSummarizer.PromptMarker, client.Messages[0].Text);
        Assert.Contains("untrusted", client.Messages[0].Text);
        Assert.Equal("User: Remind me to call Sam\n\nJarvis: Sure, when?", client.Messages[1].Text);
    }

    [Fact]
    public async Task Summarizer_uses_reply_language_from_persona()
    {
        var settings = new InMemorySettingsStore();
        await settings.SaveAsync(Owner, SettingsSections.Persona, new PersonaProfile(ReplyLanguage: "Dutch"),
            CancellationToken.None);
        var client = new RecordingClient("""{"summary": "Kort."}""");
        var summarizer = new ConversationSummarizer(new FixedChatClientResolver(client), new PersonaService(settings),
            NullLogger<ConversationSummarizer>.Instance);

        await summarizer.SummarizeAsync(Owner, Messages(("user", "Hoi"), ("assistant", "Hallo")),
            CancellationToken.None);

        Assert.Contains("Write in Dutch.", client.Messages[0].Text);
    }

    [Fact]
    public async Task Summarizer_returns_null_for_short_chats_and_model_failures()
    {
        var client = new RecordingClient("""{"summary": "x"}""");
        var summarizer = new ConversationSummarizer(new FixedChatClientResolver(client),
            new PersonaService(new InMemorySettingsStore()), NullLogger<ConversationSummarizer>.Instance);
        Assert.Null(await summarizer.SummarizeAsync(Owner, Messages(("user", "Hi")), CancellationToken.None));
        Assert.Empty(client.Messages);

        var failing = new ConversationSummarizer(new FixedChatClientResolver(new RecordingClient(null)),
            new PersonaService(new InMemorySettingsStore()), NullLogger<ConversationSummarizer>.Instance);
        Assert.Null(await failing.SummarizeAsync(Owner, Messages(("user", "Hi"), ("assistant", "Hello")),
            CancellationToken.None));
    }

    private static List<Message> Messages(params (string Role, string Content)[] items) =>
        items.Select(item => new Message(Conversation, item.Role, item.Content)).ToList();

    private sealed class RecordingClient(string? text) : IChatClient
    {
        public List<ChatMessage> Messages { get; } = [];
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages.AddRange(messages);
            return text is null
                ? throw new InvalidOperationException("model down")
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
