using System.Text;
using System.Text.Json;
using Jarvis.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// Codex can answer one turn with several agent messages. Each is a full structured response; the turn
/// must not fail because they arrive back to back, and the user must not see the same text twice.
/// </summary>
public sealed class CodexMultiMessageTests : IDisposable
{
    private const string CardReply = "Choose Calendar or Mail in the card to start connecting it.";
    private readonly string _root = Directory.CreateTempSubdirectory("jarvis-codex-multi-").FullName;

    [Fact]
    public async Task Repeated_agent_message_streams_once_and_completes_the_turn()
    {
        var text = Text(CardReply);
        var client = CreateClient(("msg-1", text), ("msg-2", text));

        var streamed = await StreamTextAsync(client);

        Assert.Equal(CardReply, streamed);
    }

    [Fact]
    public async Task Later_tool_call_message_wins_over_an_earlier_one()
    {
        var client = CreateClient(
            ("msg-1", ToolCall("RenderUi", """{"kind":"choice","title":"Old"}""")),
            ("msg-2", ToolCall("RenderUi", """{"kind":"choice","title":"Connect Calendar and Mail"}""")));

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Connect my calendar")],
            new ChatOptions { Tools = [RenderUi] });

        var call = Assert.IsType<FunctionCallContent>(Assert.Single(response.Messages.Single().Contents));
        Assert.Equal("RenderUi", call.Name);
        Assert.Equal("Connect Calendar and Mail", call.Arguments!["title"]?.ToString());
    }

    [Fact]
    public async Task Different_later_message_follows_the_earlier_text()
    {
        var client = CreateClient(("msg-1", Text("Looking that up.")), ("msg-2", Text("It is 21 degrees.")));

        var streamed = await StreamTextAsync(client);

        Assert.Equal("Looking that up.\n\nIt is 21 degrees.", streamed);
    }

    [Fact]
    public async Task Message_that_extends_the_earlier_text_only_streams_the_new_part()
    {
        var client = CreateClient(("msg-1", Text("Choose Calendar or Mail")), ("msg-2", Text(CardReply)));

        var streamed = await StreamTextAsync(client);

        Assert.Equal(CardReply, streamed);
    }

    [Fact]
    public void ParseLastResponse_reads_the_last_of_back_to_back_objects()
    {
        var raw = Text("first") + "\n" + Text("second");

        using var document = CodexCliChatClient.ParseLastResponse(raw);

        Assert.Equal("second", document.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void ParseLastResponse_reads_a_single_object()
    {
        using var document = CodexCliChatClient.ParseLastResponse(Text(CardReply));

        Assert.Equal(CardReply, document.RootElement.GetProperty("text").GetString());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private static readonly AIFunction RenderUi = AIFunctionFactory.Create(
        (string kind, string title) => "ok", "RenderUi");

    private static async Task<string> StreamTextAsync(CodexCliChatClient client)
    {
        var text = new StringBuilder();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "Connect my calendar")]))
            text.Append(update.Text);
        return text.ToString();
    }

    private static string Text(string text) =>
        JsonSerializer.Serialize(new { type = "text", text, name = "", argumentsJson = "" });

    private static string ToolCall(string name, string argumentsJson) =>
        JsonSerializer.Serialize(new { type = "tool_call", text = "", name, argumentsJson });

    private CodexCliChatClient CreateClient(params (string ItemId, string Output)[] messages)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(directory, "fake-codex");
        File.WriteAllText(path, AppServerScript.Replace("MESSAGES",
            JsonSerializer.Serialize(messages.Select(message => new[] { message.ItemId, message.Output }))));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var executable = new CodexExecutable(path, Path.Combine(directory, "managed"));
        return new CodexCliChatClient(executable, enableWebSearch: false, turnTimeoutSeconds: 30);
    }

    private const string AppServerScript = """
        #!/usr/bin/env python3
        import json, sys
        messages = json.loads(r'''MESSAGES''')
        def send(value):
            print(json.dumps(value), flush=True)
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
                    {"id": "gpt-5.4", "model": "gpt-5.4", "hidden": False, "isDefault": True, "inputModalities": ["text"]}
                ], "nextCursor": None}})
            elif method == "thread/start":
                send({"id": request_id, "result": {"thread": {"id": "thread-1"}}})
            elif method == "turn/start":
                send({"id": request_id, "result": {"turn": {"id": "turn-1"}}})
                for item_id, output in messages:
                    for index in range(0, len(output), 7):
                        send({"method": "item/agentMessage/delta", "params": {
                            "threadId": "thread-1", "turnId": "turn-1", "itemId": item_id,
                            "delta": output[index:index + 7]}})
                send({"method": "turn/completed", "params": {"turn": {"id": "turn-1", "status": "completed"}}})
        """;
}
