using Jarvis.Worker.Activities;
using Jarvis.Worker.Learning;
using Jarvis.Workflows;
using Jarvis.Domain.Automations;
using Temporalio.Client;
using Temporalio.Worker;

namespace Jarvis.Worker.Hosting;

/// <summary>
/// Central registry of Temporal workflows and activities hosted by this worker.
/// Activity and workflow type names are part of the durable execution contract; do not rename without a migration plan.
/// </summary>
internal static class TemporalWorkerRegistration
{
    public static readonly IReadOnlyList<string> WorkflowTypeNames =
    [
        nameof(ReminderWorkflow),
        nameof(FileProcessingWorkflow),
        nameof(JarvisTaskWorkflow),
        nameof(ConditionWatchWorkflow),
        nameof(DailyBriefingWorkflow),
        nameof(AssistantHeartbeatWorkflow),
        nameof(AssistantDreamingWorkflow),
        nameof(AutomationRunWorkflow),
        nameof(AutomationScheduleWorkflow),
        nameof(AutomationPollWorkflow),
        nameof(WeeklyReviewWorkflow),
        nameof(HabitCheckInWorkflow),
        nameof(PeopleCheckInWorkflow),
    ];

    public static readonly IReadOnlyList<string> ActivityTypeNames =
    [
        "DeliverReminder",
        "DeliverReminderOccurrence",
        "FailReminder",
        "ProcessStoredFile",
        "RunJarvisTask",
        "CompleteApprovedJarvisTask",
        "FailJarvisTask",
        "GetJarvisTaskStatus",
        "CheckConditionWatch",
        "FailConditionWatch",
        "ResolveDailyBriefingSchedule",
        "DeliverDailyBriefing",
        "RunAssistantHeartbeat",
        "RunAssistantDreaming",
        "ExecuteAutomationRun",
        "CompleteAutomationRunAfterApproval",
        "CloseAutomationRun",
        "ResolveAutomationSchedule",
        "FireAutomationSchedule",
        "CheckAutomationPollTrigger",
        "FireAutomationPoll",
        "ResolveWeeklyReviewSchedule",
        "DeliverWeeklyReview",
        "ResolveHabitCheckIn",
        "DeliverHabitCheckIn",
        "RunPeopleCheckIn",
    ];

    public static TemporalWorker CreateWorker(TemporalClient client, IServiceProvider services)
    {
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var reminders = services.GetRequiredService<ReminderActivities>();
        var files = services.GetRequiredService<FileProcessingActivities>();
        var tasks = services.GetRequiredService<JarvisTaskActivities>();
        var watches = services.GetRequiredService<ConditionWatchActivities>();
        var briefings = services.GetRequiredService<DailyBriefingActivities>();
        var heartbeat = services.GetRequiredService<AssistantHeartbeatActivities>();
        var dreaming = services.GetRequiredService<AssistantDreamingActivities>();
        var people = services.GetRequiredService<PeopleCheckInActivities>();
        var automationRuns = services.GetRequiredService<AutomationRunActivities>();
        var automationSchedules = services.GetRequiredService<AutomationScheduleActivities>();
        var automationPolls = services.GetRequiredService<AutomationPollActivities>();
        var weeklyReviews = services.GetRequiredService<WeeklyReviewActivities>();
        var habitCheckIns = services.GetRequiredService<HabitCheckInActivities>();

        return new TemporalWorker(client, new TemporalWorkerOptions(TemporalReminderScheduler.TaskQueue)
            .AddWorkflow<ReminderWorkflow>()
            .AddWorkflow<FileProcessingWorkflow>()
            .AddWorkflow<JarvisTaskWorkflow>()
            .AddWorkflow<ConditionWatchWorkflow>()
            .AddWorkflow<DailyBriefingWorkflow>()
            .AddWorkflow<AssistantHeartbeatWorkflow>()
            .AddWorkflow<AssistantDreamingWorkflow>()
            .AddWorkflow<PeopleCheckInWorkflow>()
            .AddWorkflow<AutomationRunWorkflow>()
            .AddWorkflow<AutomationScheduleWorkflow>()
            .AddWorkflow<AutomationPollWorkflow>()
            .AddWorkflow<WeeklyReviewWorkflow>()
            .AddWorkflow<HabitCheckInWorkflow>()
            .AddActivity(reminders.DeliverReminderAsync)
            .AddActivity(reminders.DeliverReminderOccurrenceAsync)
            .AddActivity(reminders.FailReminderAsync)
            .AddActivity(files.ProcessStoredFileAsync)
            .AddActivity(tasks.RunTaskAsync)
            .AddActivity(tasks.CompleteApprovedTaskAsync)
            .AddActivity(tasks.FailTaskAsync)
            .AddActivity(tasks.GetTaskStatusAsync)
            .AddActivity(watches.CheckAsync)
            .AddActivity(watches.FailAsync)
            .AddActivity(briefings.ResolveScheduleAsync)
            .AddActivity(briefings.DeliverAsync)
            .AddActivity(heartbeat.RunAsync)
            .AddActivity(dreaming.RunAsync)
            .AddActivity(people.RunAsync)
            .AddActivity(automationRuns.ExecuteAsync)
            .AddActivity(automationRuns.CompleteAfterApprovalAsync)
            .AddActivity(automationRuns.CloseRunAsync)
            .AddActivity(automationSchedules.ResolveNextFireAsync)
            .AddActivity(automationSchedules.FireScheduleAsync)
            .AddActivity(automationPolls.CheckAsync)
            .AddActivity(automationPolls.FirePollAsync)
            .AddActivity(weeklyReviews.ResolveScheduleAsync)
            .AddActivity(weeklyReviews.DeliverAsync)
            .AddActivity(habitCheckIns.ResolveAsync)
            .AddActivity(habitCheckIns.DeliverAsync));
    }
}
