using Jarvis.Application.Workflows;
using Jarvis.Application.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

namespace Jarvis.Workflows;

public sealed class TemporalReminderScheduler(IConfiguration configuration) : IFileProcessingScheduler,
    IConditionWatchScheduler, IDailyBriefingScheduler
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
                IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate
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
                IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate
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
                IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate
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
                IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate
            });
    }

    public async Task ResolveTaskApprovalAsync(string workflowId, string summary, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await client.GetWorkflowHandle(workflowId).SignalAsync(
            (JarvisTaskWorkflow workflow) => workflow.ResolveApprovalAsync(summary));
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
    ILogger<ReminderService> logger) : IReminderService
{
    public async Task<ReminderRecord> CreateAsync(Guid ownerId, string title, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        title = title.Trim();
        if (title.Length is < 1 or > 300) throw new ArgumentException("Reminder title must contain 1 to 300 characters.", nameof(title));
        if (dueAt <= DateTimeOffset.UtcNow) throw new ArgumentOutOfRangeException(nameof(dueAt), "Reminder time must be in the future.");
        if (dueAt > DateTimeOffset.UtcNow.AddYears(2)) throw new ArgumentOutOfRangeException(nameof(dueAt), "Reminder time must be within the next two years.");

        var reminder = await reminders.CreateAsync(ownerId, title, dueAt, cancellationToken);
        try
        {
            await scheduler.ScheduleAsync(new ReminderWorkflowInput(reminder.Id, ownerId, reminder.Title, reminder.DueAt),
                reminder.WorkflowId, cancellationToken);
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
        return reminder;
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

public sealed class JarvisTaskService(
    IJarvisTaskRepository tasks,
    TemporalReminderScheduler scheduler,
    ITaskRunAbort taskRunAbort,
    ILogger<JarvisTaskService> logger) : IJarvisTaskService
{
    public async Task<JarvisTaskRecord> CreateAsync(Guid ownerId, string title, string prompt, CancellationToken cancellationToken)
    {
        title = title.Trim();
        prompt = prompt.Trim();
        if (title.Length is < 1 or > 200) throw new ArgumentException("Task title must contain 1 to 200 characters.", nameof(title));
        if (prompt.Length is < 1 or > 32_000) throw new ArgumentException("Task instructions must contain 1 to 32,000 characters.", nameof(prompt));

        var task = await tasks.CreateWithConversationAsync(ownerId, title, prompt, cancellationToken);
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
        var jsonPath = request.JsonPath?.Trim();
        var comparison = request.Comparison?.Trim().ToLowerInvariant();
        var url = NormalizeUrl(request.Url);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            throw new ArgumentException("Watch title must contain 1 to 200 characters.", nameof(request));
        if (string.IsNullOrWhiteSpace(jsonPath) || jsonPath.Length > 512 ||
            jsonPath.Split('.').Any(segment => segment.Length is < 1 or > 64 ||
                segment.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))))
            throw new ArgumentException("JSON path must contain simple object-property names separated by dots.", nameof(request));
        if (comparison is not ("below" or "above"))
            throw new ArgumentException("Comparison must be 'below' or 'above'.", nameof(request));
        if (!double.IsFinite(request.Threshold))
            throw new ArgumentOutOfRangeException(nameof(request), "Threshold must be a finite number.");
        if (request.IntervalMinutes is < 5 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(request), "Check interval must be between 5 minutes and 24 hours.");

        var watch = await watches.CreateAsync(ownerId, request with
        {
            Title = title,
            Url = url,
            JsonPath = jsonPath,
            Comparison = comparison
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
