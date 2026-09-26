using Jarvis.Application.Workflows;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class DailyBriefingActivityContract
{
    [Activity("DeliverDailyBriefing")]
    public abstract Task<bool> DeliverAsync(DailyBriefingActivityInput input);
}

[Workflow]
public sealed class DailyBriefingWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(DailyBriefingWorkflowInput input)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
        var options = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromMinutes(1),
            RetryPolicy = new Temporalio.Common.RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(5),
                MaximumInterval = TimeSpan.FromMinutes(1),
                MaximumAttempts = 5
            }
        };

        while (true)
        {
            var utcNow = DateTime.SpecifyKind(Workflow.UtcNow, DateTimeKind.Utc);
            var now = new DateTimeOffset(utcNow);
            var fireAt = GetNextOccurrence(now, input.LocalTime, timeZone);
            await Workflow.DelayAsync(fireAt - now);

            var deliveredAt = DateTime.SpecifyKind(Workflow.UtcNow, DateTimeKind.Utc);
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(deliveredAt, timeZone).Date);
            var dayStart = ResolveLocalTime(localDate.ToDateTime(TimeOnly.MinValue), timeZone);
            var nextDayStart = ResolveLocalTime(localDate.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);
            try
            {
                var shouldContinue = await Workflow.ExecuteActivityAsync(
                    (DailyBriefingActivityContract activities) => activities.DeliverAsync(
                        new DailyBriefingActivityInput(input.OwnerId, input.WorkflowId, localDate,
                            input.TimeZoneId, dayStart, nextDayStart)), options);
                if (!shouldContinue) return;
            }
            catch (ActivityFailureException)
            {
                // Keep the daily schedule alive if data access or push delivery is temporarily unavailable.
            }

            if (Workflow.ContinueAsNewSuggested)
                throw Workflow.CreateContinueAsNewException(
                    (DailyBriefingWorkflow workflow) => workflow.RunAsync(input));
        }
    }

    private static DateTimeOffset GetNextOccurrence(DateTimeOffset now, TimeOnly localTime, TimeZoneInfo timeZone)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var candidate = ResolveLocalTime(localDate.ToDateTime(localTime), timeZone);
        if (candidate > now) return candidate;
        return ResolveLocalTime(localDate.AddDays(1).ToDateTime(localTime), timeZone);
    }

    private static DateTimeOffset ResolveLocalTime(DateTime local, TimeZoneInfo timeZone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        for (var minutes = 0; timeZone.IsInvalidTime(local) && minutes < 180; minutes++)
            local = local.AddMinutes(1);
        if (timeZone.IsInvalidTime(local))
            throw new InvalidOperationException("Could not resolve the local briefing time in the configured time zone.");

        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
