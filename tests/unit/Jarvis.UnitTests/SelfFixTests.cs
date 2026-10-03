using Jarvis.Agents;
using Jarvis.Application.Diagnostics;
using Jarvis.Application.Workflows;
using Jarvis.Infrastructure.Coding;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class SelfFixTests
{
    [Theory]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData(".git/config")]
    [InlineData("apps/secrets/token.txt")]
    [InlineData(".env")]
    [InlineData(".env.production")]
    [InlineData("certs/server.pem")]
    [InlineData("codex/auth.json")]
    public void Blocked_paths_are_never_published(string path) =>
        Assert.True(CodingPathPolicy.IsBlocked(path));

    [Theory]
    [InlineData("src/Jarvis.Api/Endpoints/ConversationEndpoints.cs")]
    [InlineData(".env.example")]
    [InlineData("docs/backend/api-reference.md")]
    public void Ordinary_paths_are_allowed(string path) => Assert.False(CodingPathPolicy.IsBlocked(path));

    [Theory]
    [InlineData("src/Jarvis.Application/Approvals/ToolApproval.cs")]
    [InlineData("src/Jarvis.Api/Program.cs")]
    [InlineData("src/Jarvis.Infrastructure/Persistence/Migrations/2026_Add.cs")]
    [InlineData("src/Jarvis.Mcp/McpToolHost.cs")]
    [InlineData("infra/compose/Dockerfile")]
    [InlineData("src/Jarvis.AppHost/ProductionDeployment.cs")]
    [InlineData("scripts/deploy/remote-up.sh")]
    public void Security_sensitive_paths_are_flagged_for_review(string path) =>
        Assert.True(CodingPathPolicy.IsProtected(path));

    [Fact]
    public void Ordinary_code_is_not_flagged() =>
        Assert.False(CodingPathPolicy.IsProtected("apps/mobile/lib/tasks_screen.dart"));

    [Fact]
    public void Branch_names_are_unique_per_attempt_and_safe()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var first = GitHubCodingPullRequestService.BranchName(id, "Skip MCP servers that fail to start!",
            new DateTimeOffset(2026, 9, 30, 6, 30, 1, TimeSpan.Zero));
        var second = GitHubCodingPullRequestService.BranchName(id, "Skip MCP servers that fail to start!",
            new DateTimeOffset(2026, 9, 30, 6, 31, 9, TimeSpan.Zero));
        Assert.Equal("jarvis/fix-55555555-0930063001-skip-mcp-servers-that-fail-to-start", first);
        Assert.NotEqual(first, second);
        Assert.Matches("^jarvis/[a-z0-9/-]+$", first);
        Assert.StartsWith("jarvis/fix-55555555-0930063001", GitHubCodingPullRequestService.BranchName(id, "!!!",
            new DateTimeOffset(2026, 9, 30, 6, 30, 1, TimeSpan.Zero)));
    }

    [Fact]
    public void Titles_come_from_the_task_not_the_models_narration()
    {
        Assert.Equal("Skip broken servers",
            GitHubCodingPullRequestService.TitleFromTask("Preamble line\nChange: Skip broken servers\n\nProblem: x"));
        Assert.Equal("Fix the greeting", GitHubCodingPullRequestService.TitleFromTask("Fix the greeting\nmore detail"));
        Assert.Equal("Jarvis change", GitHubCodingPullRequestService.TitleFromTask("  \n "));
    }

    [Fact]
    public void Check_state_summarizes_the_worst_result()
    {
        static CodingCheck Check(string status, string? conclusion) => new("c", status, conclusion);
        Assert.Equal("none", GitHubCodingPullRequestService.SummarizeChecks([]));
        Assert.Equal("success", GitHubCodingPullRequestService.SummarizeChecks(
            [Check("completed", "success"), Check("completed", "skipped")]));
        Assert.Equal("pending", GitHubCodingPullRequestService.SummarizeChecks(
            [Check("completed", "success"), Check("in_progress", null)]));
        Assert.Equal("failure", GitHubCodingPullRequestService.SummarizeChecks(
            [Check("in_progress", null), Check("completed", "failure")]));
    }

    [Fact]
    public void Pull_request_body_explains_and_flags_sensitive_files()
    {
        var run = new CodingRunRecord(Guid.NewGuid(), Guid.NewGuid(), "jarvis", "Fix the thing", "completed", "/w", null,
            [], "Changed X.", null, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var body = GitHubCodingPullRequestService.BuildBody(run,
        [
            new CodingDiffFile("src/A.cs", "modified", false),
            new CodingDiffFile("src/Jarvis.Api/Program.cs", "modified", true)
        ]);
        Assert.Contains("Changed X.", body);
        Assert.Contains("Fix the thing", body);
        Assert.Contains("`src/A.cs` (modified)", body);
        Assert.Contains("**security-sensitive**", body);
        Assert.Contains("not merged automatically", body);
    }

    [Fact]
    public void Fault_log_dedupes_repeats_and_keeps_newest_first()
    {
        var log = new RecentFaultLog();
        log.Record("Error", "Jarvis.A", "First {Thing}", "System.Exception", ["at A"]);
        log.Record("Warning", "Jarvis.B", "Second", null, []);
        log.Record("Error", "Jarvis.A", "First {Thing}", "System.Exception", ["at A"]);
        var recent = log.Recent(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal("First {Thing}", recent[0].Template);
        Assert.Equal(2, recent[0].Count);
        Assert.Single(log.Recent(1));
    }

    [Fact]
    public void Fault_log_is_bounded()
    {
        var log = new RecentFaultLog();
        for (var i = 0; i < 500; i++) log.Record("Error", "Jarvis.A", $"Template {i}", null, []);
        Assert.Equal(100, log.Recent(1_000).Count);
    }

    [Fact]
    public void Fix_prompt_carries_the_rules_the_problem_and_recent_faults()
    {
        var faults = new[]
        {
            new FaultEntry(DateTimeOffset.UtcNow, "Error", "Jarvis.Mcp", "Skipping MCP server {ServerName}",
                "System.TimeoutException", ["at Jarvis.Mcp.McpToolHost.InitializeAsync in McpToolHost.cs:line 204"], 3)
        };
        var prompt = SelfFixAgentTools.BuildPrompt("Skip slow servers", "Turns hang.", "Turns finish.", faults);
        Assert.Contains("Change: Skip slow servers", prompt);
        Assert.Contains("Turns hang.", prompt);
        Assert.Contains("Turns finish.", prompt);
        Assert.Contains("AGENTS.md", prompt);
        Assert.Contains("Skipping MCP server {ServerName}", prompt);
        Assert.Contains("McpToolHost.cs:line 204", prompt);
        Assert.Contains("Do not modify .github", prompt);
    }
}
