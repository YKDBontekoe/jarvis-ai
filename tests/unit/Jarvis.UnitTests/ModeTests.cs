using Jarvis.Agents.Modes;
using Jarvis.Application.Automations;
using Jarvis.Application.Conversations;
using Jarvis.Application.Integrations;
using Jarvis.Application.Modes;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ModeTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000aaaa");
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    // 2026-10-03 is a Saturday; 2026-10-05 a Monday.
    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);

    private static ModeDecision Decide(ModeSettings settings, DateTimeOffset now, CalendarEventRecord? current = null) =>
        ModeInference.Decide(settings, new ModeSignals(now, Utc, current));

    [Theory]
    [InlineData(5, 23, 30, ModeIds.Sleep)]
    [InlineData(5, 3, 0, ModeIds.Sleep)]
    [InlineData(5, 6, 59, ModeIds.Sleep)]
    [InlineData(5, 7, 0, ModeIds.Normal)]
    [InlineData(5, 14, 0, ModeIds.Normal)]
    [InlineData(3, 14, 0, ModeIds.Weekend)]
    public void The_clock_decides_sleep_and_the_weekend(int day, int hour, int minute, string expected) =>
        Assert.Equal(expected, Decide(ModeSettings.Default, At(day, hour, minute)).Mode);

    [Fact]
    public void A_meeting_in_progress_switches_to_meeting_until_it_ends()
    {
        var standup = new CalendarEventRecord("Stand-up", At(5, 9), At(5, 9, 30));

        var during = Decide(ModeSettings.Default, At(5, 9, 10), standup);
        var after = Decide(ModeSettings.Default, At(5, 9, 40), standup);
        var allDay = Decide(ModeSettings.Default, At(5, 12), new CalendarEventRecord("Holiday", At(5, 0), At(6, 0)));

        Assert.Equal((ModeIds.Meeting, ModeSources.Auto), (during.Mode, during.Source));
        Assert.Equal(At(5, 9, 30), during.Until);
        Assert.Contains("Stand-up", during.Reason);
        Assert.Equal(ModeIds.Normal, after.Mode);
        Assert.Equal(ModeIds.Normal, allDay.Mode);
    }

    [Fact]
    public void Sleep_beats_a_meeting_and_a_manual_choice_beats_everything_until_it_expires()
    {
        var late = new CalendarEventRecord("Call", At(5, 23), At(5, 23, 59));
        var manual = ModeSettings.Default with { Manual = new ActiveModeState(ModeIds.Focus, At(5, 13), At(5, 15)) };

        Assert.Equal(ModeIds.Sleep, Decide(ModeSettings.Default, At(5, 23, 30), late).Mode);
        var chosen = Decide(manual, At(5, 14));
        Assert.Equal((ModeIds.Focus, ModeSources.Manual), (chosen.Mode, chosen.Source));
        Assert.Equal(ModeIds.Normal, Decide(manual, At(5, 15, 1)).Mode);
        var open = manual with { Manual = manual.Manual! with { Until = null } };
        Assert.Equal(ModeIds.Focus, Decide(open, At(5, 3)).Mode);
    }

    [Fact]
    public void Automatic_modes_can_be_switched_off_and_custom_sleep_hours_are_used()
    {
        var off = ModeSettings.Default with { Auto = false };
        var late = ModeSettings.Default with { SleepStart = "01:00", SleepEnd = "09:00" };

        Assert.Equal(ModeIds.Normal, Decide(off, At(5, 3)).Mode);
        Assert.Equal(ModeIds.Normal, Decide(late, At(5, 23, 30)).Mode);
        Assert.Equal(ModeIds.Sleep, Decide(late, At(5, 8)).Mode);
        Assert.False(ModeInference.InWindow(new TimeOnly(5, 0), new TimeOnly(5, 0), new TimeOnly(5, 0)));
        Assert.Null(ModeInference.ParseTime("25:00"));
    }

    [Fact]
    public void Notification_levels_filter_what_reaches_the_phone()
    {
        Assert.True(NotificationLevels.Allows(NotificationLevels.All, "briefing.daily"));
        Assert.True(NotificationLevels.Allows(NotificationLevels.Important, "reminder.due"));
        Assert.True(NotificationLevels.Allows(NotificationLevels.Important, "approval.required"));
        Assert.False(NotificationLevels.Allows(NotificationLevels.Important, "task.completed"));
        Assert.False(NotificationLevels.Allows(NotificationLevels.None, "reminder.due"));
        Assert.Equal(ModeIds.All.Count, ModeCatalog.Defaults.Count);
        Assert.All(ModeIds.All, id => Assert.NotNull(ModeCatalog.Find(id)));
    }

    [Fact]
    public async Task Switching_validates_expires_and_clears()
    {
        var (service, _) = Create(At(5, 14));

        var bad = await service.SetModeAsync(Owner, "party", null, default);
        var tooLong = await service.SetModeAsync(Owner, "focus", 99_999, default);
        var timed = await service.SetModeAsync(Owner, "Focus", 90, default);
        var cleared = await service.SetModeAsync(Owner, "auto", null, default);

        Assert.Equal("mode", bad.Field);
        Assert.Equal("minutes", tooLong.Field);
        Assert.Equal(ModeIds.Focus, timed.Value!.Current.Mode);
        Assert.Equal(At(5, 15, 30), timed.Value.Current.Until);
        Assert.Equal(ModeIds.Normal, cleared.Value!.Current.Mode);
    }

    [Fact]
    public async Task Policies_can_be_customised_and_reset_per_mode()
    {
        var (service, _) = Create(At(5, 14));

        var custom = await service.SavePolicyAsync(Owner, "focus", "none", "  Be   terse. ", default);
        var focus = custom.Value!.Modes.Single(x => x.Definition.Id == ModeIds.Focus);
        var invalid = await service.SavePolicyAsync(Owner, "focus", "loud", null, default);
        var unknown = await service.SavePolicyAsync(Owner, "party", "all", null, default);
        var reset = await service.ResetPolicyAsync(Owner, "focus", default);

        Assert.Equal(("none", "Be terse.", true), (focus.Definition.Policy.Notifications, focus.Definition.Policy.Tone, focus.Customised));
        Assert.Equal("notifications", invalid.Field);
        Assert.Equal("mode", unknown.Field);
        Assert.Equal(NotificationLevels.Important,
            reset.Value!.Modes.Single(x => x.Definition.Id == ModeIds.Focus).Definition.Policy.Notifications);
    }

    [Fact]
    public async Task Pushes_are_held_back_in_quiet_modes_and_never_when_the_state_is_unreadable()
    {
        var (service, _) = Create(At(5, 14));

        Assert.True(await service.ShouldPushAsync(Owner, "task.completed", default));
        await service.SetModeAsync(Owner, "focus", null, default);
        Assert.False(await service.ShouldPushAsync(Owner, "task.completed", default));
        Assert.True(await service.ShouldPushAsync(Owner, "reminder.due", default));
        await service.SetModeAsync(Owner, "sleep", null, default);
        Assert.False(await service.ShouldPushAsync(Owner, "reminder.due", default));

        var broken = new ModeService(Fake<Jarvis.Application.Settings.IOwnerSettingsStore>.Create(
            ("GetAsync", _ => throw new InvalidOperationException("boom"))), Briefings());
        Assert.True(await broken.ShouldPushAsync(Owner, "reminder.due", default));
    }

    [Fact]
    public async Task The_calendar_is_only_asked_for_in_automatic_mode_and_a_failure_is_ignored()
    {
        var asked = 0;
        var calendar = Fake<ICalendarFeed>.Create(("ListUpcomingAsync", _ =>
        {
            asked++;
            return (IReadOnlyList<CalendarEventRecord>)[new CalendarEventRecord("Review", At(5, 13, 30), At(5, 14, 30))];
        }));
        var (service, _) = Create(At(5, 14), calendar);

        var auto = await service.GetStateAsync(Owner, default);
        await service.SetModeAsync(Owner, "focus", null, default);
        var before = asked;
        await service.GetStateAsync(Owner, default);

        Assert.Equal(ModeIds.Meeting, auto.Current.Mode);
        Assert.Equal(before, asked);

        var failing = Fake<ICalendarFeed>.Create(("ListUpcomingAsync", _ => throw new HttpRequestException()));
        var (other, _) = Create(At(5, 14), failing, Guid.NewGuid());
        Assert.Equal(ModeIds.Normal, (await other.GetStateAsync(Guid.NewGuid(), default)).Current.Mode);
    }

    [Fact]
    public void Set_mode_is_a_valid_automation_action_and_round_trips()
    {
        var definition = new AutomationRuleDefinition(1,
            new EventTriggerDefinition(AutomationEventKinds.Webhook, null, null), null,
            [new SetModeActionDefinition("focus", 60)], null);

        var parsed = AutomationDefinitionJson.Parse(AutomationDefinitionJson.Serialize(definition));

        Assert.Equal("focus", Assert.IsType<SetModeActionDefinition>(parsed.Actions[0]).Mode);
        Assert.Throws<ArgumentException>(() => AutomationRuleValidator.Validate(definition with
        {
            Actions = [new SetModeActionDefinition("party", null)]
        }));
        Assert.Throws<ArgumentException>(() => AutomationRuleValidator.Validate(definition with
        {
            Actions = [new SetModeActionDefinition("focus", 5000)]
        }));
        AutomationRuleValidator.Validate(definition with { Actions = [new SetModeActionDefinition("auto", null)] });
        Assert.Equal("Switch to focus mode", AutomationSimulator.Label(new SetModeActionDefinition("focus", null)));
    }

    [Fact]
    public async Task The_agent_tools_describe_and_switch_the_mode()
    {
        var (service, _) = Create(At(5, 14));
        var tools = new ModeAgentTools(service, new FixedUser());

        var before = await tools.GetCurrentModeAsync();
        var switched = await tools.SetModeAsync("meeting", 1.5);
        var invalid = await tools.SetModeAsync("party");
        var auto = await tools.SetModeAsync("auto");

        Assert.Contains("Mode: Normal", before);
        Assert.Contains("Switched to Meeting until", switched);
        Assert.Contains("could not switch", invalid);
        Assert.Contains("Automatic modes are back", auto);
    }

    private static IDailyBriefingRepository Briefings() =>
        Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null));

    private static (ModeService Service, InMemorySettingsStore Store) Create(DateTimeOffset now,
        ICalendarFeed? calendar = null, Guid? _ = null)
    {
        var store = new InMemorySettingsStore();
        return (new ModeService(store, Briefings(), calendar, new FixedClock(now)), store);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public Guid OwnerId => Owner;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
