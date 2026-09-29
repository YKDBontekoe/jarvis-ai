using System.Text.Json;
using Jarvis.Application.Files;
using Jarvis.Application.Learning;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

/// <summary>
/// Guards JSON shapes used as Temporal workflow and activity payloads.
/// </summary>
public sealed class WorkerWorkflowPayloadCompatibilityTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(typeof(ReminderWorkflowInput))]
    [InlineData(typeof(ReminderDeliveryResult))]
    [InlineData(typeof(FileProcessingInput))]
    [InlineData(typeof(JarvisTaskWorkflowInput))]
    [InlineData(typeof(JarvisTaskApprovalInput))]
    [InlineData(typeof(ConditionWatchWorkflowInput))]
    [InlineData(typeof(ConditionWatchCheckResult))]
    [InlineData(typeof(DailyBriefingWorkflowInput))]
    [InlineData(typeof(DailyBriefingActivityInput))]
    [InlineData(typeof(DailyBriefingSchedule))]
    [InlineData(typeof(HeartbeatWorkflowInput))]
    [InlineData(typeof(HeartbeatRunResult))]
    [InlineData(typeof(DreamingWorkflowInput))]
    [InlineData(typeof(DreamingRunResult))]
    public void Representative_workflow_payloads_round_trip(Type payloadType)
    {
        var sample = CreateSample(payloadType);
        var json = JsonSerializer.Serialize(sample, payloadType, Web);
        var roundTrip = JsonSerializer.Deserialize(json, payloadType, Web);
        Assert.NotNull(roundTrip);
        Assert.Equal(json, JsonSerializer.Serialize(roundTrip, payloadType, Web));
    }

    private static object CreateSample(Type type)
    {
        if (type == typeof(ReminderWorkflowInput))
            return new ReminderWorkflowInput(Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"), "Title", DateTimeOffset.Parse("2026-01-02T08:00:00Z"));
        if (type == typeof(ReminderDeliveryResult))
            return new ReminderDeliveryResult(true, DateTimeOffset.Parse("2026-01-03T08:00:00Z"), "Title");
        if (type == typeof(FileProcessingInput))
            return new FileProcessingInput(Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Guid.Parse("44444444-4444-4444-4444-444444444444"));
        if (type == typeof(JarvisTaskWorkflowInput))
            return new JarvisTaskWorkflowInput(Guid.Parse("55555555-5555-5555-5555-555555555555"));
        if (type == typeof(JarvisTaskApprovalInput))
            return new JarvisTaskApprovalInput(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Done");
        if (type == typeof(ConditionWatchWorkflowInput))
            return new ConditionWatchWorkflowInput(Guid.Parse("66666666-6666-6666-6666-666666666666"));
        if (type == typeof(ConditionWatchCheckResult))
            return new ConditionWatchCheckResult(true, 15);
        if (type == typeof(DailyBriefingWorkflowInput))
            return new DailyBriefingWorkflowInput(Guid.Parse("77777777-7777-7777-7777-777777777777"),
                "daily-briefing-1", new TimeOnly(8, 0), "Europe/Amsterdam");
        if (type == typeof(DailyBriefingActivityInput))
            return new DailyBriefingActivityInput(Guid.Parse("77777777-7777-7777-7777-777777777777"),
                "daily-briefing-1", new DateOnly(2026, 1, 2), "Europe/Amsterdam",
                DateTimeOffset.Parse("2026-01-02T07:00:00Z"), DateTimeOffset.Parse("2026-01-03T07:00:00Z"));
        if (type == typeof(DailyBriefingSchedule))
            return new DailyBriefingSchedule(DateTimeOffset.Parse("2026-01-02T07:00:00Z"), new DateOnly(2026, 1, 2),
                DateTimeOffset.Parse("2026-01-02T07:00:00Z"), DateTimeOffset.Parse("2026-01-03T07:00:00Z"));
        if (type == typeof(HeartbeatWorkflowInput))
            return new HeartbeatWorkflowInput(Guid.Parse("88888888-8888-8888-8888-888888888888"));
        if (type == typeof(HeartbeatRunResult))
            return new HeartbeatRunResult(true, 30);
        if (type == typeof(DreamingWorkflowInput))
            return new DreamingWorkflowInput(Guid.Parse("99999999-9999-9999-9999-999999999999"));
        if (type == typeof(DreamingRunResult))
            return new DreamingRunResult(true, 120);
        throw new ArgumentOutOfRangeException(nameof(type));
    }
}
