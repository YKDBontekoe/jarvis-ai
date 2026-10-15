using System.Net;
using System.Text;
using Jarvis.Api.McpRunner;
using Jarvis.Application.Approvals;
using Jarvis.Application.Integrations;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Mcp;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CustomMcpServerTests
{
    private const string Header = """
        # /// script
        # requires-python = ">=3.11"
        # dependencies = [
        #     "mcp==2.2.0",
        # ]
        # ///
        """;

    [Fact]
    public void Template_passes_the_rules_and_pins_the_sdk_exactly()
    {
        var check = CustomMcpScriptValidator.Validate(CustomMcpTemplate.Source);

        Assert.True(check.Ok, string.Join(" ", check.Errors));
        Assert.Contains(CustomMcpTemplate.McpRequirement, check.Dependencies);
        Assert.Matches(@"^\d+\.\d+\.\d+$", CustomMcpTemplate.McpVersion);
        Assert.Contains(CustomMcpTemplate.McpRequirement, CustomMcpTemplate.Guidance);
        Assert.Contains("from mcp.server.mcpserver import MCPServer", CustomMcpTemplate.Source);
    }

    [Theory]
    [InlineData("\"httpx>=0.28\"")]
    [InlineData("\"httpx\"")]
    [InlineData("\"httpx[http2]==0.28.1\"")]
    [InlineData("\"httpx==0.28.1; python_version>'3.10'\"")]
    [InlineData("\"httpx @ https://example.com/httpx.whl\"")]
    [InlineData("\"git+https://github.com/x/y\"")]
    [InlineData("\"./local\"")]
    public void Every_dependency_must_be_one_pinned_release(string dependency)
    {
        var source = $"""
            # /// script
            # requires-python = ">=3.11"
            # dependencies = ["mcp==2.2.0", {dependency}]
            # ///
            print("hi")
            """;

        var check = CustomMcpScriptValidator.Validate(source);

        Assert.False(check.Ok);
        Assert.Contains(check.Errors, error => error.Contains("Pin", StringComparison.Ordinal));
    }

    [Fact]
    public void Refuses_tool_uv_tables_that_could_redirect_the_package_index()
    {
        var check = CustomMcpScriptValidator.Validate("""
            # /// script
            # requires-python = ">=3.11"
            # dependencies = ["mcp==2.2.0"]
            # [tool.uv]
            # index-url = "https://evil.example/simple"
            # ///
            print(1)
            """);

        Assert.False(check.Ok);
        Assert.Contains(check.Errors, error => error.Contains("Only requires-python", StringComparison.Ordinal));
    }

    [Fact]
    public void Refuses_code_without_metadata_without_the_sdk_or_with_two_blocks()
    {
        Assert.False(CustomMcpScriptValidator.Validate("import os\n").Ok);
        Assert.False(CustomMcpScriptValidator.Validate("""
            # /// script
            # requires-python = ">=3.11"
            # dependencies = ["httpx==0.28.1"]
            # ///
            """).Ok);
        Assert.False(CustomMcpScriptValidator.Validate(Header + "\n" + Header).Ok);
        Assert.False(CustomMcpScriptValidator.Validate("""
            # /// script
            # dependencies = ["mcp==2.2.0"]
            # ///
            """).Ok);
    }

    [Fact]
    public void Refuses_oversized_code_and_hashes_normalized_line_endings()
    {
        var big = Header + "\n# " + new string('x', CustomMcpScriptValidator.MaxSourceBytes);
        Assert.False(CustomMcpScriptValidator.Validate(big).Ok);

        var unix = CustomMcpScriptValidator.Validate(Header + "\nprint(1)\n");
        var windows = CustomMcpScriptValidator.Validate((Header + "\nprint(1)\n").Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.True(unix.Ok);
        Assert.Equal(unix.Sha256, windows.Sha256);
        Assert.Equal(64, unix.Sha256.Length);
    }

    [Fact]
    public void Keeps_the_newest_revisions_and_always_the_approved_one()
    {
        var revisions = Enumerable.Range(1, 12).ToArray();

        Assert.Equal([1, 2], CustomMcpServerManager.RevisionsToTrim(revisions, activeRevision: 12).Order());
        var dropped = CustomMcpServerManager.RevisionsToTrim(revisions, activeRevision: 1);
        Assert.DoesNotContain(1, dropped);
        Assert.Equal(2, dropped.Count);
        Assert.Empty(CustomMcpServerManager.RevisionsToTrim([1, 2, 3], null));
    }

    [Fact]
    public void Secret_requests_become_environment_bindings_and_blocked_variables_are_refused()
    {
        var bindings = CustomMcpServerManager.SecretBindings([new CustomMcpSecretRequest("WEATHER_API_KEY", "Weather key", null)]);
        var binding = Assert.Single(bindings);
        Assert.Equal("weather_api_key", binding.SecretName);
        Assert.Equal(McpSecretBindings.EnvironmentTarget, binding.Target);

        Assert.Throws<ArgumentException>(() =>
            CustomMcpServerManager.SecretBindings([new CustomMcpSecretRequest("PYTHONPATH", null, null)]));
    }

    [Fact]
    public void Agent_stdio_servers_cannot_use_the_self_built_command()
    {
        Assert.Throws<ArgumentException>(() =>
            McpStdioCommandValidator.Normalize(McpStdioCommandValidator.CustomScriptCommand, [Guid.NewGuid().ToString("N")]));
    }

    [Fact]
    public async Task Only_a_matching_catalog_name_registers_a_self_built_server()
    {
        var registry = new UserMcpServerRegistry(new InMemoryCredentialStore(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserMcpServerRegistry>.Instance);
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => registry.AddDefinitionAsync(owner,
            new McpServerDefinition("Weather", "stdio", null, McpStdioCommandValidator.CustomScriptCommand,
                [id.ToString("N")], ["*"], [], "custom:" + Guid.NewGuid().ToString("N")), CancellationToken.None));

        var server = await registry.AddDefinitionAsync(owner,
            new McpServerDefinition("Weather", "stdio", null, McpStdioCommandValidator.CustomScriptCommand,
                [id.ToString("N")], ["*"],
                CustomMcpServerManager.SecretBindings([new CustomMcpSecretRequest("WEATHER_API_KEY", null, null)]),
                CustomMcpNames.CatalogName(id)), CancellationToken.None);

        Assert.Equal(McpStdioCommandValidator.CustomScriptCommand, server.Command);
        Assert.Equal([id.ToString("N")], server.Arguments);
        Assert.Equal("weather_api_key", Assert.Single(server.Secrets!).Name);

        var updated = await registry.SetSecretBindingsAsync(owner, server.Id, [], CancellationToken.None);
        Assert.Empty(updated!.Secrets!);
    }

    [Fact]
    public void Runner_accepts_a_script_only_through_uv_with_its_own_arguments_and_hash()
    {
        var check = CustomMcpScriptValidator.Validate(CustomMcpTemplate.Source);
        var launch = McpRunnerHost.Validate(new McpRunnerLaunch("uv", [],
            new Dictionary<string, string> { ["WEATHER_API_KEY"] = "key" },
            new McpRunnerScript("python", CustomMcpTemplate.Source, check.Sha256)));

        Assert.Equal("uv", launch.Command);
        Assert.Equal(McpRunnerHost.ScriptArguments, launch.Arguments);

        Assert.Throws<ArgumentException>(() => McpRunnerHost.Validate(new McpRunnerLaunch("uv", [],
            new Dictionary<string, string>(), new McpRunnerScript("python", CustomMcpTemplate.Source, new string('0', 64)))));
        Assert.Throws<ArgumentException>(() => McpRunnerHost.Validate(new McpRunnerLaunch("bash", [],
            new Dictionary<string, string>(), new McpRunnerScript("python", CustomMcpTemplate.Source, check.Sha256))));
        Assert.Throws<ArgumentException>(() => McpRunnerHost.Validate(new McpRunnerLaunch("uv", ["run", "evil.py"],
            new Dictionary<string, string>(), new McpRunnerScript("python", CustomMcpTemplate.Source, check.Sha256))));
        Assert.Throws<ArgumentException>(() => McpRunnerHost.Validate(new McpRunnerLaunch("uv", [],
            new Dictionary<string, string> { ["UV_INDEX_URL"] = "https://evil.example" },
            new McpRunnerScript("python", CustomMcpTemplate.Source, check.Sha256))));
        var broken = "print('no metadata')";
        Assert.Throws<ArgumentException>(() => McpRunnerHost.Validate(new McpRunnerLaunch("uv", [],
            new Dictionary<string, string>(), new McpRunnerScript("python", broken, CustomMcpScriptValidator.Sha256(broken)))));
    }

    [Fact]
    public void Diagnostics_keep_only_the_tail_and_drop_key_values()
    {
        var tail = new StderrTail(16);
        tail.Append(Encoding.UTF8.GetBytes("0123456789"));
        tail.Append(Encoding.UTF8.GetBytes("abcdefghij"));
        Assert.Equal("456789abcdefghij", tail.Text());

        var redacted = McpRunnerHost.Redact("Traceback: bad key sk-live-123456 rejected", ["sk-live-123456"]);
        Assert.DoesNotContain("sk-live-123456", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }

    [Fact]
    public void Self_built_tools_always_ask_and_belong_to_the_integrations_group()
    {
        Assert.Equal(ToolRisk.Outbound, ToolRiskPolicy.Classify("TestCustomMcpServer"));
        Assert.Equal(ToolRisk.Outbound, ToolRiskPolicy.Classify("ActivateCustomMcpServer"));
        Assert.False(ToolRiskPolicy.CanAutoApprove(ToolRiskPolicy.Classify("ActivateCustomMcpServer")));
        Assert.Equal("integrations", Jarvis.Agents.ToolGroups.ForTool("WriteCustomMcpServer")?.Id);
    }

    [Fact]
    public void Featured_catalog_names_are_unique()
    {
        var names = McpFeaturedCatalog.Categories.SelectMany(category => category.Names).ToArray();
        Assert.NotEmpty(names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Usage_counts_calls_of_the_apps_own_tools()
    {
        var now = DateTimeOffset.UtcNow;
        var usage = McpAppUsageSummary.For(
        [
            (now.AddDays(-2), ["weather_current", "CreateReminder"]),
            (now.AddHours(-1), ["weather_current", "weather_current"]),
            (now, ["ListTasks"])
        ], ["weather_current"]);

        Assert.Equal(3, usage.CallsLast7Days);
        Assert.Equal(now.AddHours(-1), usage.LastUsedAt);
        Assert.Equal(0, McpAppUsageSummary.For([(now, ["x"])], []).CallsLast7Days);
    }

    [Fact]
    public void Connection_failures_are_sorted_into_fixable_kinds()
    {
        Assert.Equal("unauthorized", McpToolHost.ErrorKind(
            new HttpRequestException("no", null, HttpStatusCode.Unauthorized)));
        Assert.Equal("timeout", McpToolHost.ErrorKind(new TaskCanceledException()));
        Assert.Equal("unreachable", McpToolHost.ErrorKind(new HttpRequestException("dns")));
        Assert.Equal("crashed", McpToolHost.ErrorKind(new IOException("pipe closed")));
        Assert.DoesNotContain("token", McpToolHost.ExplainIssue("unauthorized"), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class McpOAuthTokenTests
{
    [Fact]
    public void Parses_access_refresh_and_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var tokens = McpOAuthTokens.Parse("""{"access_token":"a","refresh_token":"r","expires_in":3600}""", now);

        Assert.Equal("a", tokens.AccessToken);
        Assert.Equal("r", tokens.RefreshToken);
        Assert.Equal(now.AddHours(1), tokens.ExpiresAt);
        Assert.Throws<InvalidOperationException>(() => McpOAuthTokens.Parse("""{"token":"x"}""", now));
    }

    [Fact]
    public void Refreshes_only_when_close_to_expiry_and_renewable()
    {
        var now = DateTimeOffset.UtcNow;
        var soon = new Dictionary<string, string>
        {
            [McpOAuthTokens.ExpiresAtSecret] = now.AddSeconds(30).ToString("O"),
            [McpOAuthTokens.RefreshTokenSecret] = "r"
        };
        Assert.True(McpOAuthTokens.NeedsRefresh(soon, now));
        Assert.False(McpOAuthTokens.NeedsRefresh(new Dictionary<string, string>(soon)
        {
            [McpOAuthTokens.ExpiresAtSecret] = now.AddHours(1).ToString("O")
        }, now));
        Assert.False(McpOAuthTokens.NeedsRefresh(new Dictionary<string, string>
        {
            [McpOAuthTokens.ExpiresAtSecret] = now.ToString("O")
        }, now));
    }

    [Fact]
    public async Task Renewal_keeps_the_refresh_token_when_the_server_does_not_rotate_it()
    {
        var handler = new RecordingHandler("""{"access_token":"new","expires_in":600}""");
        using var http = new HttpClient(handler);
        var secrets = new Dictionary<string, string>
        {
            [McpOAuthTokens.RefreshTokenSecret] = "refresh-1",
            [McpOAuthTokens.TokenEndpointSecret] = "https://auth.example.com/token",
            [McpOAuthTokens.ClientIdSecret] = "client"
        };

        var tokens = await McpOAuthTokens.RefreshAsync(http, secrets, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal("new", tokens.AccessToken);
        Assert.Equal("refresh-1", tokens.RefreshToken);
        Assert.Contains("grant_type=refresh_token", handler.Body);
        Assert.Contains("refresh_token=refresh-1", handler.Body);
    }

    [Fact]
    public async Task Renewal_refuses_a_private_token_endpoint()
    {
        using var http = new HttpClient(new RecordingHandler("{}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => McpOAuthTokens.RefreshAsync(http,
            new Dictionary<string, string>
            {
                [McpOAuthTokens.RefreshTokenSecret] = "r",
                [McpOAuthTokens.TokenEndpointSecret] = "http://127.0.0.1/token"
            }, DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task Saving_a_renewable_token_stores_what_renewal_needs()
    {
        var store = new InMemoryCredentialStore();
        var owner = Guid.NewGuid();
        await McpOAuthTokens.SaveAsync(store, owner, "github",
            new McpOAuthTokenSet("a", "r", DateTimeOffset.UtcNow.AddHours(1)), "https://auth.example.com/token",
            "client", null, CancellationToken.None);

        var secrets = (await store.GetSecretsAsync(owner, "github", CancellationToken.None))!;
        Assert.Equal("a", secrets["token"]);
        Assert.Equal("r", secrets[McpOAuthTokens.RefreshTokenSecret]);
        Assert.True(secrets.ContainsKey(McpOAuthTokens.ExpiresAtSecret));

        await McpOAuthTokens.SaveAsync(store, owner, "github", new McpOAuthTokenSet("b", null, null), null, null,
            null, CancellationToken.None);
        secrets = (await store.GetSecretsAsync(owner, "github", CancellationToken.None))!;
        Assert.Equal("b", secrets["token"]);
        Assert.False(secrets.ContainsKey(McpOAuthTokens.ExpiresAtSecret));
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
