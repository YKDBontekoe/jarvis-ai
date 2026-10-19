using Jarvis.Application.Approvals;
using Jarvis.Application.Attention;
using Jarvis.Application.Decisions;
using Jarvis.Application.Inbox;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Decisions;
using Jarvis.Domain.Inbox;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class AttentionQueueTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fa31");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fa32");
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private static ToolApprovalRecord Approval(string tool = "SendEmail") => new(Guid.NewGuid(), Owner, Guid.NewGuid(), null, "r",
        "c", tool, "{}", "pending", null, "none", null, Now.AddMinutes(-5), null);

    private static JarvisTaskRecord Task(string title, string status, DateTimeOffset? completed = null) => new(Guid.NewGuid(), Owner,
        title, "p", status, "w", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-2), null, completed, "It broke");

    private static ReminderRecord Reminder(string status, DateTimeOffset due) =>
        new(Guid.NewGuid(), Owner, "Call mum", due, "w", status, Now.AddDays(-1), null);

    private static Commitment Promise(string text, DateOnly? due, bool suggested = false, string status = CommitmentStatuses.Open) =>
        new(Guid.NewGuid(), Owner, CommitmentDirections.IOwe, "Rosa", text, due, status, suggested, CommitmentSources.Chat, null,
            null, null, Now, Now);

    private static Decision Choice(string title, DateOnly reviewOn, bool? outcome = null) =>
        new(Guid.NewGuid(), Owner, title, null, "It works", .7, reviewOn, outcome, null, null, null, Now, Now);

    private static AttentionFacts Facts(IReadOnlyList<ToolApprovalRecord>? approvals = null, IReadOnlyList<JarvisTaskRecord>? tasks = null,
        IReadOnlyList<ReminderRecord>? reminders = null, IReadOnlyList<Commitment>? promises = null, IReadOnlyList<Decision>? decisions = null) =>
        new(approvals ?? [], tasks ?? [], reminders ?? [], promises ?? [], decisions ?? [], Today, Now);

    [Fact]
    public void The_queue_orders_by_what_blocks_Jarvis_then_what_failed_then_what_is_late_then_today_then_review()
    {
        var items = AttentionRules.Build(Facts(
            approvals: [Approval()],
            tasks: [Task("Paused task", "needs_approval"), Task("Broken task", "failed", Now.AddHours(-1))],
            reminders: [Reminder("failed", Now.AddHours(-3))],
            promises: [Promise("Late promise", Today.AddDays(-3)), Promise("Due today", Today), Promise("Maybe", null, suggested: true)],
            decisions: [Choice("Switch bank", Today.AddDays(-1))]));

        Assert.Equal(["approval", "task_approval", "problem", "problem"], items.Take(4).Select(i => i.Kind));
        var rest = items.Skip(4).ToArray();
        Assert.Equal("Late promise", rest[0].Title);
        Assert.Equal(3, rest[0].DaysOverdue);
        Assert.Equal("late", rest[1].Urgency);
        Assert.Equal("Due today", rest[2].Title);
        Assert.Equal("suggested_promise", rest[^1].Kind);
    }

    [Fact]
    public void Only_current_obligations_are_listed()
    {
        var items = AttentionRules.Build(Facts(
            promises: [Promise("Tomorrow", Today.AddDays(1)), Promise("Undated", null), Promise("Done", Today, status: CommitmentStatuses.Done)],
            decisions: [Choice("Resolved", Today.AddDays(-1), outcome: true), Choice("Not yet", Today.AddDays(2))],
            tasks: [Task("Old failure", "failed", Now.AddDays(-10)), Task("Finished", "completed", Now)],
            reminders: [Reminder("failed", Now.AddDays(-10)), Reminder("pending", Now)]));

        Assert.Empty(items);
    }

    [Fact]
    public void Possible_promises_collapse_into_one_review_item()
    {
        var items = AttentionRules.Build(Facts(promises: [Promise("A", Today, suggested: true), Promise("B", null, suggested: true)]));

        var item = Assert.Single(items);
        Assert.Equal("2 possible promises to review", item.Title);
        Assert.Contains("Nothing counts until you accept it", item.Detail);
    }

    [Fact]
    public void Locked_kinds_are_never_put_away_but_others_can_be_and_come_back_when_the_time_is_up()
    {
        var items = AttentionRules.Build(Facts(approvals: [Approval()], promises: [Promise("Send the deck", Today)]));
        var promise = items.Single(i => i.Kind == "commitment");
        var state = new AttentionState { Snoozed = { [promise.Id] = Now.AddDays(1), [items[0].Id] = Now.AddDays(1) } };

        var queue = AttentionRules.Assemble(items, state, Now);

        Assert.Equal(["approval"], queue.Items.Select(i => i.Kind));
        Assert.Equal(1, queue.Snoozed);
        Assert.Contains("1 need you", queue.Summary);

        var later = AttentionRules.Assemble(items, state, Now.AddDays(2));
        Assert.Equal(2, later.Items.Count);
        Assert.Equal(0, later.Snoozed);
    }

    [Fact]
    public void An_empty_queue_says_so_and_mentions_what_was_put_away()
    {
        Assert.Equal("Nothing needs you right now.", AttentionRules.Assemble([], new AttentionState(), Now).Summary);
        var promise = AttentionRules.Build(Facts(promises: [Promise("X", Today)])).Single();
        var queue = AttentionRules.Assemble([promise], new AttentionState { Snoozed = { [promise.Id] = Now.AddDays(1) } }, Now);
        Assert.Equal("Nothing needs you right now. 1 put away for later.", queue.Summary);
    }

    [Fact]
    public void The_queue_is_bounded_per_kind_and_overall()
    {
        var many = Enumerable.Range(0, 50).Select(i => Promise($"P{i}", Today.AddDays(-i))).ToArray();
        var items = AttentionRules.Build(Facts(promises: many));

        Assert.Equal(AttentionRules.MaxPerKind, items.Count);
        Assert.Equal("P49", items[0].Title);
    }

    [Fact]
    public async Task The_service_snoozes_validates_and_is_private_to_each_owner()
    {
        var store = new InMemorySettingsStore();
        var approval = Approval();
        var approvals = Fake<IToolApprovalStore>.Create(("ListActionableAsync", args => (Guid)args[0]! == Owner ? new[] { approval } : []));
        var tasks = Fake<IJarvisTaskService>.Create(("ListAsync", _ => Array.Empty<JarvisTaskRecord>()));
        var reminders = Fake<IReminderService>.Create(("ListAsync", _ => Array.Empty<ReminderRecord>()));
        var promise = Promise("Send the deck", Today);
        var commitments = Fake<ICommitmentService>.Create(("TodayAsync", _ => Today),
            ("ListAsync", args => (Guid)args[0]! == Owner ? new[] { promise } : Array.Empty<Commitment>()));
        var decisions = Fake<IDecisionRepository>.Create(("ListAsync", _ => Array.Empty<Decision>()));
        var service = new AttentionService(approvals, tasks, reminders, commitments, decisions, store, new Clock());

        var queue = await service.GetAsync(Owner, default);
        var commitmentId = queue.Items.Single(i => i.Kind == "commitment").Id;
        var approvalId = queue.Items.Single(i => i.Kind == "approval").Id;

        Assert.Equal("days", (await service.SnoozeAsync(Owner, commitmentId, 0, default)).Field);
        Assert.Equal("days", (await service.SnoozeAsync(Owner, commitmentId, 15, default)).Field);
        Assert.Equal("id", (await service.SnoozeAsync(Owner, approvalId, 1, default)).Field);
        Assert.True((await service.SnoozeAsync(Owner, "commitment:" + Guid.NewGuid(), 1, default)).NotFound);

        var snoozed = await service.SnoozeAsync(Owner, commitmentId, 2, default);
        Assert.Equal(["approval"], snoozed.Value!.Items.Select(i => i.Kind));
        Assert.Empty((await service.GetAsync(Other, default)).Items);

        var back = await service.UnsnoozeAsync(Owner, commitmentId, default);
        Assert.Equal(2, back.Value!.Items.Count);
        Assert.True((await service.UnsnoozeAsync(Owner, commitmentId, default)).NotFound);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
