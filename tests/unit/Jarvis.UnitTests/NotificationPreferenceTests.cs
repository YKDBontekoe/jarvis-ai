using Jarvis.Application.Modes;
using Jarvis.Application.Notifications;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class NotificationPreferenceTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fe01");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fe02");

    private static ModeState State(string mode)
    {
        var effective = ModeCatalog.Defaults.Select(d => new EffectiveMode(d, false)).ToArray();
        var definition = ModeCatalog.Find(mode);
        return new ModeState(new ModeDecision(mode, ModeSources.Manual, "test", null), definition.Policy, effective,
            true, "23:00", "07:00");
    }

    private sealed class World(string mode = ModeIds.Normal)
    {
        public readonly InMemorySettingsStore Settings = new();
        public bool ModesThrow;

        public NotificationPreferenceService Service => new(Settings,
            Fake<IModeService>.Create(("GetStateAsync", _ =>
                ModesThrow ? throw new InvalidOperationException("broken") : State(mode))));
    }

    [Theory]
    [InlineData("approval.required", "approvals")]
    [InlineData("automation.approval", "approvals")]
    [InlineData("task.approval_required", "approvals")]
    [InlineData("reminder.due", "reminders")]
    [InlineData("whatsapp.reminder", "reminders")]
    [InlineData("reminder.failed", "problems")]
    [InlineData("mission.failed", "problems")]
    [InlineData("briefing.daily", "briefings")]
    [InlineData("task.completed", "updates")]
    [InlineData("learning.dreamed", "updates")]
    [InlineData("something.new", "updates")]
    public void Types_fall_into_a_kind_the_owner_can_reason_about(string type, string category) =>
        Assert.Equal(category, NotificationCategories.For(type));

    [Fact]
    public async Task By_default_everything_reaches_the_phone_in_the_normal_mode()
    {
        var view = await new World().Service.GetAsync(Owner, default);

        Assert.All(view.Categories, c => Assert.Equal(PushOutcomes.Push, c.Now.Outcome));
        Assert.Equal("Everything reaches your phone right away.", view.Summary);
        Assert.Equal(["approvals", "reminders", "problems", "briefings", "updates"], view.Categories.Select(c => c.Id));
    }

    [Fact]
    public async Task A_quiet_kind_stays_in_the_app_and_says_so()
    {
        var world = new World();
        var result = await world.Service.SetAsync(Owner, "updates", "quiet", default);

        Assert.True(result.Succeeded);
        var updates = result.View!.Categories.Single(c => c.Id == "updates");
        Assert.Equal(PushOutcomes.AppOnly, updates.Now.Outcome);
        Assert.Contains("stay in the app", updates.Now.Reason);
        Assert.Equal("1 kind of notification stays in the app only, by your choice.", result.View.Summary);
        Assert.Equal(PushOutcomes.AppOnly, (await world.Service.DecideAsync(Owner, "task.completed", default)).Outcome);
        Assert.Equal(PushOutcomes.Push, (await world.Service.DecideAsync(Owner, "reminder.due", default)).Outcome);
    }

    [Fact]
    public async Task Approvals_and_failures_cannot_be_quieted()
    {
        var service = new World().Service;

        var approvals = await service.SetAsync(Owner, "approvals", "quiet", default);
        var problems = await service.SetAsync(Owner, "problems", "quiet", default);

        Assert.False(approvals.Succeeded);
        Assert.Equal("push", approvals.Field);
        Assert.Contains("always buzzes", approvals.Message);
        Assert.False(problems.Succeeded);
        Assert.Equal(PushOutcomes.Push, (await service.DecideAsync(Owner, "approval.required", default)).Outcome);
    }

    [Fact]
    public async Task Unknown_kinds_and_values_are_refused()
    {
        var service = new World().Service;

        Assert.Equal("category", (await service.SetAsync(Owner, "gossip", "on", default)).Field);
        Assert.Equal("push", (await service.SetAsync(Owner, "updates", "digest", default)).Field);
        Assert.Equal("push", (await service.SetAsync(Owner, "updates", null, default)).Field);
    }

    [Fact]
    public async Task A_mode_holds_what_it_keeps_off_the_phone_for_the_digest_and_the_table_shows_every_mode()
    {
        var sleep = await new World(ModeIds.Sleep).Service.GetAsync(Owner, default);
        var focus = await new World(ModeIds.Focus).Service.GetAsync(Owner, default);

        Assert.All(sleep.Categories, c => Assert.Equal(PushOutcomes.Held, c.Now.Outcome));
        Assert.Contains("Sleep keeps this off your phone", sleep.Categories[0].Now.Reason);
        Assert.Equal("Sleep is on: 5 kinds of notification wait for you in the app.", sleep.Summary);
        Assert.Equal(PushOutcomes.Push, focus.Categories.Single(c => c.Id == "reminders").Now.Outcome);
        Assert.Equal(PushOutcomes.Held, focus.Categories.Single(c => c.Id == "updates").Now.Outcome);
        var approvals = focus.Categories[0];
        Assert.Equal(ModeCatalog.Defaults.Count, approvals.Modes.Count);
        Assert.Equal(PushOutcomes.Held, approvals.Modes.Single(m => m.Mode == ModeIds.Meeting).Outcome);
        Assert.Equal(PushOutcomes.Push, approvals.Modes.Single(m => m.Mode == ModeIds.Focus).Outcome);
    }

    [Fact]
    public async Task The_owner_choice_wins_over_the_mode_and_choices_are_per_owner()
    {
        var world = new World(ModeIds.Sleep);
        await world.Service.SetAsync(Owner, "updates", "quiet", default);

        Assert.Equal(PushOutcomes.AppOnly, (await world.Service.DecideAsync(Owner, "task.completed", default)).Outcome);
        Assert.Equal(PushOutcomes.Held, (await world.Service.DecideAsync(Other, "task.completed", default)).Outcome);
    }

    [Fact]
    public async Task A_broken_settings_row_never_swallows_a_notification()
    {
        var world = new World { ModesThrow = true };

        var decision = await world.Service.DecideAsync(Owner, "reminder.due", default);

        Assert.Equal(PushOutcomes.Push, decision.Outcome);
    }

    [Fact]
    public async Task The_agent_tool_explains_each_kind_and_that_the_app_keeps_everything()
    {
        var world = new World(ModeIds.Meeting);
        var tools = new Jarvis.Agents.Notifications.NotificationAgentTools(world.Service, new FixedUser());

        var text = await tools.ExplainNotificationsAsync();

        Assert.Contains("Meeting is on", text);
        Assert.Contains("Reminders:", text);
        Assert.Contains("stays in the app whether or not it buzzed", text);
    }

    private sealed class FixedUser : Jarvis.Application.Conversations.ICurrentUser
    {
        public Guid OwnerId => Owner;
    }
}
