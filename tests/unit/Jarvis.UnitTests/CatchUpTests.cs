using Jarvis.Application.Modes;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class CatchUpTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fd21");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fd22");
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Time = Now;
        public override DateTimeOffset GetUtcNow() => Time;
    }

    private sealed class World
    {
        public readonly InMemorySettingsStore Settings = new();
        public readonly Clock Time = new();
        public bool PushAllowed;
        public readonly List<string> Sent = [];

        public PushDigestService Digest => new(Settings,
            Fake<IModeService>.Create(("ShouldPushAsync", _ => PushAllowed)),
            Fake<INotificationRepository>.Create(("CreateAsync", args =>
            {
                Sent.Add((string)args[3]!);
                return new NotificationRecord(Guid.NewGuid(), (string)args[1]!, (string)args[2]!, (string)args[3]!, null, Now, null);
            })), Time);

        public CatchUpService CatchUp => new(Settings, Fake<IModeService>.Create(("ShouldPushAsync", _ => PushAllowed)), Time);

        public async Task Hold(string type, string title, Guid? owner = null) =>
            await Digest.HoldAsync(owner ?? Owner, Guid.NewGuid(), type, title, default);
    }

    [Fact]
    public async Task Held_items_are_grouped_by_kind_and_say_they_are_still_held()
    {
        var world = new World();
        await world.Hold("reminder.due", "Take medication");
        await world.Hold("approval.required", "Send the quote");
        await world.Hold("task.completed", "Report finished");
        await world.Hold("task.completed", "Report finished");

        var view = await world.CatchUp.GetAsync(Owner, default);

        Assert.True(view.StillHolding);
        Assert.Equal(4, view.Total);
        Assert.Equal(["approvals", "reminders", "updates"], view.Groups.Select(g => g.Domain).ToArray());
        var updates = view.Groups.Single(g => g.Domain == "updates");
        Assert.Equal(2, updates.Count);
        Assert.Equal(2, updates.Items.Single().Count);
        Assert.Contains("held back until your mode lets pushes through", view.Summary);
    }

    [Fact]
    public async Task After_the_digest_the_catch_up_stays_for_a_day_then_expires()
    {
        var world = new World();
        await world.Hold("reminder.due", "Take medication");
        world.PushAllowed = true;
        Assert.True(await world.Digest.FlushIfDueAsync(Owner, default));

        var view = await world.CatchUp.GetAsync(Owner, default);
        Assert.False(view.StillHolding);
        Assert.Equal(1, view.Total);
        Assert.Contains("came in while pushes were quiet", view.Summary);

        world.Time.Time = Now.AddHours(25);
        var later = await world.CatchUp.GetAsync(Owner, default);
        Assert.Equal(0, later.Total);
        Assert.Equal("Nothing was held back.", later.Summary);
    }

    [Fact]
    public async Task A_left_out_kind_is_not_summed_up_but_stays_counted_and_in_the_app()
    {
        var world = new World();
        await world.Hold("reminder.due", "Take medication");
        await world.Hold("task.completed", "Report finished");
        var set = await world.CatchUp.SetDomainAsync(Owner, "updates", false, default);

        Assert.Null(set.Error);
        var updates = set.View!.Groups.Single(g => g.Domain == "updates");
        Assert.False(updates.Included);
        Assert.Empty(updates.Items);
        Assert.Equal(1, set.View.Total);
        Assert.Equal(1, set.View.Groups.Single(g => g.Domain == "updates").Count);

        world.PushAllowed = true;
        await world.Digest.FlushIfDueAsync(Owner, default);
        Assert.Equal("1 notification came in:\n• Take medication", world.Sent.Single());
    }

    [Fact]
    public async Task Everything_left_out_sends_no_digest_push_at_all()
    {
        var world = new World();
        await world.Hold("task.completed", "Report finished");
        await world.CatchUp.SetDomainAsync(Owner, "updates", false, default);
        world.PushAllowed = true;

        Assert.False(await world.Digest.FlushIfDueAsync(Owner, default));
        Assert.Empty(world.Sent);
    }

    [Fact]
    public async Task Approvals_and_problems_cannot_be_left_out_and_unknown_kinds_are_refused()
    {
        var world = new World();

        Assert.NotNull((await world.CatchUp.SetDomainAsync(Owner, "approvals", false, default)).Error);
        Assert.NotNull((await world.CatchUp.SetDomainAsync(Owner, "problems", false, default)).Error);
        var unknown = await world.CatchUp.SetDomainAsync(Owner, "nope", false, default);
        Assert.Null(unknown.View);
        Assert.Null(unknown.Error);

        await world.CatchUp.SetDomainAsync(Owner, "updates", false, default);
        var back = await world.CatchUp.SetDomainAsync(Owner, "updates", true, default);
        Assert.True(back.View!.Groups.All(g => g.Included));
    }

    [Fact]
    public async Task Clearing_removes_the_summed_up_items_but_not_what_is_still_held()
    {
        var world = new World();
        await world.Hold("reminder.due", "Take medication");
        world.PushAllowed = true;
        await world.Digest.FlushIfDueAsync(Owner, default);
        world.PushAllowed = false;
        await world.Hold("task.completed", "Report finished");

        await world.CatchUp.ClearAsync(Owner, default);
        var view = await world.CatchUp.GetAsync(Owner, default);

        Assert.Equal(1, view.Total);
        Assert.Equal("updates", view.Groups.Single().Domain);
    }

    [Fact]
    public async Task One_owners_catch_up_never_shows_anothers_items()
    {
        var world = new World();
        await world.Hold("reminder.due", "Mine");
        await world.Hold("reminder.due", "Theirs", Other);

        Assert.Equal("Mine", (await world.CatchUp.GetAsync(Owner, default)).Groups.Single().Items.Single().Title);
        Assert.Equal("Theirs", (await world.CatchUp.GetAsync(Other, default)).Groups.Single().Items.Single().Title);
    }

    [Fact]
    public void A_set_summary_time_waits_for_that_time_and_goes_once_a_day()
    {
        var at = (int h, int m) => new DateTimeOffset(2026, 10, 19, h, m, 0, TimeSpan.Zero);

        Assert.True(DigestSchedule.IsDue(null, at(3, 0), null));
        Assert.False(DigestSchedule.IsDue("08:00", at(7, 59), null));
        Assert.True(DigestSchedule.IsDue("08:00", at(8, 0), null));
        Assert.False(DigestSchedule.IsDue("08:00", at(9, 0), at(8, 1)));
        Assert.True(DigestSchedule.IsDue("08:00", at(9, 0), at(8, 1).AddDays(-1)));
        Assert.True(DigestSchedule.IsDue("garbage", at(1, 0), null));
    }

    [Fact]
    public async Task The_digest_holds_until_the_chosen_time_in_the_owners_zone()
    {
        var world = new World();
        await world.Hold("reminder.due", "Take medication");
        world.PushAllowed = true;
        await new DigestScheduleService(world.Settings).SetAsync(Owner, "09:00", default);

        Assert.False(await world.Digest.FlushIfDueAsync(Owner, default));
        Assert.Empty(world.Sent);

        world.Time.Time = Now.AddHours(2);
        Assert.True(await world.Digest.FlushIfDueAsync(Owner, default));
        Assert.Single(world.Sent);
    }

    [Fact]
    public async Task The_summary_time_is_validated_and_can_be_cleared()
    {
        var service = new DigestScheduleService(new InMemorySettingsStore());

        Assert.Null(await service.SetAsync(Owner, "25:00", default));
        Assert.Null(await service.SetAsync(Owner, "8am", default));
        var saved = await service.SetAsync(Owner, "07:05", default);
        Assert.Equal("07:05", saved!.Time);
        Assert.Contains("once a day, at 07:05", saved.Explanation);
        var cleared = await service.SetAsync(Owner, "", default);
        Assert.Null(cleared!.Time);
        Assert.Contains("as soon as pushes are allowed", cleared.Explanation);
    }
}
