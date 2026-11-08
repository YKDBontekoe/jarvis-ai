using System.Text;
using System.Text.Json;
using Jarvis.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// An agent turn makes one model call per tool step. Each call used to launch and handshake a new Codex process, so
/// processes are now kept between calls; one that ran Codex's own shell is never handed to a later call.
/// </summary>
[Collection(nameof(CodexProcessReuse))]
public sealed class CodexProcessReuseTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("jarvis-codex-reuse-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Consecutive_model_calls_share_one_process_and_one_working_directory()
    {
        var (client, starts, threads) = CreateClient();
        using var _ = client;

        Assert.Equal("reply 1", await AskAsync(client));
        Assert.Equal("reply 2", await AskAsync(client));
        Assert.Equal("reply 3", await AskAsync(client));

        Assert.Single(File.ReadAllLines(starts));
        // The working directory is part of what Codex puts in front of the prompt, so it must not change.
        Assert.Single(File.ReadAllLines(threads).Distinct());
    }

    [Fact]
    public async Task A_process_that_ran_a_shell_command_is_not_reused()
    {
        var (client, starts, _) = CreateClient(runCommand: true);
        using var _ = client;

        await AskAsync(client);
        await AskAsync(client);

        Assert.Equal(2, File.ReadAllLines(starts).Length);
    }

    [Fact]
    public async Task A_prewarmed_process_serves_the_next_call()
    {
        var (client, starts, _) = CreateClient();
        using var _ = client;

        client.Prewarm();
        await WaitForAsync(() => File.Exists(starts) && File.ReadAllLines(starts).Length == 1);
        await AskAsync(client);
        client.Prewarm();
        await AskAsync(client);

        Assert.Single(File.ReadAllLines(starts));
    }

    [Fact]
    public async Task Signing_in_again_retires_the_kept_processes()
    {
        var (client, starts, _) = CreateClient();
        using var _ = client;

        await AskAsync(client);
        CodexCliChatClient.RetireProcesses();
        await AskAsync(client);

        Assert.Equal(2, File.ReadAllLines(starts).Length);
    }

    [Fact]
    public async Task A_late_notification_from_an_earlier_thread_is_ignored()
    {
        var (client, _, _) = CreateClient(staleDelta: true);
        using var _ = client;

        Assert.Equal("reply 1", await AskAsync(client));
        Assert.Equal("reply 2", await AskAsync(client));
    }

    private static async Task<string> AskAsync(CodexCliChatClient client)
    {
        var text = new StringBuilder();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hello")]))
            text.Append(update.Text);
        return text.ToString();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(50);
        Assert.True(condition());
    }

    private (CodexCliChatClient Client, string Starts, string Threads) CreateClient(bool runCommand = false,
        bool staleDelta = false)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var starts = Path.Combine(directory, "starts.log");
        var threads = Path.Combine(directory, "threads.log");
        var path = Path.Combine(directory, "fake-codex");
        File.WriteAllText(path, AppServerScript
            .Replace("STARTS", JsonSerializer.Serialize(starts))
            .Replace("THREADS", JsonSerializer.Serialize(threads))
            .Replace("RUN_COMMAND", runCommand ? "True" : "False")
            .Replace("STALE_DELTA", staleDelta ? "True" : "False"));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var executable = new CodexExecutable(path, Path.Combine(directory, "managed"));
        return (new CodexCliChatClient(executable, enableWebSearch: false, turnTimeoutSeconds: 30), starts, threads);
    }

    private const string AppServerScript = """
        #!/usr/bin/env python3
        import json, os, sys
        with open(STARTS, "a") as log:
            log.write(str(os.getpid()) + "\n")
        def send(value):
            print(json.dumps(value), flush=True)
        thread = 0
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
                thread += 1
                with open(THREADS, "a") as log:
                    log.write(message["params"]["cwd"] + "\n")
                send({"id": request_id, "result": {"thread": {"id": "thread-%d" % thread}}})
            elif method == "turn/start":
                thread_id = message["params"]["threadId"]
                send({"id": request_id, "result": {"turn": {"id": "turn-%d" % thread}}})
                if STALE_DELTA and thread > 1:
                    send({"method": "item/agentMessage/delta", "params": {
                        "threadId": "thread-%d" % (thread - 1), "itemId": "old", "delta": "{\"type\":\"text\""}})
                if RUN_COMMAND:
                    send({"method": "item/started", "params": {"threadId": thread_id,
                        "item": {"id": "cmd-%d" % thread, "type": "commandExecution"}}})
                    send({"method": "item/completed", "params": {"threadId": thread_id,
                        "item": {"id": "cmd-%d" % thread, "type": "commandExecution"}}})
                output = json.dumps({"type": "text", "text": "reply %d" % thread, "name": "", "argumentsJson": ""})
                send({"method": "item/agentMessage/delta", "params": {
                    "threadId": thread_id, "itemId": "msg-%d" % thread, "delta": output}})
                send({"method": "turn/completed", "params": {"threadId": thread_id,
                    "turn": {"id": "turn-%d" % thread, "status": "completed"}}})
        """;
}

/// <summary>Signing in retires every kept Codex process, so these tests must not run beside the sign-in tests.</summary>
[CollectionDefinition(nameof(CodexProcessReuse), DisableParallelization = true)]
public sealed class CodexProcessReuse;
