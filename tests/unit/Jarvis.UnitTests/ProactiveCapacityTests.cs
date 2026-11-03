using Jarvis.Application.Goals;
using Jarvis.Application.Integrations;
using Jarvis.Application.Planner;
using Jarvis.Application.Proactivity;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ProactiveCapacityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed class Clock(DateTimeOffset at) : TimeProvider { public override DateTimeOffset GetUtcNow() => at; }
    private static ProactiveCase Case() => new(Guid.NewGuid(), Guid.NewGuid(), "fixture", "goal_capacity", "Week", "Keep goals realistic",
        "open", true, null, null, [], null, new(QuietStart: 0, QuietEnd: 0, UseCalendarCapacity: true), [], [], null, 120,
        "Review", [], [], [], [], Now, Now, null, Now);
    private static ProactiveCapacityService Service(IReadOnlyList<CalendarEventRecord>? events = null, bool connected = true,
        DateTimeOffset? at = null)
    {
        var plan = new GoalPlan(new(2026, 11, 2), [new(Guid.NewGuid(), "Finished workout", 30, GoalPlanStatuses.Done, null, "done")]);
        var goal = new GoalRecord(Guid.NewGuid(), "Health", null, null, null, 7, 120, "active", null, [], [], Now, Now, [], plan, [], new Dictionary<string, DateOnly>());
        var day = new DayPlanState(new(2026, 11, 2), [], new(9, 0), new(17, 0), null, "UTC");
        return new(Fake<IOwnerSettingDocuments>.Create(("ReadDocumentAsync", args =>
                (string)args[1]! == GoalState.Section ? ProactiveRules.Serialize(new GoalState([goal])) : ProactiveRules.Serialize(day))),
            Fake<ICalendarFeed>.Create(("ListUpcomingAsync", _ => events ?? Array.Empty<CalendarEventRecord>())),
            Fake<IIntegrationCredentialStore>.Create(("GetStatusAsync", _ => connected ? new IntegrationCredentialStatus("calendar", [], Now) : null)),
            new Clock(at ?? Now));
    }

    [Fact]
    public async Task Calendar_capacity_uses_one_remaining_week_and_counts_completed_work_once()
    {
        var result = await Service([
            new("Meeting", Now, Now.AddHours(4)),
            new("Away", new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero), new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero))])
            .CalculateAsync(Case(), Ct);
        Assert.Equal(30, result.ConsumedMinutes);
        Assert.Equal(55, result.RemainingMinutes);
        Assert.Equal(85, result.TotalMinutes);
        Assert.Contains("min(90, 55) = 55", result.Explanation);
    }

    [Fact]
    public async Task Disconnected_calendar_and_events_without_end_times_leave_capacity_unknown()
    {
        Assert.Null((await Service(connected: false).CalculateAsync(Case(), Ct)).TotalMinutes);
        var result = await Service([new("Uncertain appointment", Now, null)]).CalculateAsync(Case(), Ct);
        Assert.Null(result.TotalMinutes);
        Assert.Contains("Confirm its duration", result.Explanation);
    }

    [Fact]
    public async Task Week_boundaries_follow_the_owner_time_zone_and_a_calendar_can_supply_the_budget()
    {
        var value = Case() with { AvailableMinutes = null, Policy = Case().Policy with { TimeZoneId = "Europe/Amsterdam" } };
        var result = await Service(at: new(2026, 11, 1, 23, 30, 0, TimeSpan.Zero)).CalculateAsync(value, Ct);
        Assert.Equal(new DateOnly(2026, 11, 2), result.WeekStart);
        Assert.Equal(2400, result.RemainingMinutes);
        Assert.Equal(2430, result.TotalMinutes);
    }
}
