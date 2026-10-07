using Jarvis.Application.Workflows;
using Jarvis.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class DailyBriefingDefaultTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000beef");

    [Fact]
    public async Task First_time_owner_gets_the_briefing_at_eight_in_the_device_zone()
    {
        var repository = new Repository();
        var scheduler = new Scheduler();
        var service = new DailyBriefingService(repository, scheduler, NullLogger<DailyBriefingService>.Instance);

        var preference = await service.EnsureDefaultAsync(Owner, "Europe/Amsterdam", CancellationToken.None);

        Assert.True(preference.Enabled);
        Assert.Equal(new TimeOnly(8, 0), preference.LocalTime);
        Assert.Equal("Europe/Amsterdam", preference.TimeZoneId);
        Assert.Single(scheduler.Scheduled);
    }

    [Fact]
    public async Task Saved_briefing_settings_are_never_overridden_even_when_switched_off()
    {
        var saved = new DailyBriefingPreferenceRecord(Owner, false, new TimeOnly(6, 30), "America/New_York", "wf",
            DateTimeOffset.UtcNow, null);
        var repository = new Repository(saved);
        var scheduler = new Scheduler();
        var service = new DailyBriefingService(repository, scheduler, NullLogger<DailyBriefingService>.Instance);

        var preference = await service.EnsureDefaultAsync(Owner, "Europe/Amsterdam", CancellationToken.None);

        Assert.Equal(saved, preference);
        Assert.Equal(0, repository.Saves);
        Assert.Empty(scheduler.Scheduled);
    }

    [Fact]
    public async Task Unknown_zone_is_rejected_instead_of_saved()
    {
        var repository = new Repository();
        var service = new DailyBriefingService(repository, new Scheduler(),
            NullLogger<DailyBriefingService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.EnsureDefaultAsync(Owner, "Mars/Olympus", CancellationToken.None));
        Assert.Equal(0, repository.Saves);
    }

    private sealed class Scheduler : IDailyBriefingScheduler
    {
        public List<DailyBriefingWorkflowInput> Scheduled { get; } = [];

        public Task ScheduleAsync(DailyBriefingWorkflowInput input, CancellationToken cancellationToken)
        {
            Scheduled.Add(input);
            return Task.CompletedTask;
        }

        public Task CancelAsync(string workflowId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Repository(DailyBriefingPreferenceRecord? existing = null) : IDailyBriefingRepository
    {
        private DailyBriefingPreferenceRecord? _current = existing;

        public int Saves { get; private set; }

        public Task<DailyBriefingPreferenceRecord?> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(_current);

        public Task<(DailyBriefingPreferenceRecord Preference, string PreviousWorkflowId)> SaveAsync(Guid ownerId,
            SaveDailyBriefingRequest request, CancellationToken cancellationToken)
        {
            Saves++;
            var previous = _current?.WorkflowId ?? string.Empty;
            _current = new DailyBriefingPreferenceRecord(ownerId, request.Enabled, request.LocalTime,
                request.TimeZoneId, "workflow-" + Saves, null, null);
            return Task.FromResult((_current, previous));
        }

        public Task<IReadOnlyList<DailyBriefingPreferenceRecord>> ListPendingForSchedulingAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> RequeueStaleEnabledAsync(DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkScheduleDispatchedAsync(Guid ownerId, string workflowId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> DeliverAsync(DailyBriefingActivityInput input, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
