using Jarvis.Application.Approvals;
using Jarvis.Application.Audit;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class StandingApprovalTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();

    [Theory]
    [InlineData("ForgetMemory", "memory.forget", "Forgetting memories")]
    [InlineData("ForgetMemoryAsync", "memory.forget", "Forgetting memories")]
    [InlineData("CorrectGraphFact", "graph.change", "Changing the knowledge graph")]
    [InlineData("ForgetGraphEntity", "graph.change", "Changing the knowledge graph")]
    [InlineData("browser_navigate", "browser", "Using the browser")]
    [InlineData("BrowseTheWeb", "browser", "Using the browser")]
    [InlineData("AddMcpServer", "integrations.add", "Adding integrations")]
    [InlineData("AddMcpStdioServer", "integrations.add", "Adding integrations")]
    [InlineData("RemoveMcpServer", "integrations.remove", "Removing integrations")]
    [InlineData("automation_channel_message", "automations.channel_message", "Sending messages for automations")]
    public void Known_tools_share_a_stable_category(string tool, string key, string label)
    {
        var category = ApprovalCategories.Resolve(tool, "{}");

        Assert.Equal(key, category.Key);
        Assert.Equal(label, category.Label);
        Assert.True(category.CanRemember);
    }

    [Fact]
    public void Mcp_calls_are_scoped_to_the_server_and_tool()
    {
        var category = ApprovalCategories.Resolve("InvokeMcpTool",
            """{"server":"GitHub","toolName":"create_issue","argumentsJson":"{}"}""");

        Assert.Equal("mcp.invoke.github.create_issue", category.Key);
        Assert.Equal("Using create_issue on GitHub", category.Label);
        Assert.True(category.CanRemember);

        var missing = ApprovalCategories.Resolve("InvokeMcpTool", """{"server":"GitHub"}""");
        Assert.False(missing.CanRemember);
    }

    [Fact]
    public void Direct_tools_each_get_their_own_category()
    {
        var category = ApprovalCategories.Resolve("github_create_issue", "{}");

        Assert.Equal("tool.github_create_issue", category.Key);
        Assert.Equal("Using github create issue", category.Label);
        Assert.True(category.CanRemember);
    }

    private sealed class MutableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task A_time_limited_grant_stops_working_and_disappears_when_it_expires()
    {
        var time = new MutableTime(new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero));
        var service = new StandingApprovalService(new InMemorySettingsStore(), new RecordingAudit(), time);
        var category = ApprovalCategories.Resolve("ForgetMemory", "{}");

        Assert.Equal(StandingApprovalGrantResult.Granted,
            await service.GrantAsync(Owner, category, default, new GrantTerms(TimeSpan.FromHours(2))));
        time.Now = time.Now.AddHours(1);
        Assert.True(await service.IsGrantedAsync(Owner, category.Key, default));
        Assert.Equal(time.Now.AddHours(1), Assert.Single(await service.ListAsync(Owner, default)).ExpiresAt);

        time.Now = time.Now.AddHours(1);
        Assert.False(await service.IsGrantedAsync(Owner, category.Key, default));
        Assert.Empty(await service.ListAsync(Owner, default));
    }

    [Fact]
    public async Task A_task_scoped_grant_covers_background_runs_but_not_chats()
    {
        var service = new StandingApprovalService(new InMemorySettingsStore(), new RecordingAudit());
        var category = ApprovalCategories.Resolve("ForgetMemory", "{}");

        await service.GrantAsync(Owner, category, default, new GrantTerms(Scope: StandingApprovalScopes.Tasks));

        Assert.False(await service.IsGrantedAsync(Owner, category.Key, default));
        Assert.True(await service.IsGrantedAsync(Owner, category.Key, default, backgroundTask: true));
        Assert.Equal(StandingApprovalScopes.Tasks, Assert.Single(await service.ListAsync(Owner, default)).Scope);
    }

    [Fact]
    public async Task Granting_again_without_limits_makes_it_permanent_and_everywhere()
    {
        var service = new StandingApprovalService(new InMemorySettingsStore(), new RecordingAudit());
        var category = ApprovalCategories.Resolve("ForgetMemory", "{}");
        await service.GrantAsync(Owner, category, default,
            new GrantTerms(TimeSpan.FromHours(1), StandingApprovalScopes.Tasks));

        await service.GrantAsync(Owner, category, default);

        var grant = Assert.Single(await service.ListAsync(Owner, default));
        Assert.Null(grant.ExpiresAt);
        Assert.Null(grant.Scope);
        Assert.True(await service.IsGrantedAsync(Owner, category.Key, default));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(24 * 366, null)]
    [InlineData(1, "everywhere")]
    public async Task Nonsense_terms_are_refused_and_store_nothing(int hours, string? scope)
    {
        var service = new StandingApprovalService(new InMemorySettingsStore(), new RecordingAudit());
        var category = ApprovalCategories.Resolve("ForgetMemory", "{}");

        var result = await service.GrantAsync(Owner, category, default, new GrantTerms(TimeSpan.FromHours(hours), scope));

        Assert.Equal(StandingApprovalGrantResult.InvalidTerms, result);
        Assert.Empty(await service.ListAsync(Owner, default));
    }

    [Fact]
    public async Task Grants_stored_before_expiry_existed_keep_working()
    {
        var settings = new InMemorySettingsStore();
        var category = ApprovalCategories.Resolve("ForgetMemory", "{}");
        await settings.SaveAsync(Owner, Jarvis.Application.Settings.SettingsSections.StandingApprovals,
            new StandingApprovalSettings([new StandingApprovalGrant(category.Key, category.Label, DateTimeOffset.UtcNow)]),
            default);

        var service = new StandingApprovalService(settings, new RecordingAudit());

        Assert.True(await service.IsGrantedAsync(Owner, category.Key, default));
    }

    [Fact]
    public async Task Grants_are_owner_scoped_and_can_be_revoked()
    {
        var settings = new InMemorySettingsStore();
        var audit = new RecordingAudit();
        var service = new StandingApprovalService(settings, audit);
        var category = ApprovalCategories.Resolve("ForgetMemory", "{}");
        var other = Guid.CreateVersion7();

        Assert.Equal(StandingApprovalGrantResult.Granted, await service.GrantAsync(Owner, category, CancellationToken.None));
        Assert.Equal(StandingApprovalGrantResult.Granted, await service.GrantAsync(Owner, category, CancellationToken.None));
        Assert.True(await service.IsGrantedAsync(Owner, category.Key, CancellationToken.None));
        Assert.False(await service.IsGrantedAsync(other, category.Key, CancellationToken.None));

        var listed = await service.ListAsync(Owner, CancellationToken.None);
        var grant = Assert.Single(listed);
        Assert.Equal("memory.forget", grant.Category);
        Assert.Equal("Forgetting memories", grant.Label);

        Assert.True(await service.RevokeAsync(Owner, category.Key, CancellationToken.None));
        Assert.False(await service.IsGrantedAsync(Owner, category.Key, CancellationToken.None));
        Assert.False(await service.RevokeAsync(Owner, category.Key, CancellationToken.None));
        Assert.Contains(audit.Actions, action => action == "approval.category_granted");
        Assert.Contains(audit.Actions, action => action == "approval.category_revoked");
    }

    [Fact]
    public async Task A_category_that_cannot_be_named_is_refused()
    {
        var service = new StandingApprovalService(new InMemorySettingsStore(), new NullAudit());
        var category = ApprovalCategories.Resolve("InvokeMcpTool", "{}");

        Assert.Equal(StandingApprovalGrantResult.NotAllowed,
            await service.GrantAsync(Owner, category, CancellationToken.None));
        Assert.Empty(await service.ListAsync(Owner, CancellationToken.None));
    }

    [Fact]
    public async Task The_grant_list_stops_at_the_cap()
    {
        var service = new StandingApprovalService(new InMemorySettingsStore(), new NullAudit());
        for (var index = 0; index < StandingApprovalService.MaxGrants; index++)
        {
            var category = ApprovalCategories.Resolve($"Tool{index}", "{}");
            Assert.Equal(StandingApprovalGrantResult.Granted,
                await service.GrantAsync(Owner, category, CancellationToken.None));
        }

        var extra = ApprovalCategories.Resolve("OneMoreTool", "{}");
        Assert.Equal(StandingApprovalGrantResult.TooMany,
            await service.GrantAsync(Owner, extra, CancellationToken.None));
        Assert.Equal(StandingApprovalService.MaxGrants, (await service.ListAsync(Owner, CancellationToken.None)).Count);
    }

    private sealed class RecordingAudit : IAuditEventStore
    {
        public List<string> Actions { get; } = [];

        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass,
            bool success, Guid? approvalId, string? metadataJson, CancellationToken cancellationToken,
            Guid? agentRunId = null)
        {
            Actions.Add(action);
            return Task.FromResult(new AuditEventRecord(Guid.NewGuid(), agentRunId, tool, action, riskClass,
                approvalId, DateTimeOffset.UtcNow, success, metadataJson));
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditEventRecord>>([]);
    }
}
