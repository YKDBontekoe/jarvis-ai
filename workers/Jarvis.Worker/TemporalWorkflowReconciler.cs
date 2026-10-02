using Jarvis.Application.Automations;
using Jarvis.Application.Files;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Workflows;
using Jarvis.Workflows;

namespace Jarvis.Worker;

/// <summary>
/// Repairs the PostgreSQL-to-Temporal scheduling gap after a process interruption.
/// Workflow IDs are stable and starts use Temporal's UseExisting conflict policy, so replaying
/// these dispatches is safe while Temporal remains the durable execution source of truth.
/// Dispatched work whose Temporal run died is requeued and restarted with AllowDuplicate.
/// </summary>
internal sealed class TemporalWorkflowReconciler(
    IServiceScopeFactory scopeFactory,
    ILogger<TemporalWorkflowReconciler> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(10);
    private DateTimeOffset _nextHeartbeatReconcile = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await ReconcileOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var scheduler = services.GetRequiredService<TemporalReminderScheduler>();

            var fileRepository = services.GetRequiredService<IFileRepository>();
            var fileScheduler = services.GetRequiredService<IFileProcessingScheduler>();
            var storage = services.GetRequiredService<IObjectStorage>();
            var deletingFiles = await fileRepository.ListDeletingAsync(cancellationToken);
            foreach (var file in deletingFiles)
                await TryScheduleAsync("file deletion", file.Id,
                    async () =>
                    {
                        try
                        {
                            await fileScheduler.CancelAsync(file.Id, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            logger.LogDebug(exception, "File processing workflow for {FileId} was already stopped.",
                                file.Id);
                        }
                        await storage.DeleteAsync(file.ObjectKey, cancellationToken);
                        await fileRepository.DeleteAsync(file.Id, file.OwnerId, cancellationToken);
                    }, cancellationToken);

            var reminderRepository = services.GetRequiredService<IReminderRepository>();
            await reminderRepository.RequeueOverdueDispatchedAsync(DateTimeOffset.UtcNow, cancellationToken);
            var reminders = await reminderRepository
                .ListPendingForSchedulingAsync(cancellationToken);
            foreach (var reminder in reminders)
                await TryScheduleAsync("reminder", reminder.Id,
                    async () =>
                    {
                        await scheduler.ScheduleAsync(
                            new ReminderWorkflowInput(reminder.Id, reminder.OwnerId, reminder.Title, reminder.DueAt),
                            reminder.WorkflowId, cancellationToken);
                        await services.GetRequiredService<IReminderRepository>()
                            .MarkReminderScheduleDispatchedAsync(reminder.Id, cancellationToken);
                    }, cancellationToken);

            var taskRepository = services.GetRequiredService<IJarvisTaskRepository>();
            await taskRepository.RequeueStaleQueuedAsync(
                DateTimeOffset.UtcNow.AddMinutes(-JarvisTask.QueuedDispatchStaleMinutes), cancellationToken);
            var tasks = await taskRepository
                .ListQueuedForSchedulingAsync(cancellationToken);
            foreach (var task in tasks)
                await TryScheduleAsync("task", task.Id,
                    async () =>
                    {
                        await scheduler.ScheduleTaskAsync(task, cancellationToken);
                        await services.GetRequiredService<IJarvisTaskRepository>()
                            .MarkTaskScheduleDispatchedAsync(task.Id, cancellationToken);
                    }, cancellationToken);

            var terminalTasks = await services.GetRequiredService<IJarvisTaskRepository>()
                .ListRecentlyTerminalAsync(DateTimeOffset.UtcNow.AddHours(-24), cancellationToken);
            foreach (var task in terminalTasks)
                await TryScheduleAsync("task cancel", task.Id,
                    async () =>
                    {
                        try
                        {
                            await scheduler.CancelTaskAsync(task.WorkflowId, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            logger.LogDebug(exception, "Terminal task workflow {WorkflowId} was already stopped.",
                                task.WorkflowId);
                        }
                    }, cancellationToken);

            var watchRepository = services.GetRequiredService<IConditionWatchRepository>();
            await watchRepository.RequeueStaleActiveAsync(DateTimeOffset.UtcNow, cancellationToken);
            var watches = await watchRepository
                .ListPendingForSchedulingAsync(cancellationToken);
            foreach (var watch in watches)
                await TryScheduleAsync("condition watch", watch.Id,
                    async () =>
                    {
                        await services.GetRequiredService<IConditionWatchScheduler>()
                            .ScheduleAsync(watch, cancellationToken);
                        await services.GetRequiredService<IConditionWatchRepository>()
                            .MarkScheduleDispatchedAsync(watch.Id, cancellationToken);
                    }, cancellationToken);

            var automationRepository = services.GetRequiredService<IAutomationRuleRepository>();
            var expiredRuns = await services.GetRequiredService<IAutomationRunRepository>()
                .ExpireAbandonedAsync(DateTimeOffset.UtcNow, cancellationToken);
            if (expiredRuns > 0)
                logger.LogInformation("Closed {Count} automation runs that no workflow was finishing.", expiredRuns);
            await automationRepository.RequeueStaleSchedulesAsync(DateTimeOffset.UtcNow, cancellationToken);
            var automations = await automationRepository.ListPendingScheduleDispatchAsync(cancellationToken);
            foreach (var automation in automations)
                await TryScheduleAsync("automation", automation.Id,
                    async () =>
                    {
                        await services.GetRequiredService<IAutomationScheduler>()
                            .ScheduleRuleAsync(automation, cancellationToken);
                        await automationRepository.MarkScheduleDispatchedAsync(automation.Id, cancellationToken);
                    }, cancellationToken);

            var briefingRepository = services.GetRequiredService<IDailyBriefingRepository>();
            await briefingRepository.RequeueStaleEnabledAsync(DateTimeOffset.UtcNow, cancellationToken);
            var briefings = await briefingRepository.ListPendingForSchedulingAsync(cancellationToken);
            foreach (var briefing in briefings)
                await TryScheduleAsync("daily briefing", briefing.OwnerId,
                    async () =>
                    {
                        if (briefing.Enabled)
                            await services.GetRequiredService<IDailyBriefingScheduler>().ScheduleAsync(
                                new DailyBriefingWorkflowInput(briefing.OwnerId, briefing.WorkflowId,
                                    briefing.LocalTime, briefing.TimeZoneId), cancellationToken);
                        else if (!string.IsNullOrEmpty(briefing.WorkflowId))
                        {
                            try
                            {
                                await services.GetRequiredService<IDailyBriefingScheduler>()
                                    .CancelAsync(briefing.WorkflowId, cancellationToken);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                            catch (Exception exception)
                            {
                                logger.LogDebug(exception, "Old daily briefing workflow {WorkflowId} was already stopped.",
                                    briefing.WorkflowId);
                            }
                        }
                        await briefingRepository.MarkScheduleDispatchedAsync(briefing.OwnerId,
                            briefing.WorkflowId, cancellationToken);
                    }, cancellationToken);

            if (DateTimeOffset.UtcNow >= _nextHeartbeatReconcile)
            {
                _nextHeartbeatReconcile = DateTimeOffset.UtcNow + HeartbeatInterval;
                var settingsStore = services.GetRequiredService<Jarvis.Application.Settings.IOwnerSettingsStore>();
                var heartbeatScheduler = services.GetRequiredService<Jarvis.Application.Learning.IHeartbeatScheduler>();
                var dreamingScheduler = services.GetRequiredService<Jarvis.Application.Learning.IDreamingScheduler>();
                foreach (var ownerId in await settingsStore.ListOwnersAsync(
                             Jarvis.Application.Settings.SettingsSections.Learning, cancellationToken))
                {
                    var learning = await settingsStore.GetAsync<Jarvis.Application.Settings.LearningSettings>(ownerId,
                        Jarvis.Application.Settings.SettingsSections.Learning, cancellationToken)
                        ?? Jarvis.Application.Settings.LearningSettings.Default;
                    if (learning.HeartbeatEnabled)
                        await TryScheduleAsync("heartbeat", ownerId,
                            () => heartbeatScheduler.ScheduleHeartbeatAsync(ownerId, cancellationToken),
                            cancellationToken);
                    if (learning.DreamingEnabled)
                        await TryScheduleAsync("dreaming", ownerId,
                            () => dreamingScheduler.ScheduleDreamingAsync(ownerId, cancellationToken),
                            cancellationToken);
                }

                var weeklyReviewScheduler = services.GetRequiredService<Jarvis.Application.Reviews.IWeeklyReviewScheduler>();
                foreach (var ownerId in await settingsStore.ListOwnersAsync(
                             Jarvis.Application.Settings.SettingsSections.WeeklyReview, cancellationToken))
                {
                    var review = await settingsStore.GetAsync<Jarvis.Application.Reviews.WeeklyReviewSettings>(ownerId,
                        Jarvis.Application.Settings.SettingsSections.WeeklyReview, cancellationToken);
                    if (review?.Enabled == true)
                        await TryScheduleAsync("weekly review", ownerId,
                            () => weeklyReviewScheduler.ScheduleWeeklyReviewAsync(ownerId, settingsChanged: false,
                                cancellationToken),
                            cancellationToken);
                }

                var habitScheduler = services.GetRequiredService<Jarvis.Application.Habits.IHabitCheckInScheduler>();
                foreach (var ownerId in await services.GetRequiredService<Jarvis.Application.Habits.IHabitRepository>()
                             .ListOwnersWithActiveHabitsAsync(cancellationToken))
                {
                    var habitSettings = await settingsStore.GetAsync<Jarvis.Application.Habits.HabitSettings>(ownerId,
                        Jarvis.Application.Habits.HabitSettingsSections.Settings, cancellationToken)
                        ?? Jarvis.Application.Habits.HabitSettings.Default;
                    if (habitSettings.EveningCheckIn)
                        await TryScheduleAsync("habit check-in", ownerId,
                            () => habitScheduler.ScheduleHabitCheckInAsync(ownerId, cancellationToken),
                            cancellationToken);
                }
            }

            await fileRepository.RequeueStaleQueuedAsync(
                DateTimeOffset.UtcNow.AddMinutes(-FileIndexing.QueuedDispatchStaleMinutes), cancellationToken);
            await fileRepository.RequeueStaleProcessingAsync(DateTimeOffset.UtcNow.AddMinutes(-60),
                cancellationToken);
            var files = await fileRepository.ListQueuedForProcessingAsync(cancellationToken);
            foreach (var file in files)
                await TryScheduleAsync("file", file.Id,
                    async () =>
                    {
                        await fileScheduler.ScheduleAsync(file.Id, file.OwnerId, cancellationToken);
                        await services.GetRequiredService<IFileRepository>()
                            .MarkProcessingScheduleDispatchedAsync(file.Id, cancellationToken);
                    }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Temporal schedule reconciliation failed; queued work will be retried.");
        }
    }

    private async Task TryScheduleAsync(string kind, Guid id, Func<Task> schedule,
        CancellationToken cancellationToken)
    {
        try
        {
            await schedule();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not reconcile durable {WorkKind} {WorkId}; it will be retried.", kind, id);
        }
    }
}
