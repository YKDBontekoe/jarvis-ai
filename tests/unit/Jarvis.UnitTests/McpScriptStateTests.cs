using System.Net;
using System.Net.WebSockets;
using System.Text;
using Jarvis.Api.McpRunner;
using Jarvis.Application.Integrations;
using Jarvis.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class McpScriptStateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "state-test-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(_directory, McpRunnerHost.StateFileName);

    public McpScriptStateTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task The_state_file_starts_with_what_was_saved_and_reports_only_changes()
    {
        var sync = new StateSync(StatePath, """{"session":"one"}""");
        await sync.InitializeAsync(default);

        Assert.Equal("""{"session":"one"}""", await File.ReadAllTextAsync(StatePath));
        Assert.Null(await sync.ChangedAsync(default));

        await File.WriteAllTextAsync(StatePath, """{"session":"two"}""" + "\n");
        Assert.Equal("""{"session":"two"}""", await sync.ChangedAsync(default));
        Assert.Null(await sync.ChangedAsync(default));
    }

    [Fact]
    public async Task A_removed_or_emptied_state_file_reports_a_logout_once()
    {
        var sync = new StateSync(StatePath, "saved");
        await sync.InitializeAsync(default);

        File.Delete(StatePath);
        Assert.Equal("", await sync.ChangedAsync(default));
        Assert.Null(await sync.ChangedAsync(default));

        var fresh = new StateSync(StatePath, null);
        Assert.Null(await fresh.ChangedAsync(default));
    }

    [Fact]
    public async Task A_state_file_over_the_limit_is_ignored_so_the_saved_state_stays()
    {
        var sync = new StateSync(StatePath, "saved");
        await sync.InitializeAsync(default);

        await File.WriteAllTextAsync(StatePath, new string('x', McpRunnerState.MaxLength + 1));
        Assert.Null(await sync.ChangedAsync(default));
    }

    [Fact]
    public void The_runner_hands_a_script_its_state_file_and_checks_the_saved_state()
    {
        var check = CustomMcpScriptValidator.Validate(CustomMcpTemplate.Source);
        var script = new McpRunnerScript("python", CustomMcpTemplate.Source, check.Sha256);

        var launch = McpRunnerHost.Validate(new McpRunnerLaunch("uv", [], new Dictionary<string, string>(), script,
            """{"token":"abc"}"""));
        Assert.Equal("""{"token":"abc"}""", launch.State);

        var settings = new RunnerSettings(1, TimeSpan.FromMinutes(1), _directory, _directory, "/usr/bin");
        var start = McpRunnerHost.StartInfo(launch, settings, _directory);
        Assert.Equal(StatePath, start.Environment[McpRunnerState.FileEnvironmentVariable]);

        Assert.Throws<ArgumentException>(() => McpRunnerHost.Validate(new McpRunnerLaunch("uv", [],
            new Dictionary<string, string>(), script, new string('x', McpRunnerState.MaxLength + 1))));
        // A package from the registry never gets a state file.
        var package = McpRunnerHost.StartInfo(McpRunnerHost.Validate(new McpRunnerLaunch("npx",
            ["-y", "@brave/brave-search-mcp-server@2.1.3"], new Dictionary<string, string>())), settings, _directory);
        Assert.False(package.Environment.ContainsKey(McpRunnerState.FileEnvironmentVariable));
    }

    [Fact]
    public async Task The_transport_passes_a_state_update_on_and_keeps_it_out_of_the_mcp_stream()
    {
        await using var app = await StartFakeRunnerAsync();
        var received = new List<string>();
        var transport = new McpRunnerClientTransport(new McpRunnerOptions(RunUrl(app), new string('t', 40)),
            new McpRunnerLaunch("npx", ["-y", "pkg@1.0.0"], new Dictionary<string, string>()), "test",
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, received.Add);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = await transport.ConnectAsync(timeout.Token);
        var message = await connection.MessageReader.ReadAsync(timeout.Token);

        Assert.Equal(["""{"token":"abc"}"""], received);
        Assert.Contains("\"id\":1", System.Text.Json.JsonSerializer.Serialize(message));
    }

    [Fact]
    public async Task A_changed_state_is_saved_encrypted_with_the_keys_and_a_cleared_one_is_removed()
    {
        var store = new InMemoryCredentialStore();
        var owner = Guid.NewGuid();
        var provider = IntegrationCredentialProviders.UserMcpPrefix + Guid.NewGuid().ToString("N");
        await store.SaveSecretAsync(owner, provider, "vacuum_key", "key", default);
        await using var host = new McpToolHost(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<McpToolHost>.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, new TestOwner(owner), store,
            null!, null!, null!);

        host.RememberScriptState(provider, """{"token":"abc"}""");
        await host.PersistScriptStateAsync(provider, default);
        var saved = await store.GetSecretsAsync(owner, provider, default);
        Assert.Equal("""{"token":"abc"}""", saved![IntegrationCredentialProviders.UserMcpStateSecret]);
        Assert.Equal("key", saved["vacuum_key"]);

        host.RememberScriptState(provider, "");
        await host.PersistScriptStateAsync(provider, default);
        Assert.DoesNotContain(IntegrationCredentialProviders.UserMcpStateSecret,
            (await store.GetSecretsAsync(owner, provider, default))!.Keys);
    }

    [Fact]
    public void The_saved_state_is_not_a_key_the_owner_can_declare_or_the_agent_can_read()
    {
        var binding = new McpSecretBinding(IntegrationCredentialProviders.UserMcpStateSecret,
            McpSecretBindings.EnvironmentTarget, "STATE", null, "State", null, true);
        Assert.Throws<ArgumentException>(() => McpSecretBindings.Normalize([binding], "stdio"));
        Assert.Equal("secret_state", McpSecretBindings.SecretNameFor("server_state"));

        var arguments = new Dictionary<string, object?> { ["x"] = "{{secret:server_state}}" };
        var resolved = McpSecretPlaceholders.Resolve(arguments,
            new Dictionary<string, string> { ["server_state"] = "private" }, out var missing);
        Assert.DoesNotContain("private", resolved["x"]?.ToString());
        Assert.Equal(["server_state"], missing);
    }

    private static async Task<WebApplication> StartFakeRunnerAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/run", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await McpRunnerHost.ReadLaunchAsync(socket, context.RequestAborted);
            await socket.SendAsync(Encoding.UTF8.GetBytes("""{"type":"state","state":"{\"token\":\"abc\"}"}"""),
                WebSocketMessageType.Text, true, context.RequestAborted);
            await socket.SendAsync(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"result":{}}""" + "\n"),
                WebSocketMessageType.Binary, true, context.RequestAborted);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, context.RequestAborted);
        });
        await app.StartAsync();
        return app;
    }

    private static Uri RunUrl(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.First();
        return new Uri(address.Replace("http://", "ws://") + "/run");
    }

    private sealed class TestOwner(Guid ownerId) : Jarvis.Application.Conversations.ICurrentUser
    {
        public Guid OwnerId { get; } = ownerId;
    }
}
