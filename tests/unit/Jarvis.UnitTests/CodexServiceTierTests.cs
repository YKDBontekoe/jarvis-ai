using System.Text.Json;
using Jarvis.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// Interactive chat asks Codex for priority processing ("Fast" mode) when the account offers it for the model, so a
/// waiting user gets a quicker reply. Background work never does, and nothing is sent when the tier is unavailable.
/// </summary>
public sealed class CodexServiceTierTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("jarvis-codex-tier-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Interactive_chat_requests_the_priority_tier_when_the_model_offers_it()
    {
        var (client, turns) = CreateClient(modelTiers: ["priority"]);
        using var _ = client;

        await CodexCliChatClient.MarkInteractive(client).GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]);

        Assert.Equal("priority", Assert.Single(ReadTurns(turns)).GetProperty("serviceTier").GetString());
    }

    [Fact]
    public async Task Background_work_leaves_the_tier_to_codex()
    {
        var (client, turns) = CreateClient(modelTiers: ["priority"]);
        using var _ = client;

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Extract memories")]);

        // Not even an explicit null: that would override the model's own default tier.
        Assert.False(Assert.Single(ReadTurns(turns)).TryGetProperty("serviceTier", out var _));
    }

    [Fact]
    public async Task Nothing_is_requested_when_the_model_has_no_priority_tier_or_it_is_turned_off()
    {
        var (unsupported, unsupportedTurns) = CreateClient(modelTiers: []);
        var (disabled, disabledTurns) = CreateClient(modelTiers: ["priority"], configuredTier: null);
        using var a = unsupported;
        using var b = disabled;

        await CodexCliChatClient.MarkInteractive(unsupported).GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]);
        await CodexCliChatClient.MarkInteractive(disabled).GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]);

        Assert.False(Assert.Single(ReadTurns(unsupportedTurns)).TryGetProperty("serviceTier", out var _));
        Assert.False(Assert.Single(ReadTurns(disabledTurns)).TryGetProperty("serviceTier", out var _));
    }

    [Fact]
    public async Task An_older_catalog_that_lists_the_fast_speed_tier_counts()
    {
        var (client, turns) = CreateClient(modelTiers: [], legacySpeedTiers: ["fast"]);
        using var _ = client;

        await CodexCliChatClient.MarkInteractive(client).GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]);

        Assert.Equal("priority", Assert.Single(ReadTurns(turns)).GetProperty("serviceTier").GetString());
    }

    private static JsonElement[] ReadTurns(string path) =>
        File.ReadAllLines(path).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();

    private (CodexCliChatClient Client, string Turns) CreateClient(string[] modelTiers,
        string? configuredTier = CodexCliChatClient.PriorityServiceTier, string[]? legacySpeedTiers = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var turns = Path.Combine(directory, "turns.jsonl");
        var path = Path.Combine(directory, "fake-codex");
        var model = new Dictionary<string, object>
        {
            ["id"] = "gpt-5.4", ["model"] = "gpt-5.4", ["isDefault"] = true, ["inputModalities"] = new[] { "text" },
            ["serviceTiers"] = modelTiers.Select(id => new { id, name = id, description = id }).ToArray()
        };
        if (legacySpeedTiers is not null) model["additionalSpeedTiers"] = legacySpeedTiers;
        File.WriteAllText(path, AppServerScript
            .Replace("TURNS", JsonSerializer.Serialize(turns))
            .Replace("MODEL", JsonSerializer.Serialize(JsonSerializer.Serialize(model))));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var executable = new CodexExecutable(path, Path.Combine(directory, "managed"));
        return (new CodexCliChatClient(executable, enableWebSearch: false, turnTimeoutSeconds: 30,
            chatServiceTier: configuredTier), turns);
    }

    private const string AppServerScript = """
        #!/usr/bin/env python3
        import json, sys
        model = json.loads(MODEL)
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
                send({"id": request_id, "result": {"data": [model], "nextCursor": None}})
            elif method == "thread/start":
                send({"id": request_id, "result": {"thread": {"id": "thread-1"}}})
            elif method == "turn/start":
                with open(TURNS, "a") as log:
                    log.write(json.dumps(message["params"]) + "\n")
                send({"id": request_id, "result": {"turn": {"id": "turn-1"}}})
                output = json.dumps({"type": "text", "text": "Hello", "name": "", "argumentsJson": ""})
                send({"method": "item/agentMessage/delta", "params": {"threadId": "thread-1", "itemId": "m", "delta": output}})
                send({"method": "turn/completed", "params": {"threadId": "thread-1",
                    "turn": {"id": "turn-1", "status": "completed"}}})
        """;
}
