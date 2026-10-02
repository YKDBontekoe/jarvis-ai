using Jarvis.Application.Automations;
using Jarvis.Application.Workflows;
using Jarvis.Application.Files;
using Jarvis.Application.Profiles;
using Jarvis.Domain.Automations;
using Jarvis.Domain.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

namespace Jarvis.Workflows;

public sealed class TemporalReminderScheduler(IConfiguration configuration) : IFileProcessingScheduler,
    IConditionWatchScheduler, IDailyBriefingScheduler, Jarvis.Application.Learning.IHeartbeatScheduler,
    Jarvis.Application.Learning.IDreamingScheduler, IAutomationScheduler,
    Jarvis.Application.Reviews.IWeeklyReviewScheduler, Jarvis.Application.Habits.IHabitCheckInScheduler,
    Jarvis.Application.People.IPeopleCheckInScheduler
{
    public const string TaskQueue = "jarvis-workflows";
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private TemporalClient? _client;

    public async Task ScheduleAsync(ReminderWorkflowInput reminder, string workflowId, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        await client.StartWorkflowAsync(
            (ReminderWorkflow workflow) => workflow.RunAsync(reminder),
            new WorkflowOptions(id: workflowId, taskQueue: TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
            });
    }

    public async Task CancelAsync(string workflowId, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await client.GetWorkflowHandle(workflowId).CancelAsync(new WorkflowCancelOptions());
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = exception;
                if (attempt == 3) break;
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }
        throw last!;
    }

    public async Task ScheduleAsync(Guid fileId, Guid ownerId, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        await client.StartWorkflowAsync(
            (FileProcessingWorkflow workflow) => workflow.RunAsync(new FileProcessingInput(fileId, ownerId)),
            new WorkflowOptions(id: $"jarvis:file:{fileId:N}", taskQueue: TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
            });
    }

    public Task CancelAsync(Guid fileId, CancellationToken cancellationToken) =>
        CancelAsync($"jarvis:file:{fileId:N}", cancellationToken);

    public async Task ScheduleTaskAsync(JarvisTaskRecord task, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        await client.StartWorkflowAsync(
            (JarvisTaskWorkflow workflow) => workflow.RunAsync(new JarvisTaskWorkflowInput(task.Id)),
            new WorkflowOptions(id: task.WorkflowId, taskQueue: TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
            });
    }

    public async Task CancelTaskAsync(string workflowId, CancellationToken cancellationToken) =>
        await CancelAsync(workflowId, cancellationToken);

    public async Task ScheduleAsync(ConditionWatchRecord watch, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        await client.StartWorkflowAsync(
            (ConditionWatchWorkflow workflow) => workflow.RunAsync(new ConditionWatchWorkflowInput(watch.Id)),
            new WorkflowOptions(id: watch.WorkflowId, taskQueue: TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
            });
    }

    public async Task ScheduleAsync(DailyBriefingWorkflowInput briefing, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        await client.StartWorkflowAsync(
            (DailyBriefingWorkflow workflow) => workflow.RunAsync(briefing),
            new WorkflowOptions(id: briefing.WorkflowId, taskQueue: TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
            });
    }

    public async Task ScheduleHeartbeatAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        await client.StartWorkflowAsync(
            (AssistantHeartbeatWorkflow workflow) => workflow.RunAsync(
                new Jarvis.Application.Learning.HeartbeatWorkflowInput(ownerId)),
            new WorkflowOptions(id: Jarvis.Application.Learning.HeartbeatWorkflowIds.For(ownerId), taskQueue: TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
            });
    }

    public Task CancelHeartbeatAsync(Guid ownerId, CancellationToken cancellationToken) =>
        CancelAsync(Jarvis.Application.Learning.HeartbeatWorkflowIds.For(ownerId), cancellationToken);

    public async Task ScheduleDreamingAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        await client.StartWorkflowAsync(
            (AssistantDreamingWorkflow workflow) => workflow.RunAsync(
                new Jarvis.Application.Learning.DreamingWorkflowInput(ownerId)),
            new WorkflowOptions(id: Jarvis.Application.Learning.DreamingWorkflowIds.For(ownerId), taskQueue: TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
            });
    }

    public Task CancelDreamingAsync(Guid ownerId, CancellationToken cancellationToken) =>
        CancelAsync(Jarvis.Application.Learning.DreamingWorkflowIds.For(ownerId), cancellationToken);

    public async Task ScheduleWeeklyReviewAsync(Guid ownerId, bool settingsChanged,
        CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        var options = new WorkflowOptions(id: Jarvis.Application.Reviews.WeeklyReviewWorkflowIds.For(ownerId),
            taskQueue: TaskQueue)
        {
            IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
        };
        if (settingsChanged)
            options.SignalWithStart((WeeklyReviewWorkflow workflow) => workflow.SettingsChangedAsync());
        else
            options.IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting;
        await client.StartWorkflowAsync(
            (WeeklyReviewWorkflow workflow) => workflow.RunAsync(
                new Jarvis.Application.Reviews.WeeklyReviewWorkflowInput(ownerId)), options);
    }

    public async Task ScheduleHabitCheckInAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        await client.StartWorkflowAsync(
            (HabitCheckInWorkflow workflow) => workflow.RunAsync(
                new Jarvis.Application.Habits.HabitCheckInWorkflowInput(ownerId)),
            new WorkflowOptions(id: Jarvis.Application.Habits.HabitCheckInWorkflowIds.For(ownerId), taskQueue: TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
            });
    }


    public async Task SchedulePeopleCheckInAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        await client.StartWorkflowAsync(
            (PeopleCheckInWorkflow workflow) => workflow.RunAsync(
                new Jarvis.Application.People.PeopleCheckInInput(ownerId)),
            new WorkflowOptions(id: Jarvis.Application.People.PeopleCheckInWorkflowIds.For(ownerId), taskQueue: TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
            });
    }


    public Task CancelHabitCheckInAsync(Guid ownerId, CancellationToken cancellationToken) =>
        CancelAsync(Jarvis.Application.Habits.HabitCheckInWorkflowIds.For(ownerId), cancellationToken);

    public async Task ResolveTaskApprovalAsync(string workflowId, string summary, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await client.GetWorkflowHandle(workflowId).SignalAsync(
            (JarvisTaskWorkflow workflow) => workflow.ResolveApprovalAsync(summary));
    }

    public async Task ScheduleRuleAsync(AutomationRuleRecord rule, CancellationToken cancellationToken)
    {
        var definition = AutomationDefinitionJson.Deserialize(rule.DefinitionJson);
        var client = await GetClientAsync(cancellationToken);
        if (definition.Trigger is ScheduleTriggerDefinition)
        {
            await client.StartWorkflowAsync(
                (AutomationScheduleWorkflow workflow) => workflow.RunAsync(
                    new AutomationScheduleWorkflowInput(rule.Id, rule.OwnerId, rule.ScheduleWorkflowId)),
                new WorkflowOptions(id: rule.ScheduleWorkflowId, taskQueue: TaskQueue)
                {
                    IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                    IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
                });
            return;
        }

        if (AutomationTriggerKinds.IsPolling(definition.Trigger.Kind))
        {
            var interval = definition.Trigger switch
            {
                PublicJsonThresholdTriggerDefinition json => json.IntervalMinutes,
                DeviceBatteryTriggerDefinition battery => battery.IntervalMinutes,
                DeviceLocationTriggerDefinition location => location.IntervalMinutes,
                CalendarWindowTriggerDefinition calendar => calendar.IntervalMinutes,
                _ => 15
            };
            await client.StartWorkflowAsync(
                (AutomationPollWorkflow workflow) => workflow.RunAsync(
                    new AutomationPollWorkflowInput(rule.Id, rule.OwnerId, rule.ScheduleWorkflowId, interval)),
                new WorkflowOptions(id: rule.ScheduleWorkflowId, taskQueue: TaskQueue)
                {
                    IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                    IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
                });
        }
    }

    public Task CancelRuleScheduleAsync(string scheduleWorkflowId, CancellationToken cancellationToken) =>
        CancelAsync(scheduleWorkflowId, cancellationToken);

    public async Task StartRunAsync(AutomationRunRecord run, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        var input = new AutomationRunWorkflowInput(run.RuleId, run.OwnerId, run.Id, run.IdempotencyKey,
            run.TriggerKind, run.TriggerReason, run.TestRun);
        await client.StartWorkflowAsync(
            (AutomationRunWorkflow workflow) => workflow.RunAsync(input),
            new WorkflowOptions(id: run.WorkflowId, taskQueue: TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate
            });
    }

    public async Task SignalApprovalResolvedAsync(string runWorkflowId, bool approved,
        CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await client.GetWorkflowHandle(runWorkflowId).SignalAsync(
            (AutomationRunWorkflow workflow) => workflow.ResolveApprovalAsync(approved));
    }

    private async Task<TemporalClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null) return _client;
        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (_client is null)
            {
                var address = configuration["Temporal:Address"] ?? "localhost:7233";
                _client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(address));
            }
            return _client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

}

public sealed class ReminderService(IReminderRepository reminders, TemporalReminderScheduler scheduler,
    IDailyBriefingRepository briefings, ILogger<ReminderService> logger) : IReminderService
{
    public static readonly TimeSpan MinimumSnooze = TimeSpan.FromSeconds(30);

    public async Task<ReminderRecord> CreateAsync(Guid ownerId, CreateReminderRequest request, CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > 300) throw new ArgumentException("Reminder title must contain 1 to 300 characters.", nameof(request));

        // Without an explicit zone, use the owner's zone from their briefing settings (the same one the assistant's
        // clock uses) so "every day at 08:00" means their 08:00, not UTC.
        var zoneId = string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId ?? "UTC"
            : request.TimeZoneId.Trim();
        if (!LocalClock.TryFind(zoneId, out var zone))
            throw new ArgumentException("Time zone identifier is not recognized by this server.", nameof(request));

        if (request.Place is { } place)
        {
            var placed = await reminders.CreateAsync(ownerId, new CreateReminderRequest(title, DateTimeOffset.UtcNow,
                TimeZoneId: zone.Id, Place: place with { Name = place.Name?.Trim() ?? string.Empty }),
                cancellationToken);
            // Place reminders have no Temporal workflow: a position the phone reports fires them.
            return placed;
        }

        var local = TimeZoneInfo.ConvertTime(request.DueAt, zone);
        var localTime = request.LocalTime ?? TimeOnly.FromTimeSpan(local.TimeOfDay);
        ReminderRule rule;
        try
        {
            rule = ReminderSchedule.Normalize(request.Recurrence, request.Weekdays, zone.Id, localTime, request.Until);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(exception.Message, nameof(request), exception);
        }

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset dueAt;
        try
        {
            dueAt = ReminderSchedule.ResolveFirst(request.DueAt, rule, now);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw;
        }

        if (dueAt > now.AddYears(2))
            throw new ArgumentOutOfRangeException(nameof(request), "Reminder time must be within the next two years.");

        var reminder = await reminders.CreateAsync(ownerId, new CreateReminderRequest(title, dueAt, rule.Recurrence,
            rule.Weekdays, rule.TimeZoneId, rule.Until, rule.LocalTime), cancellationToken);
        await DispatchAsync(reminder, cancellationToken);
        return reminder;
    }

    public async Task<ReminderRecord?> SnoozeAsync(Guid id, Guid ownerId, DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (dueAt < now + MinimumSnooze || dueAt > now.AddYears(2))
            throw new ArgumentOutOfRangeException(nameof(dueAt), "Snooze to a moment between a minute and two years from now.");

        var current = await reminders.GetAsync(id, ownerId, cancellationToken);
        if (current is null || current.Status is not ("pending" or "completed")) return null;
        if (current.Recurrence != Reminder.RecurrenceNone || current.Place is { Repeats: true })
            return await CreateAsync(ownerId, new CreateReminderRequest(current.Title, dueAt,
                TimeZoneId: current.TimeZoneId), cancellationToken);

        var snoozed = await reminders.SnoozeAsync(id, ownerId, dueAt, cancellationToken);
        if (snoozed is not { } result) return null;
        if (current.Place is null && result.PreviousWorkflowId != result.Reminder.WorkflowId)
            await StopQuietlyAsync(result.PreviousWorkflowId, id, cancellationToken);
        await DispatchAsync(result.Reminder, cancellationToken);
        return result.Reminder;
    }

    public async Task<ReminderRecord?> MarkDoneAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var done = await reminders.MarkDoneAsync(id, ownerId, cancellationToken);
        if (done is { Place: null }) await StopQuietlyAsync(done.WorkflowId, id, cancellationToken);
        return done;
    }

    private async Task DispatchAsync(ReminderRecord reminder, CancellationToken cancellationToken)
    {
        try
        {
            await scheduler.ScheduleAsync(new ReminderWorkflowInput(reminder.Id, reminder.OwnerId, reminder.Title,
                reminder.DueAt), reminder.WorkflowId, cancellationToken);
            await reminders.MarkReminderScheduleDispatchedAsync(reminder.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Reminder {ReminderId} remains pending for Temporal scheduling recovery.", reminder.Id);
        }
    }

    private async Task StopQuietlyAsync(string workflowId, Guid reminderId, CancellationToken cancellationToken)
    {
        try
        {
            await scheduler.CancelAsync(workflowId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Usually the workflow already finished. If not, it finds the reminder moved on and does nothing.
            logger.LogDebug(exception, "Previous workflow for reminder {ReminderId} was not stopped.", reminderId);
        }
    }

    public Task<IReadOnlyList<ReminderRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        reminders.ListRemindersAsync(ownerId, cancellationToken);

    public Task<ReminderRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        reminders.GetAsync(id, ownerId, cancellationToken);

    public async Task<ReminderRecord?> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var reminder = await reminders.GetAsync(id, ownerId, cancellationToken);
        if (reminder is null || reminder.Status != "pending") return null;
        var cancelled = await reminders.CancelAsync(id, ownerId, cancellationToken);
        if (cancelled is null) return null;
        if (reminder.Place is not null) return cancelled;
        try
        {
            await scheduler.CancelAsync(reminder.WorkflowId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Reminder {ReminderId} was cancelled in storage; Temporal will stop it if the workflow is still running.", id);
        }
        return cancelled;
    }
}

/// <summary>Fires place reminders from the positions the phone reports, and starts automations that follow them.</summary>
public sealed class PlaceReminderService(IReminderRepository reminders, IAutomationTriggerPublisher automations,
    ILogger<PlaceReminderService> logger) : IPlaceReminderService
{
    public async Task<int> ObservePositionAsync(Guid ownerId, double latitude, double longitude,
        double? accuracyMeters, CancellationToken cancellationToken)
    {
        var fired = await reminders.ObservePositionAsync(ownerId, latitude, longitude, accuracyMeters,
            DateTimeOffset.UtcNow, cancellationToken);
        foreach (var reminder in fired)
        {
            try
            {
                await automations.PublishReminderDueAsync(ownerId, reminder.ReminderId, reminder.Title,
                    reminder.FiredAt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Automations after place reminder {ReminderId} did not start.",
                    reminder.ReminderId);
            }
        }
        return fired.Count;
    }
}

public sealed class JarvisTaskService(
    IJarvisTaskRepository tasks,
    TemporalReminderScheduler scheduler,
    ITaskRunAbort taskRunAbort,
    IAssistantProfileService profiles,
    ILogger<JarvisTaskService> logger) : IJarvisTaskService
{
    public async Task<JarvisTaskRecord> CreateAsync(Guid ownerId, string title, string prompt, CancellationToken cancellationToken,
        Guid? profileId = null, Guid? projectId = null)
    {
        title = title.Trim();
        prompt = prompt.Trim();
        if (title.Length is < 1 or > 200) throw new ArgumentException("Task title must contain 1 to 200 characters.", nameof(title));
        if (prompt.Length is < 1 or > 32_000) throw new ArgumentException("Task instructions must contain 1 to 32,000 characters.", nameof(prompt));

        var binding = await profiles.CaptureBindingAsync(ownerId, profileId, cancellationToken);
        var task = await tasks.CreateWithConversationAsync(ownerId, title, prompt, cancellationToken, binding,
            projectId);
        try
        {
            await scheduler.ScheduleTaskAsync(task, cancellationToken);
            await tasks.MarkTaskScheduleDispatchedAsync(task.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Task {TaskId} remains queued for Temporal scheduling recovery.", task.Id);
        }
        return task;
    }

    public Task<IReadOnlyList<JarvisTaskRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        tasks.ListAsync(ownerId, cancellationToken);

    public async Task<bool> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetTaskAsync(id, ownerId, cancellationToken);
        if (task is null || task.Status is "completed" or "failed" or "cancelled") return false;
        if (!await tasks.CancelTaskAsync(id, ownerId, cancellationToken)) return false;
        taskRunAbort.Abort(id);
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
            logger.LogWarning(exception,
                "Task {TaskId} was cancelled in storage; Temporal will stop it if the workflow is still running.", id);
        }
        return true;
    }

    public async Task CompleteAfterApprovalAsync(Guid? taskId, Guid ownerId, string summary, CancellationToken cancellationToken)
    {
        if (taskId is null) return;
        var task = await tasks.GetTaskAsync(taskId.Value, ownerId, cancellationToken);
        if (task is null || task.Status != "needs_approval") return;
        await tasks.CompleteAndNotifyAsync(task.Id, summary, cancellationToken);
        try
        {
            await scheduler.ResolveTaskApprovalAsync(task.WorkflowId, summary, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Task {TaskId} was completed after approval; Temporal will stop waiting if the workflow is still running.",
                task.Id);
        }
    }

    public async Task FailAfterRejectedApprovalAsync(Guid? taskId, Guid ownerId, string summary, CancellationToken cancellationToken)
    {
        if (taskId is null) return;
        var task = await tasks.GetTaskAsync(taskId.Value, ownerId, cancellationToken);
        if (task is null || task.Status != "needs_approval") return;
        await tasks.FailAsync(task.Id, summary, cancellationToken);
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
            logger.LogWarning(exception,
                "Task {TaskId} was marked failed after a rejected approval; Temporal will stop it if the workflow is still running.",
                task.Id);
        }
    }
}

public sealed class ConditionWatchService(IConditionWatchRepository watches, IConditionWatchScheduler scheduler,
    ILogger<ConditionWatchService> logger) : IConditionWatchService
{
    public async Task<ConditionWatchRecord> CreateAsync(Guid ownerId, CreateConditionWatchRequest request,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        var kind = WatchKinds.Normalize(request.Kind);
        var comparison = request.Comparison?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            throw new ArgumentException("Watch title must contain 1 to 200 characters.", nameof(request));
        if (comparison is not ("below" or "above"))
            throw new ArgumentException("Comparison must be 'below' or 'above'.", nameof(request));
        if (!double.IsFinite(request.Threshold) && kind is not WatchKinds.DeviceLocation and not WatchKinds.Calendar)
            throw new ArgumentOutOfRangeException(nameof(request), "Threshold must be a finite number.");
        if (request.IntervalMinutes is < 5 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(request), "Check interval must be between 5 minutes and 24 hours.");

        var url = "";
        var jsonPath = "";
        string? credentialProvider = null;
        double? latitude = null, longitude = null, radius = null;
        int? minutesBefore = null;
        var threshold = request.Threshold;

        if (WatchKinds.RequiresUrl(kind))
        {
            url = NormalizeUrl(request.Url);
            jsonPath = request.JsonPath?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(jsonPath) || jsonPath.Length > 512 ||
                jsonPath.Split('.').Any(segment => segment.Length is < 1 or > 64 ||
                    segment.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))))
                throw new ArgumentException("JSON path must contain simple object-property names separated by dots.", nameof(request));
            if (kind == WatchKinds.AuthenticatedJson)
            {
                credentialProvider = request.CredentialProvider?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(credentialProvider) || credentialProvider.Length > 80)
                    throw new ArgumentException("Authenticated watches need a stored integration provider slug.", nameof(request));
            }
        }
        else if (kind == WatchKinds.DeviceBattery)
        {
            if (!double.IsFinite(threshold) || threshold is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(request), "Battery threshold must be between 0 and 100 percent.");
        }
        else if (kind == WatchKinds.DeviceLocation)
        {
            latitude = request.Latitude;
            longitude = request.Longitude;
            radius = request.RadiusMeters ?? request.Threshold;
            if (latitude is null or < -90 or > 90 || longitude is null or < -180 or > 180)
                throw new ArgumentException("Location watches need a latitude and longitude.", nameof(request));
            if (radius is null or < 25 or > 50_000)
                throw new ArgumentException("Location watches need a radius between 25 and 50,000 meters.", nameof(request));
            threshold = radius.Value;
        }
        else if (kind == WatchKinds.Calendar)
        {
            minutesBefore = request.MinutesBefore is > 0 and <= 24 * 60
                ? request.MinutesBefore
                : (int)Math.Clamp(request.Threshold, 5, 24 * 60);
            threshold = minutesBefore.Value;
            comparison = "below";
        }

        var watch = await watches.CreateAsync(ownerId, request with
        {
            Title = title,
            Kind = kind,
            Url = url,
            JsonPath = jsonPath,
            Comparison = comparison,
            Threshold = threshold,
            CredentialProvider = credentialProvider,
            Latitude = latitude,
            Longitude = longitude,
            RadiusMeters = radius,
            MinutesBefore = minutesBefore
        }, cancellationToken);
        try
        {
            await scheduler.ScheduleAsync(watch, cancellationToken);
            await watches.MarkScheduleDispatchedAsync(watch.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Condition watch {WatchId} remains active for Temporal scheduling recovery.", watch.Id);
        }
        return watch;
    }

    public Task<IReadOnlyList<ConditionWatchRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        watches.ListAsync(ownerId, cancellationToken);

    public Task<ConditionWatchRecord?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        watches.GetAsync(id, ownerId, cancellationToken);

    public async Task<bool> CancelAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var watch = await watches.GetAsync(id, ownerId, cancellationToken);
        if (watch is null || watch.Status != "active") return false;
        if (!await watches.CancelAsync(id, ownerId, cancellationToken)) return false;
        try
        {
            await scheduler.CancelAsync($"jarvis-watch-{id:N}", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Condition watch {WatchId} was cancelled in storage; Temporal will stop it at its next check.", id);
        }
        return true;
    }

    private static string NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || uri.Port != 443 ||
            System.Net.IPAddress.TryParse(uri.Host, out _) || !uri.Host.Contains('.', StringComparison.Ordinal))
            throw new ArgumentException("Watch URL must be a credential-free HTTPS URL on a public DNS hostname.", nameof(value));

        var host = uri.IdnHost.TrimEnd('.');
        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Local and private hostnames cannot be watched.", nameof(value));
        if (uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Uri.UnescapeDataString(part.Split('=', 2)[0]))
            .Any(name => IsSensitiveQueryName(name)))
            throw new ArgumentException("Watch URLs cannot contain API-key, token, password, or credential query parameters.", nameof(value));

        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
    }

    private static bool IsSensitiveQueryName(string name)
    {
        var normalized = new string(name.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized.Contains("apikey", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("secret", StringComparison.Ordinal) ||
               normalized.Contains("password", StringComparison.Ordinal) ||
               normalized.Contains("credential", StringComparison.Ordinal) ||
               normalized.Contains("authorization", StringComparison.Ordinal) ||
               normalized is "key" or "auth" or "sig" or "signature";
    }
}

public sealed class DailyBriefingService(IDailyBriefingRepository briefings, IDailyBriefingScheduler scheduler,
    ILogger<DailyBriefingService> logger) : IDailyBriefingService
{
    public Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
        briefings.GetAsync(ownerId, cancellationToken);

    public async Task<DailyBriefingPreferenceRecord> SaveAsync(Guid ownerId, SaveDailyBriefingRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TimeZoneId) || request.TimeZoneId.Length > 100)
            throw new ArgumentException("A valid time zone identifier is required.", nameof(request));
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId); }
        catch (TimeZoneNotFoundException exception)
        { throw new ArgumentException("Time zone identifier is not recognized by this server.", nameof(request), exception); }
        catch (InvalidTimeZoneException exception)
        { throw new ArgumentException("Time zone identifier is invalid.", nameof(request), exception); }

        var (preference, previousWorkflowId) = await briefings.SaveAsync(ownerId, request, cancellationToken);
        if (!string.IsNullOrWhiteSpace(previousWorkflowId))
        {
            try { await scheduler.CancelAsync(previousWorkflowId, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            { logger.LogWarning(exception, "Could not stop replaced morning briefing workflow {WorkflowId}.", previousWorkflowId); }
        }

        try
        {
            if (preference.Enabled)
                await scheduler.ScheduleAsync(new DailyBriefingWorkflowInput(ownerId, preference.WorkflowId,
                    preference.LocalTime, preference.TimeZoneId), cancellationToken);
            await briefings.MarkScheduleDispatchedAsync(ownerId, preference.WorkflowId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        { logger.LogWarning(exception, "Morning briefing {OwnerId} remains pending for Temporal scheduling recovery.", ownerId); }
        return preference;
    }
}
