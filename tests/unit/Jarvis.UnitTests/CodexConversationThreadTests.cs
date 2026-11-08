using System.Text.Json;
using Jarvis.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// A conversation keeps one Codex thread, so later calls send only what the thread has not seen. Whenever the thread
/// does not hold exactly the start of the conversation, the call falls back to the full prompt on a new thread.
/// </summary>
[Collection(nameof(CodexProcessReuse))]
public sealed class CodexConversationThreadTests : IDisposable
{
    private static readonly Guid Conversation = Guid.NewGuid();
    private readonly string _root = Directory.CreateTempSubdirectory("jarvis-codex-thread-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Tool_steps_and_later_turns_continue_the_thread_with_only_what_is_new()
    {
        var (client, turns) = CreateClient(ToolCall("CreateReminder", """{"title":"stretch"}"""), Text("Done."),
            Text("You have one reminder."));
        using var _ = client;
        var chat = CodexCliChatClient.ForConversation(client, Conversation);
        var options = Options();
        List<ChatMessage> history = [new(ChatRole.User, "Remind me to stretch")];
        var context = Context("Current time reference: 12:00");

        var step1 = await chat.GetResponseAsync([.. history, context], options);
        var call = Assert.IsType<FunctionCallContent>(step1.Messages.Single().Contents.Single());
        history.Add(step1.Messages.Single());
        history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, "Reminder scheduled.")]));
        var step2 = await chat.GetResponseAsync([history[0], context, .. history[1..]], options);
        history.Add(step2.Messages.Single());
        history.Add(new ChatMessage(ChatRole.User, "What reminders do I have?"));
        await chat.GetResponseAsync([.. history, Context("Current time reference: 12:05")], options);

        var sent = ReadTurns(turns);
        Assert.Equal(3, sent.Length);
        Assert.Single(sent.Select(turn => turn.ThreadId).Distinct());
        Assert.Contains("Available Jarvis functions", sent[0].Text);
        // The tool step sends the result alone: the request and the unchanged context are already in the thread.
        Assert.DoesNotContain("Available Jarvis functions", sent[1].Text);
        Assert.Contains("Jarvis tool result (CreateReminder): \"Reminder scheduled.\"", sent[1].Text);
        Assert.DoesNotContain("Remind me to stretch", sent[1].Text);
        Assert.DoesNotContain("Current time reference", sent[1].Text);
        // The next turn sends the new message and that turn's context, marked as replacing the old one.
        Assert.Contains("What reminders do I have?", sent[2].Text);
        Assert.Contains("12:05", sent[2].Text);
        Assert.Contains("replace the earlier ones", sent[2].Text);
        Assert.DoesNotContain("Reminder scheduled", sent[2].Text);
    }

    [Fact]
    public async Task A_regenerated_reply_starts_a_new_thread_with_the_full_conversation()
    {
        var (client, turns) = CreateClient(Text("First answer."), Text("Second answer."));
        using var _ = client;
        var chat = CodexCliChatClient.ForConversation(client, Conversation);
        List<ChatMessage> history = [new(ChatRole.User, "Hello")];

        await chat.GetResponseAsync(history, Options());
        // The reply was deleted and the same question asked again: the thread holds a reply that is gone.
        await chat.GetResponseAsync([.. history, new ChatMessage(ChatRole.Assistant, "Edited away"),
            new ChatMessage(ChatRole.User, "Hello again")], Options());

        var sent = ReadTurns(turns);
        Assert.NotEqual(sent[0].ThreadId, sent[1].ThreadId);
        Assert.Contains("Available Jarvis functions", sent[1].Text);
        Assert.Contains("Hello again", sent[1].Text);
    }

    [Fact]
    public async Task Changed_tools_or_another_conversation_get_their_own_thread()
    {
        var (client, turns) = CreateClient(Text("One."), Text("Two."), Text("Three."));
        using var _ = client;
        var chat = CodexCliChatClient.ForConversation(client, Conversation);
        var other = CodexCliChatClient.ForConversation(client, Guid.NewGuid());
        var first = await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")], Options());

        await other.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")], Options());
        await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi"), first.Messages.Single(),
            new ChatMessage(ChatRole.User, "More")], Options("CreateReminder", "LogExpense"));

        var sent = ReadTurns(turns);
        Assert.Equal(3, sent.Select(turn => turn.ThreadId).Distinct().Count());
        Assert.Contains("LogExpense", sent[2].Text);
    }

    [Fact]
    public async Task Calls_outside_a_conversation_always_use_a_fresh_thread()
    {
        var (client, turns) = CreateClient(Text("One."), Text("Two."));
        using var _ = client;

        var first = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")], Options());
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi"), first.Messages.Single(),
            new ChatMessage(ChatRole.User, "More")], Options());

        var sent = ReadTurns(turns);
        Assert.NotEqual(sent[0].ThreadId, sent[1].ThreadId);
        Assert.Contains("Available Jarvis functions", sent[1].Text);
    }

    [Fact]
    public async Task A_streamed_reply_is_recognized_when_the_next_turn_continues()
    {
        var (client, turns) = CreateClient(Text("Hello there, how can I help?"), Text("Sure."));
        using var _ = client;
        var chat = CodexCliChatClient.ForConversation(client, Conversation);
        List<ChatMessage> history = [new(ChatRole.User, "Hi")];

        // Stored the way the agent framework stores a streamed reply: many small text parts.
        var parts = new List<AIContent>();
        await foreach (var update in chat.GetStreamingResponseAsync(history, Options()))
            parts.AddRange(update.Contents.OfType<TextContent>());
        history.Add(new ChatMessage(ChatRole.Assistant, parts));
        history.Add(new ChatMessage(ChatRole.User, "Thanks"));
        await chat.GetResponseAsync(history, Options());

        var sent = ReadTurns(turns);
        Assert.Equal(sent[0].ThreadId, sent[1].ThreadId);
        Assert.Contains("Thanks", sent[1].Text);
        Assert.DoesNotContain("Hello there", sent[1].Text);
    }

    [Fact]
    public async Task Background_work_between_turns_does_not_take_the_conversations_process()
    {
        var (client, turns) = CreateClient(Text("One."), Text("Two."), Text("Three."), Text("Four."), Text("Five."));
        using var _ = client;
        var chat = CodexCliChatClient.ForConversation(client, Conversation);
        // Two kept processes, as a server has once memory extraction runs beside chat.
        await Task.WhenAll(client.GetResponseAsync([new ChatMessage(ChatRole.User, "SLOW one")], Options()),
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "SLOW two")], Options()));
        List<ChatMessage> history = [new(ChatRole.User, "Hi")];
        history.Add((await chat.GetResponseAsync(history, Options())).Messages.Single());

        // Memory extraction starts right after the reply and is still running when the next message arrives.
        var extraction = client.GetResponseAsync([new ChatMessage(ChatRole.User, "SLOW extract memories")], Options());
        await Task.Delay(300);
        history.Add(new ChatMessage(ChatRole.User, "And then?"));
        await chat.GetResponseAsync(history, Options());
        await extraction;

        var sent = ReadTurns(turns);
        var hi = sent.Single(turn => turn.Text.Contains("\nHi\n"));
        var next = sent.Single(turn => turn.Text.Contains("And then?"));
        Assert.Equal(hi.ThreadId, next.ThreadId);
        Assert.StartsWith("Conversation continues", next.Text);
    }

    [Fact]
    public void A_tool_call_reloaded_from_storage_has_the_same_fingerprint()
    {
        // A saved conversation can come back with its properties in another order and with JSON's escaping
        // (\u002B for +); the call Jarvis returned holds plain values in the model's order.
        var returned = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "CreateReminder", new Dictionary<string, object?>
                { ["dueAt"] = "2026-11-08T13:05:21+00:00", ["title"] = "stretch é" })]);
        using var stored = JsonDocument.Parse("""{"title":"stretch \u00e9","dueAt":"2026-11-08T13:05:21\u002B00:00"}""");
        var reloaded = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "CreateReminder", stored.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => (object?)property.Value.Clone()))]);

        Assert.Equal(CodexCliChatClient.Fingerprint(returned), CodexCliChatClient.Fingerprint(reloaded));
    }

    [Fact]
    public async Task Turned_off_every_call_starts_a_new_thread()
    {
        var (client, turns) = CreateClient(threads: false, Text("One."), Text("Two."));
        using var _ = client;
        var chat = CodexCliChatClient.ForConversation(client, Conversation);

        var first = await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")], Options());
        await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi"), first.Messages.Single(),
            new ChatMessage(ChatRole.User, "More")], Options());

        var sent = ReadTurns(turns);
        Assert.NotEqual(sent[0].ThreadId, sent[1].ThreadId);
    }

    private static ChatMessage Context(string text) =>
        new ChatMessage(ChatRole.User, text).WithAgentRequestMessageSource(
            AgentRequestMessageSourceType.AIContextProvider, "clock");

    private static ChatOptions Options(params string[] toolNames) => new()
    {
        Instructions = "Be brief.",
        Tools = (toolNames.Length == 0 ? ["CreateReminder"] : toolNames)
            .Select(name => (AITool)AIFunctionFactory.Create((string title) => "ok", name)).ToList()
    };

    private static string Text(string text) =>
        JsonSerializer.Serialize(new { type = "text", text, name = "", argumentsJson = "" });

    private static string ToolCall(string name, string argumentsJson) =>
        JsonSerializer.Serialize(new { type = "tool_call", text = "", name, argumentsJson });

    private sealed record SentTurn(string ThreadId, string Text);

    private static SentTurn[] ReadTurns(string path) => File.ReadAllLines(path)
        .Select(line => JsonSerializer.Deserialize<SentTurn>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
        .ToArray();

    private (CodexCliChatClient Client, string Turns) CreateClient(params string[] replies) =>
        CreateClient(true, replies);

    private (CodexCliChatClient Client, string Turns) CreateClient(bool threads, params string[] replies)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var turns = Path.Combine(directory, "turns.jsonl");
        var path = Path.Combine(directory, "fake-codex");
        File.WriteAllText(path, AppServerScript
            .Replace("TURNS", JsonSerializer.Serialize(turns))
            .Replace("REPLIES", JsonSerializer.Serialize(JsonSerializer.Serialize(replies))));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var executable = new CodexExecutable(path, Path.Combine(directory, "managed"));
        return (new CodexCliChatClient(executable, enableWebSearch: false, turnTimeoutSeconds: 30,
            conversationThreads: threads), turns);
    }

    /// <summary>Replies in order, one per turn, across all threads; records each turn's thread and text.</summary>
    private const string AppServerScript = """
        #!/usr/bin/env python3
        import json, sys, time
        replies = json.loads(REPLIES)
        def send(value):
            print(json.dumps(value), flush=True)
        threads = 0
        turn = 0
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            message = json.loads(line)
            method = message.get("method")
            request_id = message.get("id")
            if method == "initialize":
                send({"id": request_id, "result": {}})
            elif method == "model/list":
                send({"id": request_id, "result": {"data": [
                    {"id": "gpt-5.4", "model": "gpt-5.4", "isDefault": True, "inputModalities": ["text"]}
                ], "nextCursor": None}})
            elif method == "thread/start":
                threads += 1
                send({"id": request_id, "result": {"thread": {"id": "thread-%d" % threads}}})
            elif method == "turn/start":
                params = message["params"]
                text = "".join(item.get("text", "") for item in params["input"] if item["type"] == "text")
                if "SLOW" in text:
                    time.sleep(1.5)
                with open(TURNS, "a") as log:
                    log.write(json.dumps({"threadId": params["threadId"], "text": text}) + "\n")
                reply = replies[min(turn, len(replies) - 1)]
                turn += 1
                send({"id": request_id, "result": {"turn": {"id": "turn-%d" % turn}}})
                for index in range(0, len(reply), 5):
                    send({"method": "item/agentMessage/delta", "params": {"threadId": params["threadId"],
                        "itemId": "msg-%d" % turn, "delta": reply[index:index + 5]}})
                send({"method": "turn/completed", "params": {"threadId": params["threadId"],
                    "turn": {"id": "turn-%d" % turn, "status": "completed"}}})
        """;
}
