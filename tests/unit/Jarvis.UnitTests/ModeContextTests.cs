using Jarvis.Application.Integrations;
using Jarvis.Application.Modes;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ModeContextTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fe11");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fe12");
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Time = start;
        public override DateTimeOffset GetUtcNow() => Time;
    }

    private sealed class Feed(CalendarEventRecord? current) : ICalendarFeed
    {
        public Task<IReadOnlyList<CalendarEventRecord>> ListUpcomingAsync(Guid ownerId, DateTimeOffset from,
            DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarEventRecord>>(current is null ? [] : [current]);
    }

    private static (ModeContextService Context, ModeService Modes, Clock Time) Create(CalendarEventRecord? current = null,
        DateTimeOffset? now = null)
    {
        var store = new InMemorySettingsStore();
        var time = new Clock(now ?? Monday);
        var briefings = Fake<IDailyBriefingRepository>.Create(("GetAsync", _ => null));
        var modes = new ModeService(store, briefings, current is null ? null : new Feed(current), time);
        return (new ModeContextService(modes, store, time), modes, time);
    }

    [Fact]
    public async Task Normal_has_nothing_muted_and_no_exit()
    {
        var (context, _, _) = Create();
        var card = await context.GetAsync(Owner, default);

        Assert.Equal("normal", card.Mode);
        Assert.Equal("Nothing is muted.", card.Muted);
        Assert.Null(card.ExitLabel);
        Assert.False(card.CanCorrect);
        Assert.Contains("never grants a permission", card.Effects);
    }

    [Fact]
    public async Task Starting_focus_sets_a_timed_manual_mode_and_keeps_the_chosen_action()
    {
        var (context, modes, _) = Create();
        var started = await context.StartFocusAsync(Owner, "  Finish the   report ", 45, default);

        Assert.True(started.Succeeded);
        var card = started.Value!;
        Assert.Equal("focus", card.Mode);
        Assert.True(card.FocusRunning);
        Assert.Equal("Finish the report", card.Focus!.Intention);
        Assert.Equal(45, card.MinutesLeft);
        Assert.Equal("Switch it off", card.ExitLabel);
        Assert.Equal(["Reminders", "Approvals", "Triggered watches"], card.StillReaches);
        Assert.Contains("Approvals stay one tap away.", card.AlwaysAvailable);
        Assert.Equal("manual", (await modes.GetStateAsync(Owner, default)).Current.Source);
    }

    [Fact]
    public async Task Focus_is_validated_and_cannot_start_twice()
    {
        var (context, _, _) = Create();

        Assert.Equal("minutes", (await context.StartFocusAsync(Owner, "x", 2, default)).Field);
        Assert.Equal("minutes", (await context.StartFocusAsync(Owner, "x", 500, default)).Field);
        Assert.Equal("intention", (await context.StartFocusAsync(Owner, new string('a', 201), 30, default)).Field);
        Assert.True((await context.StartFocusAsync(Owner, "one", null, default)).Succeeded);
        Assert.Equal("focus", (await context.StartFocusAsync(Owner, "two", 30, default)).Field);
    }

    [Fact]
    public async Task Ending_focus_stores_the_reflection_and_returns_to_automatic()
    {
        var (context, modes, time) = Create();
        await context.StartFocusAsync(Owner, "Write", 45, default);
        time.Time = Monday.AddMinutes(20);

        var ended = await context.EndFocusAsync(Owner, "Got the outline done", default);

        Assert.True(ended.Succeeded);
        Assert.Equal("normal", ended.Value!.Mode);
        Assert.Null(ended.Value.Focus);
        Assert.Equal("default", (await modes.GetStateAsync(Owner, default)).Current.Source);
        var recent = await context.RecentAsync(Owner, default);
        Assert.Equal("Got the outline done", recent.Single().Reflection);
        Assert.Equal(Monday.AddMinutes(20), recent.Single().EndedAt);
        Assert.Equal("focus", (await context.EndFocusAsync(Owner, null, default)).Field);
    }

    [Fact]
    public async Task An_expired_session_returns_to_normal_and_asks_for_a_reflection()
    {
        var (context, _, time) = Create();
        await context.StartFocusAsync(Owner, "Write", 30, default);
        time.Time = Monday.AddMinutes(31);

        var card = await context.GetAsync(Owner, default);

        Assert.Equal("normal", card.Mode);
        Assert.False(card.FocusRunning);
        Assert.True(card.ReflectionDue);

        var skipped = await context.EndFocusAsync(Owner, null, default);
        Assert.True(skipped.Succeeded);
        var recent = await context.RecentAsync(Owner, default);
        Assert.Null(recent.Single().Reflection);
        Assert.Equal(Monday.AddMinutes(30), recent.Single().EndedAt);
    }

    [Fact]
    public async Task Another_manual_mode_does_not_count_as_a_running_focus()
    {
        var (context, modes, _) = Create();
        await context.StartFocusAsync(Owner, "Write", 45, default);
        await modes.SetModeAsync(Owner, "meeting", 60, default);

        var card = await context.GetAsync(Owner, default);
        Assert.Equal("meeting", card.Mode);
        Assert.False(card.FocusRunning);
        Assert.True(card.ReflectionDue);

        // Ending the session must not switch off the meeting the owner chose afterwards.
        var ended = await context.EndFocusAsync(Owner, "ok", default);
        Assert.Equal("meeting", ended.Value!.Mode);
    }

    [Fact]
    public async Task A_wrong_meeting_guess_can_be_corrected_until_it_would_have_ended()
    {
        var meeting = new CalendarEventRecord("Standup", Monday.AddMinutes(-10), Monday.AddMinutes(50));
        var (context, modes, _) = Create(meeting);

        var before = await context.GetAsync(Owner, default);
        Assert.Equal("meeting", before.Mode);
        Assert.Equal("auto", before.Source);
        Assert.True(before.CanCorrect);
        Assert.Contains("Standup", before.Reason);
        Assert.Equal("Not right now", before.ExitLabel);

        var corrected = await context.CorrectAsync(Owner, default);

        Assert.True(corrected.Succeeded);
        Assert.Equal("normal", corrected.Value!.Mode);
        Assert.Equal(50, corrected.Value.MinutesLeft);
        Assert.Equal("manual", (await modes.GetStateAsync(Owner, default)).Current.Source);
    }

    [Fact]
    public async Task Only_a_guess_can_be_corrected_and_manual_choices_win()
    {
        var (context, _, _) = Create();
        Assert.Equal("mode", (await context.CorrectAsync(Owner, default)).Field);

        await context.StartFocusAsync(Owner, null, 30, default);
        var refused = await context.CorrectAsync(Owner, default);
        Assert.False(refused.Succeeded);
        Assert.Contains("chose this mode yourself", refused.Message);
    }

    [Fact]
    public async Task Focus_sessions_are_private_to_their_owner()
    {
        var (context, _, _) = Create();
        await context.StartFocusAsync(Owner, "Mine", 30, default);

        Assert.Empty(await context.RecentAsync(Other, default));
        Assert.Null((await context.GetAsync(Other, default)).Focus);
    }

    [Fact]
    public async Task A_quiet_mode_says_what_is_held_back()
    {
        var (_, modes, _) = Create();
        var card = ModeContextRules.Describe((await modes.SetModeAsync(Owner, "meeting", 30, default)).Value!,
            new FocusState(), Monday);

        Assert.Equal("No pushes reach your phone; everything waits in the app.", card.Muted);
        Assert.Empty(card.StillReaches);
    }
}
