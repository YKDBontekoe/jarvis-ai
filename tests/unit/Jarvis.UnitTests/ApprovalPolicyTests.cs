using Jarvis.Application.Approvals;
using Jarvis.Application.Audit;
using Jarvis.Application.Settings;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ToolRiskPolicyTests
{
    [Theory]
    [InlineData("DiscoverMcpServerTools", ToolRisk.ReadOnly)]
    [InlineData("ReadMcpResourceAsync", ToolRisk.ReadOnly)]
    [InlineData("GetMcpPrompt", ToolRisk.ReadOnly)]
    [InlineData("RunAutomation", ToolRisk.ReversibleLocal)]
    [InlineData("ProposeGraphFact", ToolRisk.ReversibleLocal)]
    [InlineData("GetDeviceLocation", ToolRisk.SensitiveRead)]
    [InlineData("SendWhatsAppMessage", ToolRisk.Outbound)]
    [InlineData("InvokeMcpTool", ToolRisk.Outbound)]
    [InlineData("browser_click", ToolRisk.Outbound)]
    [InlineData("automation_channel_message", ToolRisk.Outbound)]
    [InlineData("RemoveMcpServer", ToolRisk.Outbound)]
    [InlineData("ForgetMemory", ToolRisk.Destructive)]
    [InlineData("MergeGraphEntities", ToolRisk.Destructive)]
    [InlineData("RunMission", ToolRisk.Unknown)]
    public void Known_tools_have_an_explicit_class(string tool, ToolRisk expected) =>
        Assert.Equal(expected, ToolRiskPolicy.Classify(tool));

    [Theory]
    [InlineData("SomethingNew")]
    [InlineData("")]
    [InlineData(null)]
    public void Unclassified_tools_stay_unknown_so_they_keep_asking(string? tool) =>
        Assert.Equal(ToolRisk.Unknown, ToolRiskPolicy.Classify(tool));

    [Fact]
    public void Only_read_only_and_reversible_classes_can_run_without_asking_below_the_autonomous_level()
    {
        foreach (var risk in Enum.GetValues<ToolRisk>())
            Assert.Equal(risk is ToolRisk.ReadOnly or ToolRisk.ReversibleLocal, ToolRiskPolicy.CanAutoApprove(risk));
    }
}

public sealed class ApprovalPolicyTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000a99a");
    private static readonly DateTimeOffset Noon = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static ApprovalPolicyRequest Call(string tool, bool background = false, bool mcpReadOnly = false) =>
        new(Owner, tool, background, mcpReadOnly, Guid.NewGuid());

    [Fact]
    public async Task Read_only_tools_run_without_asking_in_chat_and_in_tasks()
    {
        var world = new World();

        var chat = await world.Policy().EvaluateAsync(Call("ReadMcpResource"), default);
        var task = await world.Policy().EvaluateAsync(Call("GetMcpPrompt", background: true), default);

        Assert.True(chat.AutoApprove);
        Assert.True(task.AutoApprove);
        Assert.Equal(ToolRisk.ReadOnly, chat.Risk);
    }

    [Fact]
    public async Task Reversible_changes_run_unattended_only_inside_a_task_at_the_full_level()
    {
        var world = new World();

        Assert.False((await world.Policy().EvaluateAsync(Call("ProposeGraphFact"), default)).AutoApprove);
        Assert.True((await world.Policy().EvaluateAsync(Call("ProposeGraphFact", background: true), default)).AutoApprove);

        await world.Save(new AutonomySettings(Level: AutonomyLevels.Standard));
        Assert.False((await world.Policy().EvaluateAsync(Call("ProposeGraphFact", background: true), default)).AutoApprove);
        Assert.True((await world.Policy().EvaluateAsync(Call("ReadMcpResource"), default)).AutoApprove);
    }

    [Theory]
    [InlineData("SendWhatsAppMessage")]
    [InlineData("ForgetMemory")]
    [InlineData("DeleteExpense")]
    [InlineData("RunCodingTask")]
    [InlineData("GetDeviceLocation")]
    [InlineData("InvokeMcpTool")]
    [InlineData("RunMission")]
    [InlineData("SomethingNew")]
    public async Task Outbound_destructive_private_and_unknown_actions_always_ask(string tool)
    {
        var world = new World();

        var chat = await world.Policy().EvaluateAsync(Call(tool), default);
        var task = await world.Policy().EvaluateAsync(Call(tool, background: true), default);

        Assert.False(chat.AutoApprove);
        Assert.False(task.AutoApprove);
        Assert.Empty(world.Audit);
    }

    [Fact]
    public async Task A_servers_read_only_claim_is_honoured_only_while_the_owner_allows_it()
    {
        var world = new World();
        Assert.True((await world.Policy().EvaluateAsync(Call("list_events", mcpReadOnly: true), default)).AutoApprove);
        Assert.False((await world.Policy().EvaluateAsync(Call("list_events"), default)).AutoApprove);

        await world.Save(new AutonomySettings(AutoApproveMcpReadHints: false));
        var refused = await world.Policy().EvaluateAsync(Call("list_events", mcpReadOnly: true), default);

        Assert.False(refused.AutoApprove);
        // Built-in read-only tools are a separate switch.
        Assert.True((await world.Policy().EvaluateAsync(Call("ReadMcpResource"), default)).AutoApprove);
    }

    [Theory]
    [InlineData(false, AutonomyLevels.Full, true)]
    [InlineData(true, AutonomyLevels.AskEverything, true)]
    [InlineData(true, AutonomyLevels.Full, true)]
    public async Task Switching_things_off_means_everything_asks(bool enabled, string level, bool readOnlyOff)
    {
        var world = new World();
        await world.Save(new AutonomySettings(Enabled: enabled, Level: level, AutoApproveReadOnly: !readOnlyOff));

        var decision = await world.Policy().EvaluateAsync(Call("ReadMcpResource", background: true), default);

        Assert.False(decision.AutoApprove);
        Assert.NotEmpty(decision.Reason);
    }

    [Fact]
    public async Task The_daily_limit_falls_back_to_asking_and_resets_the_next_day()
    {
        var world = new World();
        await world.Save(new AutonomySettings(MaxAutoApprovalsPerDay: 2));

        Assert.True((await world.Policy().EvaluateAsync(Call("ReadMcpResource"), default)).AutoApprove);
        Assert.True((await world.Policy().EvaluateAsync(Call("ReadMcpResource"), default)).AutoApprove);
        var third = await world.Policy().EvaluateAsync(Call("ReadMcpResource"), default);

        Assert.False(third.AutoApprove);
        Assert.Contains("daily limit", third.Reason);
        Assert.Equal(2, world.Audit.Count);

        world.Now = Noon.AddDays(1);
        Assert.True((await world.Policy().EvaluateAsync(Call("ReadMcpResource"), default)).AutoApprove);
    }

    [Fact]
    public async Task Audit_records_the_tool_risk_and_reason_but_never_arguments()
    {
        var world = new World();

        await world.Policy().EvaluateAsync(Call("ReadMcpResource", background: true), default);

        var entry = Assert.Single(world.Audit);
        Assert.Equal("ReadMcpResource", entry.Tool);
        Assert.Equal("approval.policy_auto_approved", entry.Action);
        Assert.Contains("ReadOnly", entry.Metadata);
        Assert.DoesNotContain("arguments", entry.Metadata, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Only_an_explicit_read_only_hint_without_a_destructive_flag_counts_for_integration_tools()
    {
        Assert.False(Jarvis.Mcp.McpToolHost.DeclaresReadOnly(null));
        Assert.False(Jarvis.Mcp.McpToolHost.DeclaresReadOnly(new ModelContextProtocol.Protocol.ToolAnnotations()));
        Assert.True(Jarvis.Mcp.McpToolHost.DeclaresReadOnly(
            new ModelContextProtocol.Protocol.ToolAnnotations { ReadOnlyHint = true }));
        Assert.False(Jarvis.Mcp.McpToolHost.DeclaresReadOnly(
            new ModelContextProtocol.Protocol.ToolAnnotations { ReadOnlyHint = true, DestructiveHint = true }));
        Assert.False(Jarvis.Mcp.McpToolHost.DeclaresReadOnly(
            new ModelContextProtocol.Protocol.ToolAnnotations { ReadOnlyHint = false }));
    }

    [Fact]
    public void Autonomy_level_and_limits_are_validated()
    {
        Assert.Equal(AutonomyLevels.Full, AutonomySettings.Default.Level);
        Assert.Throws<ArgumentException>(() => new AutonomySettings(Level: "yolo").Normalize());
        Assert.Throws<ArgumentException>(() => new AutonomySettings(MaxAutoApprovalsPerDay: -1).Normalize());
        Assert.Throws<ArgumentException>(() => new AutonomySettings(MaxAutoApprovalsPerDay: 5_000).Normalize());
        Assert.Equal(AutonomyLevels.AskEverything,
            new AutonomySettings(Level: AutonomyLevels.AskEverything).Normalize().Level);
    }

    private sealed class World
    {
        public InMemorySettingsStore Settings { get; } = new();
        public DateTimeOffset Now { get; set; } = Noon;
        public List<(string Tool, string Action, string Metadata)> Audit { get; } = [];

        public Task Save(AutonomySettings settings) =>
            Settings.SaveAsync(Owner, SettingsSections.Autonomy, settings, default);

        public ApprovalPolicy Policy() => new(Settings, new RecordingAudit(Audit), new FixedTime(Now));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingAudit(List<(string Tool, string Action, string Metadata)> events) : IAuditEventStore
    {
        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass,
            bool success, Guid? approvalId, string? metadataJson, CancellationToken cancellationToken,
            Guid? agentRunId = null)
        {
            events.Add((tool, action, metadataJson ?? string.Empty));
            return Task.FromResult(new AuditEventRecord(Guid.NewGuid(), agentRunId, tool, action, riskClass,
                approvalId, DateTimeOffset.UtcNow, success, metadataJson));
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditEventRecord>>([]);
    }
}
