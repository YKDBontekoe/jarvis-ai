using System.Text.Json;
using Jarvis.Application.Approvals;
using Jarvis.Application.Audit;
using Jarvis.Application.Learning;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Microsoft.Extensions.Logging;

namespace Jarvis.Agents.Learning;

/// <summary>
/// One heartbeat: reflect on new activity (continuous learning), then surface anything that needs attention.
/// Check-ins are deterministic and de-duplicated, stay silent during quiet hours, and never repeat an alert.
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
    ILogger<HeartbeatService> logger,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan ReminderHorizon = TimeSpan.FromHours(2);
    private static readonly TimeSpan StaleApproval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailedTaskWindow = TimeSpan.FromHours(24);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<HeartbeatOutcome> RunAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync<LearningSettings>(ownerId, SettingsSections.Learning,
            cancellationToken) ?? LearningSettings.Default;
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

        var timeZone = await ResolveTimeZoneAsync(ownerId, cancellationToken);
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var quiet = settings.IsQuietHour(localNow.Hour);
        var fresh = new List<CheckInItem>();
        if (settings.ProactiveCheckIns && !quiet)
        {
            var known = state.Keys.ToHashSet(StringComparer.Ordinal);
            fresh = (await CollectAsync(ownerId, now, timeZone, cancellationToken))
                .Where(item => !known.Contains(item.Key))
                .ToList();
            if (fresh.Count > 0)
                await notifications.CreateAsync(ownerId, "heartbeat.checkin",
                    fresh.Count == 1 ? "Heads up" : $"Heads up — {fresh.Count} things need you",
                    string.Join("\n", fresh.Select(item => "• " + item.Text)), null, cancellationToken);
        }

        var summary = Summarize(reflected, fresh.Count, quiet);
        var keys = state.Keys.Concat(fresh.Select(item => item.Key)).TakeLast(HeartbeatState.MaxRememberedKeys).ToArray();
        await settingsStore.SaveAsync(ownerId, LearningSections.HeartbeatState, new HeartbeatState(now,
            latestMessage ?? state.LastReflectedMessageAt, keys, summary), cancellationToken);
        await audit.AppendAsync(ownerId, "learning", "heartbeat.ran", "low", true, null,
            JsonSerializer.Serialize(new { checkIns = fresh.Count, quiet, learned = reflected.LearnedAnything }),
            cancellationToken);
        return new HeartbeatOutcome(reflected, fresh, quiet, summary);
    }

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
        return items;
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

    internal static string Summarize(ReflectionOutcome reflected, int checkIns, bool quiet)
    {
        var learned = reflected.LearnedAnything ? ReflectionService.Describe(reflected) : "Nothing new to learn.";
        var attention = quiet ? "Quiet hours — no check-ins sent." :
            checkIns == 0 ? "Nothing needs your attention." : $"Sent {checkIns} check-in{(checkIns == 1 ? "" : "s")}.";
        return $"{learned} {attention}";
    }
}
