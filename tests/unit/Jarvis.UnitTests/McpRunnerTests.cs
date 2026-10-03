using System.Net;
using System.Net.WebSockets;
using Jarvis.Api.McpRunner;
using Jarvis.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class McpRunnerTests
{
    private const string Token = "runner-test-token-0123456789-abcdefgh";

    [Fact]
    public void Launches_only_npx_or_uvx_packages_with_allowed_variables()
    {
        var ok = McpRunnerHost.Validate(new McpRunnerLaunch("npx", ["-y", "@brave/brave-search-mcp-server@2.1.3"],
            new Dictionary<string, string> { ["BRAVE_API_KEY"] = "key" }));
        Assert.Equal("npx", ok.Command);

        Assert.Throws<ArgumentException>(() => McpRunnerHost.Validate(
            new McpRunnerLaunch("github-mcp-server", ["stdio"], new Dictionary<string, string>())));
        Assert.Throws<ArgumentException>(() => McpRunnerHost.Validate(
            new McpRunnerLaunch("bash", ["-c", "id"], new Dictionary<string, string>())));
        Assert.Throws<ArgumentException>(() => McpRunnerHost.Validate(new McpRunnerLaunch("npx", ["-y", "pkg@1.0.0"],
            new Dictionary<string, string> { ["NODE_OPTIONS"] = "--require /tmp/x.js" })));
    }

    [Fact]
    public void Connectors_get_a_clean_environment_that_their_keys_cannot_override()
    {
        var settings = new RunnerSettings(4, TimeSpan.FromMinutes(5), "/work", "/cache", "/usr/bin");
        var start = McpRunnerHost.StartInfo(new McpRunnerLaunch("npx", ["-y", "pkg@1.0.0"],
            new Dictionary<string, string> { ["API_KEY"] = "secret" }), settings, "/work/abc");

        Assert.Equal("secret", start.Environment["API_KEY"]);
        Assert.Equal("/work/abc", start.Environment["HOME"]);
        Assert.Equal("/usr/bin", start.Environment["PATH"]);
        Assert.Equal("/cache/npm", start.Environment["npm_config_cache"]);
        Assert.False(start.Environment.ContainsKey("McpRunner__Token"));
        Assert.Equal(["-y", "pkg@1.0.0"], start.ArgumentList);
    }

    [Fact]
    public async Task Rejects_connections_without_the_token()
    {
        await using var app = await StartAsync();
        var url = RunUrl(app);

        using var anonymous = new ClientWebSocket();
        var refused = await Assert.ThrowsAsync<WebSocketException>(() => anonymous.ConnectAsync(url, default));
        Assert.Contains("401", refused.Message);

        using var wrong = new ClientWebSocket();
        wrong.Options.SetRequestHeader("Authorization", "Bearer " + new string('x', 40));
        await Assert.ThrowsAsync<WebSocketException>(() => wrong.ConnectAsync(url, default));
    }

    [Fact]
    public async Task Closes_with_a_policy_error_for_a_command_it_does_not_run()
    {
        await using var app = await StartAsync();
        var transport = new McpRunnerClientTransport(new McpRunnerOptions(RunUrl(app), Token),
            new McpRunnerLaunch("bash", ["-c", "id"], new Dictionary<string, string>()), "test",
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        // The runner refuses the launch and closes, so the MCP handshake fails instead of hanging.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            ModelContextProtocol.Client.McpClient.CreateAsync(transport, cancellationToken: timeout.Token));
        Assert.False(timeout.IsCancellationRequested);
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["McpRunner:Token"] = Token });
        var app = McpRunnerHost.Build(builder);
        await app.StartAsync();
        return app;
    }

    private static Uri RunUrl(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.First();
        return new Uri(address.Replace("http://", "ws://") + "/run");
    }
}
