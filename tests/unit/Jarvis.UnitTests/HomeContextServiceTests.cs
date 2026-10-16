using Jarvis.Application.Devices;
using Jarvis.Application.Home;
using Jarvis.Application.Integrations;
using Jarvis.Application.People;
using Jarvis.Application.People.Radar;
using Jarvis.Application.Routines;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.People;
using Jarvis.Infrastructure.Home;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class HomeContextServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 7, 14, 0, TimeSpan.Zero);
    private readonly Guid _owner = Guid.CreateVersion7();
    private readonly Dictionary<(Guid, string), object> _settings = [];
    private readonly List<Guid> _routinesDismissed = [];

    private HomeContextService Service(bool radarFails = false, Func<object?[], object?>? remindersList = null,
        Func<object?[], object?>? tasksList = null, TimeSpan? sourceTimeout = null)
    {
        var reminder = new ReminderRecord(Guid.CreateVersion7(), _owner, "Joëlle on campus", Now.AddMinutes(46),
            "wf", "pending", Now.AddDays(-1), null);
        var done = new ReminderRecord(Guid.CreateVersion7(), _owner, "Water the basil", Now.AddHours(-2), "wf2",
            "completed", Now.AddDays(-1), Now.AddHours(-1));
        var rosa = new Person(Guid.CreateVersion7(), _owner, "rosa", "Partner", null, null, null, null, null, null,
            null, null, null, Now, Now);
        var link = new PersonChannelLink(Guid.CreateVersion7(), _owner, rosa.Id, Guid.CreateVersion7(), "+31633", null,
            null, null, Now);
        return new HomeContextService(
            Fake<IReminderService>.Create(("ListAsync", remindersList ?? (_ => (IReadOnlyList<ReminderRecord>)[reminder, done]))),
            Fake<IJarvisTaskService>.Create(("ListAsync", tasksList ?? (_ => (IReadOnlyList<JarvisTaskRecord>)[]))),
            Fake<ICalendarFeed>.Create(("ListUpcomingAsync", _ => (IReadOnlyList<CalendarEventRecord>)[])),
            Fake<IDeviceTelemetryStore>.Create(("GetAsync", _ => (DeviceTelemetryRecord?)null)),
            Fake<IDailyBriefingService>.Create(("GetAsync", _ => new DailyBriefingPreferenceRecord(_owner, true,
                new TimeOnly(8, 0), "Europe/Amsterdam", "wf", null, null))),
            Fake<INotificationRepository>.Create(("ListNotificationsAsync", _ => (IReadOnlyList<NotificationRecord>)[])),
            Fake<IPeopleService>.Create(("ListAsync", _ => (IReadOnlyList<Person>)[rosa])),
            Fake<IPersonLinkRepository>.Create(("ListAsync", _ => (IReadOnlyList<PersonChannelLink>)[link])),
            Fake<IRelationshipRadarService>.Create(("OverviewAsync", _ => radarFails
                ? Task.FromException<RadarOverview>(new InvalidOperationException("radar down"))
                : Task.FromResult(new RadarOverview(false, [])))),
            Fake<IRoutineSuggestionService>.Create(
                ("ListAsync", _ => (IReadOnlyList<RoutineSuggestionView>)[]),
                ("DismissAsync", args =>
                {
                    _routinesDismissed.Add((Guid)args[0]!);
                    return true;
                })),
            Fake<IOwnerSettingsStore>.Create(
                ("GetAsync", args => _settings.GetValueOrDefault(((Guid)args[0]!, (string)args[1]!))),
                ("SaveAsync", args =>
                {
                    _settings[((Guid)args[0]!, (string)args[1]!)] = args[2]!;
                    return Task.CompletedTask;
                })),
            NullLogger<HomeContextService>.Instance,
            new FakeTimeProvider(Now), sourceTimeout);
    }

    [Fact]
    public async Task Home_context_reads_the_day_people_and_recap()
    {
        var context = await Service().GetAsync(_owner, CancellationToken.None);

        Assert.Equal("Just one thing today: Joëlle on campus at 10:00.", context.DayLine);
        Assert.Equal(HomeHeroKinds.Soon, context.Hero!.Kind);
        Assert.Equal("Partner", context.People.Single().Relationship);
        Assert.Equal(1, context.DayRecap.RemindersDone);
        Assert.Equal(["Water the basil"], context.DayRecap.Done);
    }

    [Fact]
    public async Task A_failing_source_only_leaves_its_part_out()
    {
        var context = await Service(radarFails: true).GetAsync(_owner, CancellationToken.None);

        Assert.NotNull(context.Hero);
        // The person is still there, only without radar numbers.
        Assert.Null(context.People.Single().UsualReplyMinutes);
    }

    [Fact]
    public async Task Dismissing_hides_the_card_for_this_owner_and_declines_routines()
    {
        var service = Service();
        var hero = (await service.GetAsync(_owner, CancellationToken.None)).Hero!;

        Assert.True(await service.DismissAsync(_owner, new HomeContextDismissRequest(hero.Id, hero.Kind),
            CancellationToken.None));
        Assert.Null((await service.GetAsync(_owner, CancellationToken.None)).Hero);

        var routine = Guid.CreateVersion7();
        Assert.True(await service.DismissAsync(_owner,
            new HomeContextDismissRequest($"routine:{routine}", HomeInsightKinds.Routine), CancellationToken.None));
        Assert.Equal([routine], _routinesDismissed);

        Assert.False(await service.DismissAsync(_owner, new HomeContextDismissRequest("x", "unknown"),
            CancellationToken.None));
        Assert.False(await service.DismissAsync(_owner, new HomeContextDismissRequest(" ", HomeHeroKinds.Soon),
            CancellationToken.None));
        // Another owner's Home is untouched.
        var other = Guid.CreateVersion7();
        Assert.False(_settings.ContainsKey((other, HomeContextDismissals.Section)));
    }

    [Fact]
    public async Task A_slow_source_is_cancelled_before_the_next_one_uses_the_shared_context()
    {
        var reminderRunning = false;
        var cancelled = false;
        var overlapped = false;
        var service = Service(
            remindersList: args => SlowReminders((CancellationToken)args[1]!),
            tasksList: _ =>
            {
                // Home's sources share one database context, which cannot run two queries at once.
                overlapped = reminderRunning;
                return (IReadOnlyList<JarvisTaskRecord>)[];
            },
            sourceTimeout: TimeSpan.FromMilliseconds(100));

        async Task<IReadOnlyList<ReminderRecord>> SlowReminders(CancellationToken token)
        {
            reminderRunning = true;
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return [];
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }
            finally
            {
                reminderRunning = false;
            }
        }

        var context = await service.GetAsync(_owner, CancellationToken.None);

        Assert.True(cancelled, "the slow source should be told to stop");
        Assert.False(overlapped, "the next source must not start while the slow one is still running");
        // The day is still composed from what did answer.
        Assert.Equal("Nothing on your plate today. A good day to get ahead.", context.DayLine);
    }

    [Fact]
    public async Task Cancelling_the_request_is_not_swallowed_as_a_failing_source()
    {
        using var cancel = new CancellationTokenSource();
        var service = Service(remindersList: args =>
        {
            cancel.Cancel();
            return Task.FromCanceled<IReadOnlyList<ReminderRecord>>((CancellationToken)args[1]!);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(_owner, cancel.Token));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
