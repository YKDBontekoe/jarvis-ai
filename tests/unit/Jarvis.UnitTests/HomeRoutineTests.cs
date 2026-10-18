using Jarvis.Application.HomeRoutines;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class HomeRoutineTests
{
    private static readonly Guid Owner = Guid.Parse("01996b8c-6000-7000-8000-00000000fa11");
    private static readonly Guid Other = Guid.Parse("01996b8c-6000-7000-8000-00000000fa12");
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static HomeRoutineService Service(InMemorySettingsStore? store = null) => new(store ?? new InMemorySettingsStore(), new Clock());

    [Fact]
    public async Task The_overview_says_the_devices_are_examples()
    {
        var overview = await Service().OverviewAsync(Owner, default);

        Assert.Equal(["morning", "focus", "leave", "evening"], overview.Routines.Select(r => r.Id));
        Assert.All(overview.Devices, d => Assert.Equal("fixture", d.Source));
        Assert.Contains("not connected to a real home", overview.Notice);
    }

    [Fact]
    public async Task A_step_naming_an_absent_device_is_unavailable_and_never_moves_to_another_device()
    {
        var plan = (await Service().PlanAsync(Owner, "focus", default)).Value!;

        var lamp = plan.Steps.Single(s => s.Step.Id == "f-light");
        Assert.Equal("unavailable", lamp.Status);
        Assert.Equal("Bedroom lamp", lamp.Device!.Name);
        Assert.Equal("ready", plan.Steps.Single(s => s.Step.Id == "f-living").Status);
        Assert.Equal("2 of 3 would run, 1 unavailable.", plan.Summary);
    }

    [Fact]
    public async Task Locks_need_approval_and_are_never_run_by_a_routine()
    {
        var service = Service();
        var plan = (await service.PlanAsync(Owner, "leave", default)).Value!;
        Assert.Equal("needs_approval", plan.Steps.Single(s => s.Step.Id == "l-lock").Status);

        var run = (await service.RunAsync(Owner, "leave", default)).Value!;
        var lockStep = run.Steps.Single(s => s.StepId == "l-lock");
        Assert.Equal("not_run", lockStep.Outcome);
        Assert.False(lockStep.Reversible);
        Assert.Contains("1 needs your approval", run.Summary);
        Assert.Contains("Nothing changed in your home", run.Summary);
    }

    [Fact]
    public async Task Removing_one_action_leaves_the_rest_and_is_remembered()
    {
        var service = Service();
        var plan = (await service.ChooseAsync(Owner, "leave", ["l-eco"], null, default)).Value!;

        Assert.Equal("removed", plan.Steps.Single(s => s.Step.Id == "l-eco").Status);
        var run = (await service.RunAsync(Owner, "leave", default)).Value!;
        Assert.Equal("skipped", run.Steps.Single(s => s.StepId == "l-eco").Outcome);
        Assert.Equal("simulated", run.Steps.Single(s => s.StepId == "l-living").Outcome);
        Assert.Equal("removed", (await service.PlanAsync(Owner, "leave", default)).Value!.Steps.Single(s => s.Step.Id == "l-eco").Status);
    }

    [Fact]
    public async Task Reading_steps_report_the_value_and_never_claim_a_change()
    {
        var run = (await Service().RunAsync(Owner, "morning", default)).Value!;

        Assert.All(run.Steps, s => Assert.Equal("read", s.Outcome));
        Assert.Contains("19.5 °C", run.Steps.Single(s => s.StepId == "m-temp").Detail);
        Assert.Contains("closed", run.Steps.Single(s => s.StepId == "m-door").Detail);
    }

    [Fact]
    public async Task Only_simulated_reversible_steps_can_be_undone_once()
    {
        var service = Service();
        var run = (await service.RunAsync(Owner, "evening", default)).Value!;

        var undone = await service.ReverseAsync(Owner, run.Id, default);

        Assert.True(undone.Succeeded);
        Assert.Contains(undone.Value!.Steps, s => s.Outcome == "reversed");
        Assert.Equal("run", (await service.ReverseAsync(Owner, run.Id, default)).Field);
        var none = (await service.RunAsync(Owner, "morning", default)).Value!;
        Assert.Equal("run", (await service.ReverseAsync(Owner, none.Id, default)).Field);
    }

    [Fact]
    public async Task Choices_are_validated_against_what_a_device_can_do()
    {
        var service = Service();

        Assert.Equal("removed", (await service.ChooseAsync(Owner, "focus", ["nope"], null, default)).Field);
        Assert.Equal("devices", (await service.ChooseAsync(Owner, "focus", null,
            new Dictionary<string, string> { ["f-eco"] = "lock-front" }, default)).Field);
        Assert.Equal("devices", (await service.ChooseAsync(Owner, "focus", null,
            new Dictionary<string, string> { ["f-eco"] = "ghost" }, default)).Field);
        Assert.True((await service.PlanAsync(Owner, "nothing", default)).NotFound);
        Assert.True((await service.RunAsync(Owner, "nothing", default)).NotFound);
    }

    [Fact]
    public async Task Choosing_another_able_device_changes_who_does_the_action()
    {
        var service = Service();
        var plan = (await service.ChooseAsync(Owner, "focus", null,
            new Dictionary<string, string> { ["f-light"] = "light-living" }, default)).Value!;

        var step = plan.Steps.Single(s => s.Step.Id == "f-light");
        Assert.Equal("Living room light", step.Device!.Name);
        Assert.Equal("ready", step.Status);
    }

    [Fact]
    public async Task Runs_are_bounded_and_private_to_the_owner()
    {
        var store = new InMemorySettingsStore();
        var service = Service(store);
        for (var i = 0; i < 14; i++) await service.RunAsync(Owner, "evening", default);

        Assert.Equal(HomeRoutineRules.MaxRuns, (await service.RunsAsync(Owner, default)).Count);
        Assert.Empty(await Service(store).RunsAsync(Other, default));
    }
}
