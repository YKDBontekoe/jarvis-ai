using System.Diagnostics;
using Jarvis.Application.Integrations;
using Jarvis.Mcp;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// MCP servers start in front of every chat turn, so they must start together: one at a time, the turn waited for
/// the sum of every server's start-up before the model could begin.
/// </summary>
public sealed class McpParallelStartupTests : IDisposable
{
    private const int StartupDelayMs = 1_500;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mcp-startup-" + Guid.NewGuid().ToString("N"));
    private readonly string _server;

    public McpParallelStartupTests()
    {
        Directory.CreateDirectory(_directory);
        _server = Path.Combine(_directory, "slow_server.mjs");
        // Answers initialize only after DELAY_MS, like an npx or uvx server that takes a while to boot.
        File.WriteAllText(_server, """
            import readline from 'node:readline';
            const prefix = process.env.TOOL_PREFIX;
            const delay = Number(process.env.DELAY_MS || 0);
            import { appendFileSync } from 'node:fs';
            if (process.env.STARTS_LOG) appendFileSync(process.env.STARTS_LOG, prefix + '\n');
            const send = (id, result) => process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id, result }) + '\n');
            readline.createInterface({ input: process.stdin }).on('line', line => {
              const request = JSON.parse(line);
              if (request.id === undefined) return;
              if (request.method === 'initialize')
                setTimeout(() => send(request.id, { protocolVersion: request.params.protocolVersion,
                  capabilities: { tools: {} }, serverInfo: { name: prefix, version: '1.0.0' } }), delay);
              else if (request.method === 'tools/list') {
                send(request.id, { tools: [{ name: prefix + '_lookup', description: 'Look something up.',
                  inputSchema: { type: 'object', properties: {} } }] });
                // A server that dies after listing its tools, like one that crashed between two chat turns.
                if (process.env.EXIT_AFTER_LIST) setTimeout(() => process.exit(0), 50);
              }
              else if (request.method === 'ping') send(request.id, {});
              else process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: request.id,
                error: { code: -32601, message: 'Method not found' } }) + '\n');
            });
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Servers_start_together_and_keep_their_configured_order()
    {
        await using var host = CreateHost(("alpha", StartupDelayMs), ("beta", StartupDelayMs), ("gamma", StartupDelayMs));

        var started = Stopwatch.GetTimestamp();
        await host.InitializeAsync(CancellationToken.None);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Equal(["alpha_lookup", "beta_lookup", "gamma_lookup"], host.Tools.Select(tool => tool.Name));
        Assert.Equal(["alpha", "beta", "gamma"], host.Statuses.Select(status => status.Name));
        Assert.All(host.Statuses, status => Assert.Equal("connected", status.State));
        // One at a time this is at least 4.5 s; together it is about one start-up plus process launch.
        Assert.True(elapsed < TimeSpan.FromMilliseconds(StartupDelayMs * 2.5),
            $"MCP servers took {elapsed.TotalMilliseconds:0} ms to start; they should start in parallel.");
    }

    [Fact]
    public async Task A_server_that_fails_to_start_does_not_hold_up_or_break_the_others()
    {
        await using var host = CreateHost(("alpha", 0), ("broken", -1), ("gamma", 0));

        await host.InitializeAsync(CancellationToken.None);

        Assert.Equal(["alpha_lookup", "gamma_lookup"], host.Tools.Select(tool => tool.Name));
        Assert.Equal(["alpha", "broken", "gamma"], host.Statuses.Select(status => status.Name));
        Assert.Equal(["connected", "unavailable", "connected"], host.Statuses.Select(status => status.State));
    }

    [Fact]
    public async Task Cancelling_while_servers_start_cancels_the_turn()
    {
        await using var host = CreateHost(("alpha", 10_000), ("beta", 10_000));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.InitializeAsync(cancellation.Token));
    }

    private string StartsLog => Path.Combine(_directory, "starts.log");

    private int Starts => File.Exists(StartsLog) ? File.ReadAllLines(StartsLog).Length : 0;

    [Fact]
    public async Task The_next_turn_reuses_the_running_servers()
    {
        await using var pool = new McpConnectionPool();
        var owner = Guid.NewGuid();
        await using (var first = CreateHost(pool, owner, [("alpha", StartupDelayMs), ("beta", StartupDelayMs)]))
            await first.InitializeAsync(CancellationToken.None);

        await using var second = CreateHost(pool, owner, [("alpha", StartupDelayMs), ("beta", StartupDelayMs)]);
        var started = Stopwatch.GetTimestamp();
        await second.InitializeAsync(CancellationToken.None);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Equal(2, Starts);
        Assert.Equal(["alpha_lookup", "beta_lookup"], second.Tools.Select(tool => tool.Name));
        Assert.All(second.Statuses, status => Assert.Equal("connected", status.State));
        Assert.True(elapsed < TimeSpan.FromMilliseconds(StartupDelayMs),
            $"Reusing the servers took {elapsed.TotalMilliseconds:0} ms.");
    }

    [Fact]
    public async Task A_server_that_died_between_turns_is_started_again()
    {
        await using var pool = new McpConnectionPool();
        var owner = Guid.NewGuid();
        await using (var first = CreateHost(pool, owner, [("alpha", 0)], exitAfterList: true))
            await first.InitializeAsync(CancellationToken.None);
        await Task.Delay(500);

        await using var second = CreateHost(pool, owner, [("alpha", 0)], exitAfterList: true);
        await second.InitializeAsync(CancellationToken.None);

        Assert.Equal(2, Starts);
        Assert.Equal(["alpha_lookup"], second.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task A_changed_credential_or_another_owner_gets_its_own_connection()
    {
        await using var pool = new McpConnectionPool();
        var owner = Guid.NewGuid();
        await using (var first = CreateHost(pool, owner, [("alpha", 0)], token: "one"))
            await first.InitializeAsync(CancellationToken.None);
        await using (var rotated = CreateHost(pool, owner, [("alpha", 0)], token: "two"))
            await rotated.InitializeAsync(CancellationToken.None);
        await using (var other = CreateHost(pool, Guid.NewGuid(), [("alpha", 0)], token: "two"))
            await other.InitializeAsync(CancellationToken.None);

        Assert.Equal(3, Starts);
        Assert.NotEqual(McpConnectionPool.KeyFor(owner, new McpServerOptions { Name = "alpha" }),
            McpConnectionPool.KeyFor(Guid.NewGuid(), new McpServerOptions { Name = "alpha" }));
    }

    /// <summary>Servers start with a cleared environment, so name node by its full path.</summary>
    private static readonly string Node = (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(directory => Path.Combine(directory, OperatingSystem.IsWindows() ? "node.exe" : "node"))
        .FirstOrDefault(File.Exists) ?? "node";

    private McpToolHost CreateHost(params (string Name, int DelayMs)[] servers) =>
        CreateHost(null, Guid.NewGuid(), servers);

    /// <summary>A delay of -1 makes the server's command fail to start.</summary>
    private McpToolHost CreateHost(McpConnectionPool? pool, Guid owner, (string Name, int DelayMs)[] servers,
        string token = "one", bool exitAfterList = false)
    {
        var settings = new Dictionary<string, string?>();
        for (var index = 0; index < servers.Length; index++)
        {
            var (name, delay) = servers[index];
            var prefix = $"Mcp:Servers:{index}:";
            settings[prefix + "Name"] = name;
            settings[prefix + "Transport"] = "stdio";
            settings[prefix + "Command"] = delay < 0 ? Path.Combine(_directory, "missing-command") : Node;
            settings[prefix + "Arguments:0"] = _server;
            settings[prefix + "AllowedTools:0"] = name + "_lookup";
            settings[prefix + "AutoApprovedTools:0"] = name + "_lookup";
            settings[prefix + "EnvironmentVariables:TOOL_PREFIX"] = name;
            settings[prefix + "EnvironmentVariables:DELAY_MS"] = Math.Max(delay, 0).ToString();
            settings[prefix + "EnvironmentVariables:STARTS_LOG"] = StartsLog;
            settings[prefix + "EnvironmentVariables:SERVICE_TOKEN"] = token;
            if (exitAfterList) settings[prefix + "EnvironmentVariables:EXIT_AFTER_LIST"] = "1";
            settings[prefix + "ConnectionTimeoutSeconds"] = "20";
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var store = new InMemoryCredentialStore();
        var registry = new Jarvis.Infrastructure.Persistence.UserMcpServerRegistry(store,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Jarvis.Infrastructure.Persistence.UserMcpServerRegistry>.Instance);
        return new McpToolHost(configuration,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<McpToolHost>.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, new Owner(owner),
            store, registry, new NoHostPolicy(), null!, pool);
    }

    private sealed class Owner(Guid ownerId) : Jarvis.Application.Conversations.ICurrentUser
    {
        public Guid OwnerId { get; } = ownerId;
    }

    private sealed class NoHostPolicy : IOwnerMcpPolicyStore
    {
        public Task<IReadOnlyDictionary<string, HostMcpOverride>> GetAsync(Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, HostMcpOverride>>(
                new Dictionary<string, HostMcpOverride>());

        public Task<HostMcpOverride> SaveAsync(Guid ownerId, string serverName, HostMcpOverride value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
