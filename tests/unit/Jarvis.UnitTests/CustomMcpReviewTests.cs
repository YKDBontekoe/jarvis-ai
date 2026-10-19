using Jarvis.Application.Integrations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CustomMcpReviewTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fa01");
    private static readonly Guid AppId = Guid.Parse("01996b8c-6000-7000-8000-00000000fa10");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static string Source(string body, string dependencies = "\"mcp==2.2.0\"") => $"""
        # /// script
        # requires-python = ">=3.11"
        # dependencies = [{dependencies}]
        # ///
        {body}
        """;

    private static CustomMcpServerDetail Detail(string draft, string? active = null, CustomMcpTestResult? test = null)
    {
        var draftCheck = CustomMcpScriptValidator.Validate(draft);
        var activeCheck = active is null ? null : CustomMcpScriptValidator.Validate(active);
        var summary = new CustomMcpServerSummary(AppId, "Water", null, 3, draftCheck.Sha256, activeCheck?.Sha256,
            active is null ? null : 2, "srv", [], test, Now, Now);
        return new CustomMcpServerDetail(summary, draftCheck.Source, activeCheck?.Source, []);
    }

    [Fact]
    public void A_first_version_shows_every_line_as_added_and_says_what_the_test_did_not_prove()
    {
        var draft = Source("print('a')\nprint('b')");
        var sha = CustomMcpScriptValidator.Validate(draft).Sha256;

        var review = CustomMcpReviewBuilder.Build(Detail(draft, null,
            new CustomMcpTestResult(true, sha, ["log_water"], null, null, Now)));

        Assert.True(review.FirstVersion);
        Assert.Equal(0, review.Removed);
        Assert.True(review.Added >= 2);
        Assert.Equal(CustomMcpReviewBuilder.TestPassed, review.TestStatus);
        Assert.Contains("does not prove each tool works", review.TestLabel);
        Assert.Equal(["log_water"], review.Tools);
        Assert.Contains(review.Limits, l => l.Contains("not a guarantee"));
    }

    [Fact]
    public void A_revision_shows_only_the_changed_lines_and_dependency_differences()
    {
        var active = Source("print('a')\nprint('b')\nprint('c')", "\"mcp==2.2.0\", \"httpx==0.28.1\"");
        var draft = Source("print('a')\nprint('B')\nprint('c')", "\"mcp==2.2.0\", \"pydantic==2.9.0\"");

        var review = CustomMcpReviewBuilder.Build(Detail(draft, active));

        Assert.False(review.FirstVersion);
        Assert.Contains(review.Changes, l => l is { Kind: "remove", Text: "print('b')" });
        Assert.Contains(review.Changes, l => l is { Kind: "add", Text: "print('B')" });
        Assert.DoesNotContain(review.Changes, l => l.Text == "print('a')");
        Assert.Equal(["pydantic==2.9.0"], review.DependenciesAdded);
        Assert.Equal(["httpx==0.28.1"], review.DependenciesRemoved);
    }

    [Fact]
    public void A_test_of_older_code_is_stale_and_never_counts_as_a_pass()
    {
        var draft = Source("print('a')");
        var review = CustomMcpReviewBuilder.Build(Detail(draft, null,
            new CustomMcpTestResult(true, "0000", ["x"], null, null, Now)));

        Assert.Equal(CustomMcpReviewBuilder.TestStale, review.TestStatus);
        Assert.Empty(review.Tools);
        Assert.Contains("different code", review.TestLabel);
        Assert.Equal(CustomMcpReviewBuilder.TestNone, CustomMcpReviewBuilder.Build(Detail(draft)).TestStatus);
    }

    [Fact]
    public void A_tool_call_test_is_labelled_as_testing_one_tool_only()
    {
        var draft = Source("print('a')");
        var sha = CustomMcpScriptValidator.Validate(draft).Sha256;

        var review = CustomMcpReviewBuilder.Build(Detail(draft, null,
            new CustomMcpTestResult(true, sha, ["a", "b"], null, null, Now, "{\"ok\":true}")));

        Assert.StartsWith("Tool test:", review.TestLabel);
        Assert.Contains("Other tools are untested", review.TestLabel);
    }

    [Fact]
    public void Very_large_files_still_diff_quickly_by_line_sets()
    {
        var before = Enumerable.Range(0, 600).Select(i => $"line {i}").ToArray();
        var after = before.Append("brand new").Skip(1).ToArray();

        var diff = CustomMcpReviewBuilder.Diff(before, after);

        Assert.Single(diff, l => l.Kind == "add");
        Assert.Single(diff, l => l.Kind == "remove");
    }

    private sealed class World
    {
        public CustomMcpServerDetail Detail = null!;
        public bool Activated;

        public (ICustomMcpServerStore Store, IUserMcpServerRegistry Registry) Fakes => (
            Fake<ICustomMcpServerStore>.Create(
                ("GetAsync", _ => Detail),
                ("ActivateAsync", args =>
                {
                    Activated = true;
                    return Detail.Server with { ActiveSha256 = (string)args[2]!, ActiveRevision = Detail.Server.Revision };
                })),
            Fake<IUserMcpServerRegistry>.Create(("SetEnabledAsync", _ => (UserMcpServer?)null)));
    }

    private static World Untested()
    {
        var draft = Source("print('a')");
        return new World { Detail = Detail(draft) };
    }

    [Fact]
    public async Task Activation_without_a_reviewed_hash_is_refused_instead_of_taking_the_current_draft()
    {
        var world = Untested();
        var (store, registry) = world.Fakes;

        var error = await Assert.ThrowsAsync<CustomMcpReviewException>(() =>
            CustomMcpServerManager.ActivateAsync(Owner, AppId, null, store, registry, default));

        Assert.Equal("hash_required", error.Code);
        Assert.False(world.Activated);
    }

    [Fact]
    public async Task Untested_code_needs_a_reason_and_the_reason_is_returned_for_the_audit_trail()
    {
        var world = Untested();
        var (store, registry) = world.Fakes;
        var sha = world.Detail.Server.DraftSha256;

        var refused = await Assert.ThrowsAsync<CustomMcpReviewException>(() =>
            CustomMcpServerManager.ActivateAsync(Owner, AppId, sha, store, registry, default));
        var tooShort = await Assert.ThrowsAsync<CustomMcpReviewException>(() =>
            CustomMcpServerManager.ActivateAsync(Owner, AppId, sha, store, registry, default, "ok"));
        Assert.Equal("not_tested", refused.Code);
        Assert.Equal("not_tested", tooShort.Code);
        Assert.False(world.Activated);

        var (_, _, reason) = await CustomMcpServerManager.ActivateAsync(Owner, AppId, sha, store, registry, default,
            "It only does arithmetic and I read the code.");
        Assert.True(world.Activated);
        Assert.Equal("It only does arithmetic and I read the code.", reason);
    }

    [Fact]
    public async Task Code_that_passed_its_test_activates_without_a_reason_and_a_test_of_other_code_does_not_count()
    {
        var world = Untested();
        var sha = world.Detail.Server.DraftSha256;
        world.Detail = world.Detail with
        {
            Server = world.Detail.Server with { LastTest = new CustomMcpTestResult(true, "other", ["x"], null, null, Now) }
        };
        var (store, registry) = world.Fakes;
        await Assert.ThrowsAsync<CustomMcpReviewException>(() =>
            CustomMcpServerManager.ActivateAsync(Owner, AppId, sha, store, registry, default));

        world.Detail = world.Detail with
        {
            Server = world.Detail.Server with { LastTest = new CustomMcpTestResult(true, sha, ["x"], null, null, Now) }
        };
        var (_, _, reason) = await CustomMcpServerManager.ActivateAsync(Owner, AppId, sha, store, registry, default);

        Assert.Null(reason);
        Assert.True(world.Activated);
    }

    [Fact]
    public void The_builder_prompt_states_the_contract_and_forbids_outside_changes_by_default()
    {
        var (prompt, field, _) = CustomAppStarter.Compose(new CustomAppStarter.Answers(
            "track my water intake", "a number of glasses", null, "a weekly total", false));

        Assert.Null(field);
        Assert.Contains("track my water intake", prompt);
        Assert.Contains("What I will give it: a number of glasses", prompt);
        Assert.Contains("do not invent endpoints", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only read and calculate", prompt);
        Assert.Contains("Do not switch it on: I will review the code first.", prompt);

        var effectful = CustomAppStarter.Compose(new CustomAppStarter.Answers("post to my blog", null, "my blog API", null, true));
        Assert.Contains("ask me first", effectful.Prompt);
        Assert.Contains("Where its data comes from: my blog API", effectful.Prompt);
        Assert.Equal("purpose", CustomAppStarter.Compose(new CustomAppStarter.Answers(" ", null, null, null, false)).Field);
        Assert.Equal(4, CustomAppStarter.Examples.Count);
    }
}
