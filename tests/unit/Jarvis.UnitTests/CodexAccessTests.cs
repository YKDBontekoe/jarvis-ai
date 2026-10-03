using Jarvis.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CodexAccessTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    private static HashSet<string> Disabled(IEnumerable<string> arguments)
    {
        var list = arguments.ToList();
        return list.Select((argument, index) => (argument, index))
            .Where(item => item.argument == "--disable").Select(item => list[item.index + 1]).ToHashSet();
    }

    [Fact]
    public void Defaults_open_the_shell_workspace_writes_and_network()
    {
        var access = CodexAccess.From(Config());
        Assert.Equal(CodexAccess.WorkspaceWrite, access.Sandbox);
        Assert.True(access.AllowNetwork);
        Assert.True(access.AllowShell);

        var disabled = Disabled(CodexCliChatClient.CreateAppServerStart("codex", "/tmp/scratch", true, access)
            .ArgumentList);
        foreach (var feature in new[] { "shell_tool", "shell_snapshot", "code_mode_host", "skill_search",
                     "image_generation", "apps", "plugins" })
            Assert.DoesNotContain(feature, disabled);
        // Features that drive a browser or desktop by themselves, or spawn agents, stay off.
        foreach (var feature in new[] { "computer_use", "browser_use", "browser_use_external", "in_app_browser",
                     "multi_agent", "multi_agent_v2", "standalone_web_search" })
            Assert.Contains(feature, disabled);
    }

    [Fact]
    public void Strict_profile_restores_the_locked_down_behavior()
    {
        var disabled = Disabled(CodexCliChatClient.CreateAppServerStart("codex", "/tmp/scratch", false,
            CodexAccess.Strict).ArgumentList);
        foreach (var feature in new[] { "shell_tool", "shell_snapshot", "code_mode_host", "skill_search",
                     "image_generation", "apps", "plugins" })
            Assert.Contains(feature, disabled);
        Assert.Equal("readOnly", (string?)CodexAccess.Strict.TurnSandboxPolicy("/tmp/scratch")["type"]);
        Assert.False((bool)CodexAccess.Strict.TurnSandboxPolicy("/tmp/scratch")["networkAccess"]!);
    }

    [Fact]
    public void Workspace_write_policy_limits_writes_to_scratch_and_configured_roots()
    {
        var access = CodexAccess.From(Config(("Codex:Access:WritableRoots:0", "/srv/work")));
        var policy = access.TurnSandboxPolicy("/tmp/scratch");
        Assert.Equal("workspaceWrite", (string?)policy["type"]);
        Assert.Equal(["/tmp/scratch", "/srv/work"],
            policy["writableRoots"]!.AsArray().Select(root => (string)root!).ToArray());
        Assert.True((bool)policy["networkAccess"]!);
    }

    [Fact]
    public void Full_access_and_config_overrides_are_honored()
    {
        var access = CodexAccess.From(Config(("Codex:Access:Sandbox", "danger-full-access"),
            ("Codex:Access:AllowShell", "false"), ("Codex:Access:DisabledFeatures:0", "image_generation")));
        Assert.Equal("dangerFullAccess", (string?)access.TurnSandboxPolicy("/tmp/s")["type"]);
        var disabled = access.DisabledFeatures(webSearch: true).ToHashSet();
        Assert.Contains("shell_tool", disabled);
        Assert.Contains("image_generation", disabled);
        Assert.DoesNotContain("code_mode_host", disabled);
    }

    [Theory]
    [InlineData("Codex:Access:Sandbox", "wide-open")]
    [InlineData("Codex:Access:WritableRoots:0", "relative/dir")]
    public void Invalid_settings_are_rejected_at_startup(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => CodexAccess.From(Config((key, value))));

    [Fact]
    public void Proxy_settings_reach_the_child_only_when_network_is_allowed()
    {
        Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://proxy.test:3128");
        try
        {
            var open = CodexCliChatClient.CreateAppServerStart("codex", "/tmp/s", true, CodexAccess.Open);
            var strict = CodexCliChatClient.CreateAppServerStart("codex", "/tmp/s", true, CodexAccess.Strict);
            Assert.Equal("http://proxy.test:3128", open.Environment["HTTPS_PROXY"]);
            Assert.False(strict.Environment.ContainsKey("HTTPS_PROXY"));
            // Application secrets never reach the model process.
            Assert.False(open.Environment.ContainsKey("ConnectionStrings__jarvis"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
        }
    }

    [Fact]
    public void Prompt_mentions_the_shell_only_when_it_is_allowed()
    {
        var messages = new[] { new ChatMessage(ChatRole.User, "hi") };
        Assert.Contains("scratch workspace", CodexCliChatClient.BuildPrompt(messages, null, [], false, true).Text);
        Assert.DoesNotContain("scratch workspace", CodexCliChatClient.BuildPrompt(messages, null, [], false).Text);
    }

    [Theory]
    [InlineData("commandExecution", "RunCommand")]
    [InlineData("fileChange", "EditFiles")]
    [InlineData("agentMessage", null)]
    [InlineData("reasoning", null)]
    public void Codex_shell_and_file_items_are_reported_as_named_steps(string itemType, string? expected)
    {
        var json = "{\"method\":\"item/started\",\"params\":{\"item\":{\"type\":\"" + itemType +
                   "\",\"id\":\"exec-1\",\"command\":\"echo secret\"}}}";
        using var message = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(expected, CodexCliChatClient.NativeWorkTool(message.RootElement));
    }
}
