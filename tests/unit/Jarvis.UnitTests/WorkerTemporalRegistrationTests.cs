using System.Reflection;
using Jarvis.Worker.Hosting;
using Jarvis.Workflows;
using Temporalio.Activities;
using Temporalio.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class WorkerTemporalRegistrationTests
{
    [Fact]
    public void Registered_activity_names_match_implementations()
    {
        var implementationTypes = new[]
        {
            typeof(Jarvis.Worker.Activities.ReminderActivities),
            typeof(Jarvis.Worker.Activities.FileProcessingActivities),
            typeof(Jarvis.Worker.Activities.JarvisTaskActivities),
            typeof(Jarvis.Worker.Activities.ConditionWatchActivities),
            typeof(Jarvis.Worker.Activities.DailyBriefingActivities),
            typeof(Jarvis.Worker.Learning.AssistantHeartbeatActivities),
            typeof(Jarvis.Worker.Learning.AssistantDreamingActivities),
            typeof(Jarvis.Worker.AutomationRunActivities),
            typeof(Jarvis.Worker.AutomationScheduleActivities),
            typeof(Jarvis.Worker.AutomationPollActivities),
            typeof(Jarvis.Worker.Activities.WeeklyReviewActivities),
        };

        var discovered = implementationTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Select(method => method.GetCustomAttribute<ActivityAttribute>()?.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = TemporalWorkerRegistration.ActivityTypeNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, discovered);
    }

    [Fact]
    public void Registered_workflow_types_match_workflow_assembly()
    {
        var workflowTypes = new[]
        {
            typeof(ReminderWorkflow),
            typeof(FileProcessingWorkflow),
            typeof(JarvisTaskWorkflow),
            typeof(ConditionWatchWorkflow),
            typeof(DailyBriefingWorkflow),
            typeof(AssistantHeartbeatWorkflow),
            typeof(AssistantDreamingWorkflow),
            typeof(AutomationRunWorkflow),
            typeof(AutomationScheduleWorkflow),
            typeof(AutomationPollWorkflow),
            typeof(WeeklyReviewWorkflow),
        };

        var discovered = workflowTypes
            .Select(type => type.GetCustomAttribute<WorkflowAttribute>() is not null ? type.Name : null)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = TemporalWorkerRegistration.WorkflowTypeNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, discovered);
    }
}
