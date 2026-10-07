using System.Text.Json;
using Jarvis.Application.Approvals;
using Jarvis.Application.Audit;
using Jarvis.Application.Inbox;
using Jarvis.Application.Integrations;
using Jarvis.Application.Learning;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents.Learning;

/// <summary>
/// One heartbeat: reflect on new activity (continuous learning), keep the inbox triaged, then surface anything that
/// needs attention. Check-ins are deterministic and de-duplicated, stay silent during quiet hours, and never repeat an
/// alert. Within the owner's <see cref="AutonomySettings"/> a check-in can also start a read-only background task, such
/// as preparing for an upcoming meeting; a daily budget bounds how many.
/// </summary>
public sealed class HeartbeatService(
    ReflectionService reflection,
    IOwnerSettingsStore settingsStore,
    IDailyBriefingRepository briefings,
    IReminderRepository reminders,
    IToolApprovalStore approvals,
    IJarvisTaskRepository tasks,
    INotificationRepository notifications,
    IAuditEventStore audit,
    ICalendarFeed calendar,
    IJarvisTaskService taskService,
    IInboxService inbox,
    ICommitmentService commitments,
    ILogger<HeartbeatService> logger,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan MeetingPrepLead = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan MeetingPrepHorizon = TimeSpan.FromHours(2);
    private static readonly TimeSpan BudgetWindow = TimeSpan.FromHours(24);
    private const int MaxTriagePerRun = 3;
    private const int UrgentPriority = InboxPriorities.High;
    private static readonly TimeSpan ReminderHorizon = TimeSpan.FromHours(2);
    private static readonly TimeSpan StaleApproval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailedTaskWindow = TimeSpan.FromHours(24);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<HeartbeatOutcome> RunAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync<LearningSettings>(ownerId, SettingsSections.Learning,
            cancellationToken) ?? LearningSettings.Default;
        var autonomy = await settingsStore.GetAsync<AutonomySettings>(ownerId, SettingsSections.Autonomy,
            cancellationToken) ?? AutonomySettings.Default;
        var state = await settingsStore.GetAsync<HeartbeatState>(ownerId, LearningSections.HeartbeatState,
            cancellationToken) ?? new HeartbeatState();
        var now = _clock.GetUtcNow();

        var reflected = ReflectionOutcome.Nothing;
        DateTimeOffset? latestMessage = null;
        try
        {
            (reflected, latestMessage) = await reflection.ReflectAsync(ownerId, settings,
                state.LastReflectedMessageAt ?? now.AddDays(-1), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Heartbeat reflection failed for owner {OwnerId}.", ownerId);
        }

        var triaged = autonomy is { Enabled: true, TriageInbox: true }
            ? await TriageInboxAsync(ownerId, cancellationToken)
            : 0;

        var timeZone = await ResolveTimeZoneAsync(ownerId, cancellationToken);
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var quiet = settings.IsQuietHour(localNow.Hour);
        var notified = new List<CheckInItem>();
        var handledKeys = new List<string>();
        var taskStarts = (state.TaskStartedAt ?? []).Where(at => at > now - BudgetWindow).ToList();
        var started = 0;
        if (settings.ProactiveCheckIns && !quiet)
        {
            var known = state.Keys.ToHashSet(StringComparer.Ordinal);
            var candidates = (await CollectAsync(ownerId, now, timeZone, cancellationToken))
                .Concat(InsightItems(reflected))
                .Where(item => !known.Contains(item.Key))
                .ToList();
            var plan = HeartbeatPlanner.Plan(candidates, autonomy, taskStarts.Count);
            foreach (var item in plan.Start)
            {
                if (!await TryStartTaskAsync(ownerId, item, cancellationToken)) continue;
                started++;
                taskStarts.Add(now);
                handledKeys.Add(item.Key);
            }

            if (plan.DailyBudgetExhausted)
                await audit.AppendAsync(ownerId, "learning", "heartbeat.budget_exhausted", "low", true, null,
                    JsonSerializer.Serialize(new { limit = autonomy.MaxHeartbeatTasksPerDay }), cancellationToken);
            notified.AddRange(plan.Notify);
            handledKeys.AddRange(plan.Notify.Select(item => item.Key));
            if (notified.Count > 0)
                await notifications.CreateAsync(ownerId, "heartbeat.checkin",
                    notified.Count == 1 ? "Heads up" : $"Heads up — {notified.Count} things need you",
                    string.Join("\n", notified.Select(item => "• " + item.Text)), null, cancellationToken);
        }

        var summary = Summarize(reflected, notified.Count, quiet, started);
        var keys = state.Keys.Concat(handledKeys).TakeLast(HeartbeatState.MaxRememberedKeys).ToArray();
        await settingsStore.SaveAsync(ownerId, LearningSections.HeartbeatState, new HeartbeatState(now,
            latestMessage ?? state.LastReflectedMessageAt, keys, summary,
            taskStarts.TakeLast(HeartbeatState.MaxRememberedTaskStarts).ToArray()), cancellationToken);
        await audit.AppendAsync(ownerId, "learning", "heartbeat.ran", "low", true, null,
            JsonSerializer.Serialize(new
            {
                checkIns = notified.Count, quiet, learned = reflected.LearnedAnything, tasksStarted = started,
                inboxTriaged = triaged
            }), cancellationToken);
        return new HeartbeatOutcome(reflected, notified, quiet, summary, started);
    }

    /// <summary>Starts the proposed task. A failure leaves the check-in unhandled so a later heartbeat can retry it.</summary>
    private async Task<bool> TryStartTaskAsync(Guid ownerId, CheckInItem item, CancellationToken cancellationToken)
    {
        try
        {
            var task = await taskService.CreateAsync(ownerId, item.Task!.Title, item.Task.Prompt, cancellationToken);
            // Ids and the check-in kind only: titles and prompts can carry calendar or message text.
            await audit.AppendAsync(ownerId, "learning", "heartbeat.task_started", "low", true, null,
                JsonSerializer.Serialize(new { taskId = task.Id, kind = item.Key.Split(':')[0] }), cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Heartbeat could not start a background task for owner {OwnerId}.", ownerId);
            return false;
        }
    }

    /// <summary>
    /// Pulls new chats into the inbox, then lets the model summarise and draft replies for a few threads that need
    /// one. Drafts are saved on the thread; nothing is ever sent from here.
    /// </summary>
    private async Task<int> TriageInboxAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        try
        {
            await inbox.SyncAsync(ownerId, cancellationToken);
            var waiting = await inbox.ListAsync(ownerId, new HashSet<string> { InboxStates.NeedsReply },
                cancellationToken);
            var done = 0;
            foreach (var thread in waiting.Threads
                         .Where(thread => thread.State == InboxStates.NeedsReply && thread.TriagedAt is null)
                         .OrderByDescending(thread => thread.Priority)
                         .Take(MaxTriagePerRun))
                if ((await inbox.TriageAsync(thread.Id, ownerId, cancellationToken)).Succeeded)
                    done++;
            if (done > 0)
                await audit.AppendAsync(ownerId, "learning", "heartbeat.inbox_triaged", "low", true, null,
                    JsonSerializer.Serialize(new { count = done }), cancellationToken);
            return done;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Heartbeat inbox sync failed for owner {OwnerId}.", ownerId);
            return 0;
        }
    }

    private static IEnumerable<CheckInItem> InsightItems(ReflectionOutcome reflected) =>
        (reflected.Insights ?? []).Select(text =>
            new CheckInItem("insight:" + HeartbeatPlanner.Fingerprint(text), "Possible follow-up: " + text));

    internal async Task<IReadOnlyList<CheckInItem>> CollectAsync(Guid ownerId, DateTimeOffset now, TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var items = new List<CheckInItem>();
        foreach (var reminder in await reminders.ListRemindersAsync(ownerId, cancellationToken))
        {
            if (reminder.Status != "pending" || reminder.Place is not null || reminder.DueAt <= now || reminder.DueAt - now > ReminderHorizon) continue;
            var local = TimeZoneInfo.ConvertTime(reminder.DueAt, timeZone);
            items.Add(new CheckInItem($"reminder:{reminder.Id:N}", $"“{reminder.Title}” is due at {local:HH:mm}."));
        }
        foreach (var approval in await approvals.ListActionableAsync(ownerId, cancellationToken))
        {
            if (approval.Status != "pending" || now - approval.CreatedAt < StaleApproval) continue;
            items.Add(new CheckInItem($"approval:{approval.Id:N}",
                $"I'm still waiting for your approval to run {approval.ToolName}."));
        }
        foreach (var task in await tasks.ListAsync(ownerId, cancellationToken))
        {
            if (task.Status != "failed" || task.CompletedAt is not { } completed || now - completed > FailedTaskWindow)
                continue;
            items.Add(new CheckInItem($"task-failed:{task.Id:N}",
                $"The background task “{task.Title}” failed. Ask me to try again if it still matters."));
        }
        await AddOptionalAsync(items, "calendar", () => CollectMeetingsAsync(now, timeZone, ownerId, cancellationToken));
        await AddOptionalAsync(items, "inbox", () => CollectInboxAsync(ownerId, cancellationToken));
        await AddOptionalAsync(items, "commitments",
            () => CollectOverdueCommitmentsAsync(ownerId, now, timeZone, cancellationToken));
        return items;
    }

    /// <summary>These sources depend on optional integrations, so one that fails only drops its own check-ins.</summary>
    private async Task AddOptionalAsync(List<CheckInItem> items, string source,
        Func<Task<IReadOnlyList<CheckInItem>>> collect)
    {
        try
        {
            items.AddRange(await collect());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Heartbeat skipped the {Source} check-ins.", source);
        }
    }

    private async Task<IReadOnlyList<CheckInItem>> CollectMeetingsAsync(DateTimeOffset now, TimeZoneInfo timeZone,
        Guid ownerId, CancellationToken cancellationToken)
    {
        var events = await calendar.ListUpcomingAsync(ownerId, now + MeetingPrepLead, now + MeetingPrepHorizon,
            cancellationToken);
        return events
            .Where(item => item.StartAt >= now + MeetingPrepLead && item.StartAt <= now + MeetingPrepHorizon &&
                           (item.EndAt is not { } end || end - item.StartAt < TimeSpan.FromHours(12)))
            .OrderBy(item => item.StartAt)
            .Take(3)
            .Select(item => HeartbeatPlanner.MeetingPrep(item.Title, item.StartAt, timeZone))
            .ToArray();
    }

    private async Task<IReadOnlyList<CheckInItem>> CollectInboxAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var waiting = await inbox.ListAsync(ownerId, new HashSet<string> { InboxStates.NeedsReply }, cancellationToken);
        // Ordinary threads are in the morning briefing; only urgent ones interrupt, and only once.
        return waiting.Threads
            .Where(thread => thread.State == InboxStates.NeedsReply && thread.Priority >= UrgentPriority)
            .Take(3)
            .Select(thread => new CheckInItem($"inbox:{thread.Id:N}",
                thread.Counterparty is { Length: > 0 } who
                    ? $"{who} is waiting for a reply: {thread.Title}."
                    : $"A chat is waiting for a reply: {thread.Title}."))
            .ToArray();
    }

    private async Task<IReadOnlyList<CheckInItem>> CollectOverdueCommitmentsAsync(Guid ownerId, DateTimeOffset now,
        TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var open = await commitments.ListAsync(ownerId, null, CommitmentStatuses.Open, includeSuggested: false,
            cancellationToken);
        // The due-day reminder covers today; this is the one nudge for what slipped past.
        return open
            .Where(item => !item.Suggested && item.DueOn is { } due && due < today)
            .OrderBy(item => item.DueOn)
            .Take(3)
            .Select(item => new CheckInItem($"commitment:{item.Id:N}",
                CommitmentsBriefingSection.Describe(item, today) + "."))
            .ToArray();
    }

    private async Task<TimeZoneInfo> ResolveTimeZoneAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zone = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        if (string.IsNullOrWhiteSpace(zone)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(zone); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    internal static string Summarize(ReflectionOutcome reflected, int checkIns, bool quiet, int tasksStarted = 0)
    {
        var learned = reflected.LearnedAnything ? ReflectionService.Describe(reflected) : "Nothing new to learn.";
        var attention = quiet ? "Quiet hours — no check-ins sent." :
            checkIns == 0 ? "Nothing needs your attention." : $"Sent {checkIns} check-in{(checkIns == 1 ? "" : "s")}.";
        var started = tasksStarted > 0
            ? $" Started {tasksStarted} background task{(tasksStarted == 1 ? "" : "s")}."
            : string.Empty;
        return $"{learned} {attention}{started}";
    }
}
