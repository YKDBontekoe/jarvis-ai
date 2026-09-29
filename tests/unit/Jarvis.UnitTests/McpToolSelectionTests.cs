using Jarvis.Application.Integrations;
using Jarvis.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class McpToolSelectionTests
{
    [Fact]
    public void Normalize_accepts_star_or_exact_names()
    {
        Assert.Equal(["*"], McpToolSelection.Normalize([" * "]));
        Assert.Equal(["calendar_events_list", "calendar_event_create"],
            McpToolSelection.Normalize([" calendar_events_list ", "calendar_event_create", "calendar_events_list"]));
    }

    [Theory]
    [InlineData("*", "other")]
    [InlineData("has space")]
    [InlineData("")]
    public void Normalize_rejects_mixed_star_and_invalid_names(params string[] tools)
    {
        if (tools.Length == 1 && tools[0] == "")
            Assert.Throws<ArgumentException>(() => McpToolSelection.Normalize([]));
        else
            Assert.Throws<ArgumentException>(() => McpToolSelection.Normalize(tools));
    }

    [Fact]
    public void Normalize_rejects_more_than_80_tools()
    {
        var tools = Enumerable.Range(0, 81).Select(index => $"tool_{index}").ToArray();
        Assert.Throws<ArgumentException>(() => McpToolSelection.Normalize(tools));
    }

    [Fact]
    public void Apply_adds_removes_and_replaces()
    {
        Assert.Equal(["a", "b"], McpToolSelection.Apply(["a"], "add", ["b"]));
        Assert.Equal(["*"], McpToolSelection.Apply(["a"], "add", ["*"]));
        Assert.Equal(["*"], McpToolSelection.Apply(["*"], "add", ["b"]));
        Assert.Equal(["b"], McpToolSelection.Apply(["a", "b"], "remove", ["a"]));
        Assert.Equal(["*"], McpToolSelection.Apply(["a"], "replace", ["*"]));
        Assert.Throws<ArgumentException>(() => McpToolSelection.Apply(["*"], "remove", ["a"]));
        Assert.Throws<ArgumentException>(() => McpToolSelection.Apply(["a"], "remove", ["a"]));
    }

    [Fact]
    public void Host_filter_cannot_widen_operator_allowlist()
    {
        Assert.Null(McpToolSelection.ApplyHostFilter(null, ["*"], "replace", ["*"]));
        Assert.Null(McpToolSelection.ApplyHostFilter(null, ["*"], "add", ["list_issues"]));
        Assert.Equal(["list_issues"], McpToolSelection.ApplyHostFilter(null, ["*"], "replace", ["list_issues"])!);
        Assert.Equal(["list_issues"], McpToolSelection.ApplyHostFilter(null, ["list_issues", "create_issue"], "remove", ["create_issue"])!);
        Assert.Throws<ArgumentException>(() =>
            McpToolSelection.ApplyHostFilter(null, ["list_issues"], "replace", ["delete_repo"]));
        Assert.Throws<ArgumentException>(() => McpToolSelection.ApplyHostFilter(null, ["*"], "remove", ["list_issues"]));
    }

    [Fact]
    public void Restrict_intersects_owner_filter_with_operator_tools()
    {
        Assert.Equal(["b"], McpToolSelection.Restrict(["a", "b", "c"], ["b", "d"]));
        Assert.Equal(["a", "b"], McpToolSelection.Restrict(["*"], ["a", "b"]));
        Assert.Equal(["a", "b"], McpToolSelection.Restrict(["a", "b"], null));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://example.com/mcp")]
    [InlineData("https://localhost/secret")]
    [InlineData("https://10.1.1.1/secret")]
    public void Resource_uris_reject_local_and_insecure_targets(string uri)
    {
        Assert.Throws<ArgumentException>(() => McpResourceUris.Validate(uri));
    }

    [Theory]
    [InlineData("repo://issues/1")]
    [InlineData("https://tools.example.net/docs/1")]
    public void Resource_uris_allow_remote_and_custom_schemes(string uri)
    {
        McpResourceUris.Validate(uri);
    }
}

public sealed class McpServerControlTests
{
    [Fact]
    public async Task Enabled_flag_defaults_on_and_can_be_paused()
    {
        var store = new InMemoryCredentialStore();
        var registry = new UserMcpServerRegistry(store, NullLogger<UserMcpServerRegistry>.Instance);
        var owner = Guid.NewGuid();
        var id = "jarvis-mcp-" + Guid.NewGuid().ToString("N");
        await store.SaveSecretAsync(owner, id, "server_config",
            """{"name":"Calendar","endpoint":"https://example.com/mcp","allowedTools":["list_events"]}""",
            CancellationToken.None);

        var listed = await registry.ListAsync(owner, CancellationToken.None);
        Assert.True(listed.Single().Enabled);

        var paused = await registry.SetEnabledAsync(owner, id, false, CancellationToken.None);
        Assert.False(paused!.Enabled);
        Assert.Equal(["list_events"], paused.AllowedTools);
        Assert.False((await registry.ListAsync(owner, CancellationToken.None)).Single().Enabled);
    }

    [Fact]
    public async Task Host_policy_round_trips_pause_and_tool_filter()
    {
        var store = new InMemoryCredentialStore();
        var policy = new OwnerMcpPolicyStore(store);
        var owner = Guid.NewGuid();
        var next = McpToolSelection.NextHostOverride(null, ["*"], false, "replace", ["list_issues"]);
        await policy.SaveAsync(owner, "github", next, CancellationToken.None);

        var saved = await policy.GetAsync(owner, CancellationToken.None);
        Assert.False(saved["GitHub"].Enabled);
        Assert.Equal(["list_issues"], saved["github"].AllowedTools);

        var resumed = McpToolSelection.NextHostOverride(saved["github"], ["*"], true, null, null);
        await policy.SaveAsync(owner, "github", resumed, CancellationToken.None);
        var again = await policy.GetAsync(owner, CancellationToken.None);
        Assert.True(again["github"].Enabled);
        Assert.Equal(["list_issues"], again["github"].AllowedTools);
    }
}
