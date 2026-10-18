using Jarvis.Application.Approvals;
using Jarvis.Application.Decisions;
using Jarvis.Application.Inbox;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Attention;

public static class AttentionKinds
{
    public const string Approval = "approval";
    public const string TaskApproval = "task_approval";
    public const string Problem = "problem";
    public const string Commitment = "commitment";
    public const string Decision = "decision";
    public const string SuggestedPromise = "suggested_promise";

    /// <summary>Things Jarvis is waiting on, or that went wrong: they cannot be put away.</summary>
    public static bool Locked(string kind) => kind is Approval or TaskApproval or Problem;
}

public static class AttentionUrgency
{
    public const string Now = "now";
    public const string Late = "late";
    public const string Today = "today";
    public const string Review = "review";

    public static int Rank(string urgency) => urgency switch { Now => 0, Late => 1, Today => 2, _ => 3 };
}

/// <summary>One thing that needs the owner. <see cref="Destination"/> says where to deal with it.</summary>
public sealed record AttentionItem(string Id, string Kind, string Title, string Detail, string Urgency, string Destination,
    DateOnly? DueOn, int DaysOverdue, bool Locked);

public sealed record AttentionQueue(IReadOnlyList<AttentionItem> Items, int Snoozed, IReadOnlyDictionary<string, int> Counts,
    string Summary);

/// <summary>Items the owner put away until a time; the underlying records are never touched.</summary>
public sealed class AttentionState
{
    public Dictionary<string, DateTimeOffset> Snoozed { get; set; } = [];
}

public sealed record AttentionFacts(
    IReadOnlyList<ToolApprovalRecord> Approvals,
    IReadOnlyList<JarvisTaskRecord> Tasks,
    IReadOnlyList<ReminderRecord> Reminders,
    IReadOnlyList<Jarvis.Domain.Inbox.Commitment> Commitments,
    IReadOnlyList<Jarvis.Domain.Decisions.Decision> Decisions,
    DateOnly Today,
    DateTimeOffset Now);

/// <summary>
/// Builds the one queue behind "Needs you". Pure, so every rule is tested with a fixed day. Order: what Jarvis is
/// blocked on, what went wrong, what is late, what is due today, then what only needs a look.
/// </summary>
public static class AttentionRules
{
    public const int MaxPerKind = 20;
    public const int MaxTotal = 60;
    public static readonly TimeSpan RecentFailure = TimeSpan.FromDays(3);

    public static IReadOnlyList<AttentionItem> Build(AttentionFacts facts)
    {
        var items = new List<AttentionItem>();

        items.AddRange(facts.Approvals.OrderBy(a => a.CreatedAt).Take(MaxPerKind).Select(a =>
            new AttentionItem("approval:" + a.Id, AttentionKinds.Approval, "Approve or decline: " + a.ToolName,
                "Jarvis is waiting for your answer.", AttentionUrgency.Now, "approvals", null, 0, true)));

        items.AddRange(facts.Tasks.Where(t => t.Status == "needs_approval").OrderBy(t => t.CreatedAt).Take(MaxPerKind).Select(t =>
            new AttentionItem("task:" + t.Id, AttentionKinds.TaskApproval, t.Title, "This task paused for your approval.",
                AttentionUrgency.Now, "task:" + t.Id, null, 0, true)));

        items.AddRange(facts.Tasks.Where(t => t.Status == "failed" && t.CompletedAt >= facts.Now - RecentFailure)
            .OrderByDescending(t => t.CompletedAt).Take(MaxPerKind).Select(t =>
                new AttentionItem("task-failed:" + t.Id, AttentionKinds.Problem, t.Title, t.Summary ?? "This task failed.",
                    AttentionUrgency.Now, "task:" + t.Id, null, 0, true)));

        items.AddRange(facts.Reminders.Where(r => r.Status == "failed" && r.DueAt >= facts.Now - RecentFailure)
            .OrderByDescending(r => r.DueAt).Take(MaxPerKind).Select(r =>
                new AttentionItem("reminder-failed:" + r.Id, AttentionKinds.Problem, r.Title,
                    "This reminder could not be delivered.", AttentionUrgency.Now, "reminders", null, 0, true)));

        foreach (var c in HomeCommitments(facts))
            items.Add(c);

        items.AddRange(facts.Decisions.Where(d => !d.IsResolved && d.ReviewOn <= facts.Today)
            .OrderBy(d => d.ReviewOn).Take(MaxPerKind).Select(d =>
            {
                var overdue = facts.Today.DayNumber - d.ReviewOn.DayNumber;
                return new AttentionItem("decision:" + d.Id, AttentionKinds.Decision, "Review: " + d.Title,
                    $"You predicted “{d.Prediction}” at {Math.Round(d.Probability * 100)}%.",
                    overdue > 0 ? AttentionUrgency.Late : AttentionUrgency.Today, "decision:" + d.Id, d.ReviewOn, overdue, false);
            }));

        var suggested = facts.Commitments.Where(c => c.Suggested && c.Status == CommitmentStatuses.Open).ToArray();
        if (suggested.Length > 0)
            items.Add(new AttentionItem("suggested-promises", AttentionKinds.SuggestedPromise,
                suggested.Length == 1 ? "1 possible promise to review" : $"{suggested.Length} possible promises to review",
                "Jarvis noticed these in your messages. Nothing counts until you accept it.", AttentionUrgency.Review,
                "commitments", null, 0, false));

        return items.OrderBy(i => AttentionUrgency.Rank(i.Urgency)).ThenBy(i => KindRank(i.Kind)).ThenByDescending(i => i.DaysOverdue)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase).Take(MaxTotal).ToArray();
    }

    private static int KindRank(string kind) => kind switch
    {
        AttentionKinds.Approval => 0,
        AttentionKinds.TaskApproval => 1,
        AttentionKinds.Problem => 2,
        AttentionKinds.Commitment => 3,
        AttentionKinds.Decision => 4,
        _ => 5
    };

    private static IEnumerable<AttentionItem> HomeCommitments(AttentionFacts facts) =>
        facts.Commitments.Where(c => c is { Suggested: false, Status: CommitmentStatuses.Open, DueOn: not null } &&
                                     c.DueOn <= facts.Today)
            .OrderBy(c => c.DueOn).Take(MaxPerKind).Select(c =>
            {
                var overdue = facts.Today.DayNumber - c.DueOn!.Value.DayNumber;
                var who = c.Direction == CommitmentDirections.IOwe ? $"You owe {c.Counterparty}" : $"{c.Counterparty} owes you";
                return new AttentionItem("commitment:" + c.Id, AttentionKinds.Commitment, c.Description, who,
                    overdue > 0 ? AttentionUrgency.Late : AttentionUrgency.Today, "commitment:" + c.Id, c.DueOn, overdue, false);
            });

    public static AttentionQueue Assemble(IReadOnlyList<AttentionItem> all, AttentionState state, DateTimeOffset now)
    {
        var snoozed = all.Count(i => !i.Locked && state.Snoozed.TryGetValue(i.Id, out var until) && until > now);
        var shown = all.Where(i => i.Locked || !(state.Snoozed.TryGetValue(i.Id, out var until) && until > now)).ToArray();
        var counts = shown.GroupBy(i => i.Kind).ToDictionary(g => g.Key, g => g.Count());
        var summary = shown.Length == 0
            ? snoozed > 0 ? $"Nothing needs you right now. {snoozed} put away for later." : "Nothing needs you right now."
            : shown.Count(i => i.Urgency == AttentionUrgency.Now) is > 0 and var now1
                ? $"{shown.Length} need you; {now1} are waiting on your answer or went wrong."
                : $"{shown.Length} need you.";
        return new AttentionQueue(shown, snoozed, counts, summary);
    }
}

public sealed record AttentionOperation<T>(T? Value, string? Field = null, string? Message = null, bool NotFound = false)
{
    public bool Succeeded => Field is null && !NotFound;
    public static AttentionOperation<T> Ok(T value) => new(value);
    public static AttentionOperation<T> Invalid(string field, string message) => new(default, field, message);
    public static AttentionOperation<T> Missing() => new(default, null, null, true);
}

public interface IAttentionService
{
    Task<AttentionQueue> GetAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Puts an item away for 1 to 14 days. Approvals and problems cannot be put away.</summary>
    Task<AttentionOperation<AttentionQueue>> SnoozeAsync(Guid ownerId, string id, int days, CancellationToken cancellationToken);

    Task<AttentionOperation<AttentionQueue>> UnsnoozeAsync(Guid ownerId, string id, CancellationToken cancellationToken);
}

public sealed class AttentionService(
    IToolApprovalStore approvals,
    IJarvisTaskService tasks,
    IReminderService reminders,
    ICommitmentService commitments,
    IDecisionRepository decisions,
    IOwnerSettingsStore settings,
    TimeProvider? timeProvider = null) : IAttentionService
{
    public const int MaxSnoozeDays = 14;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<AttentionQueue> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var all = await LoadItemsAsync(ownerId, now, cancellationToken);
        var state = await LoadStateAsync(ownerId, now, cancellationToken);
        return AttentionRules.Assemble(all, state, now);
    }

    public async Task<AttentionOperation<AttentionQueue>> SnoozeAsync(Guid ownerId, string id, int days,
        CancellationToken cancellationToken)
    {
        if (days is < 1 or > MaxSnoozeDays)
            return AttentionOperation<AttentionQueue>.Invalid("days", $"Choose 1 to {MaxSnoozeDays} days.");
        var now = clock.GetUtcNow();
        var all = await LoadItemsAsync(ownerId, now, cancellationToken);
        var item = all.FirstOrDefault(i => i.Id == id);
        if (item is null) return AttentionOperation<AttentionQueue>.Missing();
        if (item.Locked)
            return AttentionOperation<AttentionQueue>.Invalid("id", "Approvals and problems stay here until you deal with them.");
        var state = await LoadStateAsync(ownerId, now, cancellationToken);
        state.Snoozed[id] = now.AddDays(days);
        await settings.SaveAsync(ownerId, SettingsSections.Attention, state, cancellationToken);
        return AttentionOperation<AttentionQueue>.Ok(AttentionRules.Assemble(all, state, now));
    }

    public async Task<AttentionOperation<AttentionQueue>> UnsnoozeAsync(Guid ownerId, string id, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var state = await LoadStateAsync(ownerId, now, cancellationToken);
        if (!state.Snoozed.Remove(id)) return AttentionOperation<AttentionQueue>.Missing();
        await settings.SaveAsync(ownerId, SettingsSections.Attention, state, cancellationToken);
        return AttentionOperation<AttentionQueue>.Ok(AttentionRules.Assemble(await LoadItemsAsync(ownerId, now, cancellationToken), state, now));
    }

    private async Task<IReadOnlyList<AttentionItem>> LoadItemsAsync(Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = await commitments.TodayAsync(ownerId, cancellationToken);
        var facts = new AttentionFacts(
            await approvals.ListActionableAsync(ownerId, cancellationToken),
            await tasks.ListAsync(ownerId, cancellationToken),
            await reminders.ListAsync(ownerId, cancellationToken),
            await commitments.ListAsync(ownerId, null, CommitmentStatuses.Open, true, cancellationToken),
            await decisions.ListAsync(ownerId, cancellationToken),
            today, now);
        return AttentionRules.Build(facts);
    }

    /// <summary>Snoozes that have run out are dropped so the document stays small.</summary>
    private async Task<AttentionState> LoadStateAsync(Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var state = await settings.GetAsync<AttentionState>(ownerId, SettingsSections.Attention, cancellationToken) ?? new AttentionState();
        state.Snoozed = state.Snoozed.Where(p => p.Value > now).ToDictionary(p => p.Key, p => p.Value);
        return state;
    }
}
