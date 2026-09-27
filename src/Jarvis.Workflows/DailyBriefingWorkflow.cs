using Jarvis.Application.Workflows;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Jarvis.Workflows;

public abstract class DailyBriefingActivityContract
{
    [Activity("ResolveDailyBriefingSchedule")]
    public abstract Task<DailyBriefingSchedule> ResolveScheduleAsync(DailyBriefingWorkflowInput input);

    [Activity("DeliverDailyBriefing")]
    public abstract Task<bool> DeliverAsync(DailyBriefingActivityInput input);
}

[Workflow]
public sealed class DailyBriefingWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(DailyBriefingWorkflowInput input)
    {
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
            DailyBriefingActivityInput delivery;
            if (Workflow.Patched("daily-briefing-schedule-activity"))
            {
                var schedule = await Workflow.ExecuteActivityAsync(
                    (DailyBriefingActivityContract activities) => activities.ResolveScheduleAsync(input), options);
                var now = new DateTimeOffset(DateTime.SpecifyKind(Workflow.UtcNow, DateTimeKind.Utc));
                var delay = schedule.FireAt - now;
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                await Workflow.DelayAsync(delay);
                delivery = new DailyBriefingActivityInput(input.OwnerId, input.WorkflowId, schedule.LocalDate,
                    input.TimeZoneId, schedule.LocalDayStart, schedule.NextLocalDayStart);
            }
            else
            {
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
                var now = new DateTimeOffset(DateTime.SpecifyKind(Workflow.UtcNow, DateTimeKind.Utc));
                var fireAt = GetNextOccurrence(now, input.LocalTime, timeZone);
                await Workflow.DelayAsync(fireAt - now);

                var deliveredAt = DateTime.SpecifyKind(Workflow.UtcNow, DateTimeKind.Utc);
                var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(deliveredAt, timeZone).Date);
                var dayStart = ResolveLocalTime(localDate.ToDateTime(TimeOnly.MinValue), timeZone);
                var nextDayStart = ResolveLocalTime(localDate.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);
                delivery = new DailyBriefingActivityInput(input.OwnerId, input.WorkflowId, localDate,
                    input.TimeZoneId, dayStart, nextDayStart);
            }

            var sameDayAttempts = 0;
            while (true)
            {
                try
                {
                    var shouldContinue = await Workflow.ExecuteActivityAsync(
                        (DailyBriefingActivityContract activities) => activities.DeliverAsync(delivery), options);
                    if (!shouldContinue) return;
                    break;
                }
                catch (ActivityFailureException)
                {
                    // Keep the daily schedule alive if data access or push delivery is temporarily unavailable.
                    if (!Workflow.Patched("daily-briefing-retry-same-day")) break;
                    if (Workflow.Patched("daily-briefing-abandon-stale-day"))
                    {
                        sameDayAttempts++;
                        var now = new DateTimeOffset(DateTime.SpecifyKind(Workflow.UtcNow, DateTimeKind.Utc));
                        if (sameDayAttempts >= 12 || now >= delivery.NextLocalDayStart)
                        {
                            var wait = delivery.NextLocalDayStart - now;
                            if (wait > TimeSpan.Zero) await Workflow.DelayAsync(wait);
                            break;
                        }
                    }
                    await Workflow.DelayAsync(TimeSpan.FromMinutes(5));
                }
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
