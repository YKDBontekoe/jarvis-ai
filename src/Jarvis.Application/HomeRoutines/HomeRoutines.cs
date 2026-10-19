using Jarvis.Application.Settings;

namespace Jarvis.Application.HomeRoutines;

public static class DeviceCapabilities
{
    public const string ReadSensor = "sensor.read";
    public const string LightOff = "light.turn_off";
    public const string LightOn = "light.turn_on";
    public const string ClimateEco = "climate.set_eco";
    public const string LockLock = "lock.lock";
}

/// <summary>One thing a home device can do, and whether the owner can take it back.</summary>
public sealed record HomeDevice(string Id, string Name, string Room, string Kind, IReadOnlyList<string> Capabilities,
    bool Present, string? Reading, string Source);

public sealed record RoutineStep(string Id, string Label, string Capability, bool ReadOnly, bool Sensitive, bool Reversible);

public sealed record RoutineTemplate(string Id, string Title, string Description, IReadOnlyList<RoutineStep> Steps);

public static class StepStatus
{
    public const string Ready = "ready";
    public const string Unavailable = "unavailable";
    public const string NeedsApproval = "needs_approval";
    public const string Removed = "removed";
}

public static class StepOutcome
{
    public const string Read = "read";
    public const string Simulated = "simulated";
    public const string Unavailable = "unavailable";
    public const string NotRun = "not_run";
    public const string Skipped = "skipped";
    public const string Reversed = "reversed";
}

public sealed record PlanStep(RoutineStep Step, HomeDevice? Device, string Status, string Reason);

public sealed record RoutinePlan(RoutineTemplate Template, IReadOnlyList<PlanStep> Steps, string Summary,
    bool Simulation, IReadOnlyList<string> Devices);

public sealed record RunStep(string StepId, string Label, string? DeviceName, string Outcome, string Detail, bool Reversible);

public sealed record RoutineRun(string Id, string RoutineId, string Title, DateTimeOffset At, IReadOnlyList<RunStep> Steps,
    string Summary, bool Simulated);

public sealed record RoutineChoice(List<string>? Removed = null, Dictionary<string, string>? Devices = null);

public sealed class HomeRoutineState
{
    public Dictionary<string, RoutineChoice> Choices { get; set; } = [];
    public List<RoutineRun> Runs { get; set; } = [];
}

public sealed record HomeRoutineOverview(IReadOnlyList<RoutineTemplate> Routines, IReadOnlyList<HomeDevice> Devices,
    string Source, string Notice);

/// <summary>
/// Home routines for the owner. The first source of devices is a fixture, so nothing can touch a real home: every
/// run is a simulation that shows exactly which actions would happen, which are unavailable and which would need
/// the owner's approval. Locks and other security controls are never run by a routine.
/// </summary>
public static class HomeRoutineRules
{
    public const int MaxRuns = 10;
    public const string FixtureSource = "fixture";

    public const string Notice =
        "These are example devices. Jarvis is not connected to a real home, so a run only shows what it would do and changes nothing.";

    public static IReadOnlyList<HomeDevice> Fixture() =>
    [
        new("light-living", "Living room light", "Living room", "light", [DeviceCapabilities.LightOff, DeviceCapabilities.LightOn], true, null, FixtureSource),
        new("light-bedroom", "Bedroom lamp", "Bedroom", "light", [DeviceCapabilities.LightOff, DeviceCapabilities.LightOn], false, null, FixtureSource),
        new("thermostat-hall", "Hall thermostat", "Hall", "climate", [DeviceCapabilities.ClimateEco, DeviceCapabilities.ReadSensor], true, "19.5 °C", FixtureSource),
        new("lock-front", "Front door lock", "Hall", "lock", [DeviceCapabilities.LockLock], true, null, FixtureSource),
        new("sensor-door", "Back door sensor", "Kitchen", "sensor", [DeviceCapabilities.ReadSensor], true, "closed", FixtureSource)
    ];

    public static IReadOnlyList<RoutineTemplate> Templates { get; } =
    [
        new("morning", "Morning overview", "A read-only look at your home before the day starts.",
        [
            new("m-temp", "Read the hall thermostat", DeviceCapabilities.ReadSensor, true, false, false),
            new("m-door", "Check the back door sensor", DeviceCapabilities.ReadSensor, true, false, false)
        ]),
        new("focus", "Start focus", "Dim the room and save energy while you work.",
        [
            new("f-light", "Turn off the bedroom lamp", DeviceCapabilities.LightOff, false, false, true),
            new("f-living", "Turn off the living room light", DeviceCapabilities.LightOff, false, false, true),
            new("f-eco", "Set the hall thermostat to eco", DeviceCapabilities.ClimateEco, false, false, true)
        ]),
        new("leave", "Leave home", "Switch things off and secure the door.",
        [
            new("l-living", "Turn off the living room light", DeviceCapabilities.LightOff, false, false, true),
            new("l-eco", "Set the hall thermostat to eco", DeviceCapabilities.ClimateEco, false, false, true),
            new("l-lock", "Lock the front door", DeviceCapabilities.LockLock, false, true, false)
        ]),
        new("evening", "Evening wind-down", "Soften the lights for the evening.",
        [
            new("e-living", "Turn off the living room light", DeviceCapabilities.LightOff, false, false, true),
            new("e-bedroom", "Turn on the bedroom lamp", DeviceCapabilities.LightOn, false, false, true)
        ])
    ];

    public static RoutineTemplate? Find(string id) => Templates.FirstOrDefault(t => t.Id == id);

    /// <summary>Which device a step uses: the owner's choice if it can do the job, else the first present one that can.</summary>
    public static HomeDevice? Pick(RoutineStep step, IReadOnlyList<HomeDevice> devices, RoutineChoice? choice)
    {
        var capable = devices.Where(d => d.Capabilities.Contains(step.Capability)).ToArray();
        if (choice?.Devices is { } chosen && chosen.TryGetValue(step.Id, out var id) &&
            capable.FirstOrDefault(d => d.Id == id) is { } picked)
            return picked;
        // A step that names a device uses that device even when it is away; it never quietly falls back to another one.
        return capable.FirstOrDefault(d => step.Label.Contains(d.Name, StringComparison.OrdinalIgnoreCase)) ??
               capable.FirstOrDefault(d => d.Present) ?? capable.FirstOrDefault();
    }

    public static RoutinePlan Plan(RoutineTemplate template, IReadOnlyList<HomeDevice> devices, RoutineChoice? choice)
    {
        var removed = choice?.Removed ?? [];
        var steps = template.Steps.Select(step =>
        {
            if (removed.Contains(step.Id)) return new PlanStep(step, null, StepStatus.Removed, "You took this out.");
            var device = Pick(step, devices, choice);
            if (device is null)
                return new PlanStep(step, null, StepStatus.Unavailable, "No connected device can do this.");
            if (!device.Present)
                return new PlanStep(step, device, StepStatus.Unavailable, $"{device.Name} is not reachable.");
            if (step.Sensitive)
                return new PlanStep(step, device, StepStatus.NeedsApproval,
                    "Security controls are never run by a routine. You would approve this one yourself.");
            return new PlanStep(step, device, StepStatus.Ready, step.ReadOnly ? "Read only." : "Would run.");
        }).ToArray();
        return new RoutinePlan(template, steps, Summarise(steps), true,
            steps.Where(s => s.Device is not null).Select(s => s.Device!.Name).Distinct().ToArray());
    }

    public static string Summarise(IReadOnlyList<PlanStep> steps)
    {
        var active = steps.Where(s => s.Status != StepStatus.Removed).ToArray();
        if (active.Length == 0) return "Every action is taken out. Nothing would happen.";
        var ready = active.Count(s => s.Status == StepStatus.Ready);
        var parts = new List<string> { $"{ready} of {active.Length} would run" };
        if (active.Count(s => s.Status == StepStatus.Unavailable) is > 0 and var unavailable)
            parts.Add($"{unavailable} unavailable");
        if (active.Count(s => s.Status == StepStatus.NeedsApproval) is > 0 and var approval)
            parts.Add($"{approval} needs your approval");
        return string.Join(", ", parts) + ".";
    }

    public static RoutineRun Simulate(RoutinePlan plan, string id, DateTimeOffset at)
    {
        var steps = plan.Steps.Select(s =>
        {
            var name = s.Device?.Name;
            return s.Status switch
            {
                StepStatus.Removed => new RunStep(s.Step.Id, s.Step.Label, name, StepOutcome.Skipped, "Taken out by you.", false),
                StepStatus.Unavailable => new RunStep(s.Step.Id, s.Step.Label, name, StepOutcome.Unavailable, s.Reason, false),
                StepStatus.NeedsApproval => new RunStep(s.Step.Id, s.Step.Label, name, StepOutcome.NotRun, s.Reason, false),
                _ when s.Step.ReadOnly => new RunStep(s.Step.Id, s.Step.Label, name, StepOutcome.Read,
                    s.Device?.Reading is { } reading ? $"{s.Device.Name}: {reading}" : "No reading.", false),
                _ => new RunStep(s.Step.Id, s.Step.Label, name, StepOutcome.Simulated,
                    "Simulated. Nothing changed.", s.Step.Reversible)
            };
        }).ToArray();
        return new RoutineRun(id, plan.Template.Id, plan.Template.Title, at, steps, Summarise(plan.Steps) + " Nothing changed in your home.", true);
    }
}

public sealed record HomeRoutineOperation<T>(T? Value, string? Field = null, string? Message = null, bool NotFound = false)
{
    public bool Succeeded => Field is null && !NotFound;
    public static HomeRoutineOperation<T> Ok(T value) => new(value);
    public static HomeRoutineOperation<T> Invalid(string field, string message) => new(default, field, message);
    public static HomeRoutineOperation<T> Missing() => new(default, null, null, true);
}

public interface IHomeRoutineService
{
    Task<HomeRoutineOverview> OverviewAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<HomeRoutineOperation<RoutinePlan>> PlanAsync(Guid ownerId, string routineId, CancellationToken cancellationToken);
    Task<HomeRoutineOperation<RoutinePlan>> ChooseAsync(Guid ownerId, string routineId, IReadOnlyList<string>? removed,
        IReadOnlyDictionary<string, string>? devices, CancellationToken cancellationToken);
    Task<HomeRoutineOperation<RoutineRun>> RunAsync(Guid ownerId, string routineId, CancellationToken cancellationToken);
    Task<HomeRoutineOperation<RoutineRun>> ReverseAsync(Guid ownerId, string runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RoutineRun>> RunsAsync(Guid ownerId, CancellationToken cancellationToken);
}

public sealed class HomeRoutineService(IOwnerSettingsStore settings, TimeProvider? timeProvider = null) : IHomeRoutineService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<HomeRoutineOverview> OverviewAsync(Guid ownerId, CancellationToken cancellationToken) =>
        new(HomeRoutineRules.Templates, HomeRoutineRules.Fixture(), HomeRoutineRules.FixtureSource, HomeRoutineRules.Notice);

    public async Task<HomeRoutineOperation<RoutinePlan>> PlanAsync(Guid ownerId, string routineId,
        CancellationToken cancellationToken)
    {
        if (HomeRoutineRules.Find(routineId) is not { } template) return HomeRoutineOperation<RoutinePlan>.Missing();
        var state = await LoadAsync(ownerId, cancellationToken);
        return HomeRoutineOperation<RoutinePlan>.Ok(
            HomeRoutineRules.Plan(template, HomeRoutineRules.Fixture(), state.Choices.GetValueOrDefault(routineId)));
    }

    public async Task<HomeRoutineOperation<RoutinePlan>> ChooseAsync(Guid ownerId, string routineId,
        IReadOnlyList<string>? removed, IReadOnlyDictionary<string, string>? devices, CancellationToken cancellationToken)
    {
        if (HomeRoutineRules.Find(routineId) is not { } template) return HomeRoutineOperation<RoutinePlan>.Missing();
        var known = Fixture.ToDictionary(d => d.Id);
        foreach (var id in removed ?? [])
            if (template.Steps.All(s => s.Id != id))
                return HomeRoutineOperation<RoutinePlan>.Invalid("removed", $"Unknown action {id}.");
        foreach (var (stepId, deviceId) in devices ?? new Dictionary<string, string>())
        {
            var step = template.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step is null) return HomeRoutineOperation<RoutinePlan>.Invalid("devices", $"Unknown action {stepId}.");
            if (!known.TryGetValue(deviceId, out var device) || !device.Capabilities.Contains(step.Capability))
                return HomeRoutineOperation<RoutinePlan>.Invalid("devices", $"That device cannot do \"{step.Label}\".");
        }
        var state = await LoadAsync(ownerId, cancellationToken);
        state.Choices[routineId] = new RoutineChoice(removed?.Distinct().ToList() ?? [],
            devices is null ? [] : new Dictionary<string, string>(devices));
        await settings.SaveAsync(ownerId, SettingsSections.HomeRoutines, state, cancellationToken);
        return HomeRoutineOperation<RoutinePlan>.Ok(HomeRoutineRules.Plan(template, Fixture, state.Choices[routineId]));
    }

    public async Task<HomeRoutineOperation<RoutineRun>> RunAsync(Guid ownerId, string routineId,
        CancellationToken cancellationToken)
    {
        var planned = await PlanAsync(ownerId, routineId, cancellationToken);
        if (!planned.Succeeded) return HomeRoutineOperation<RoutineRun>.Missing();
        var state = await LoadAsync(ownerId, cancellationToken);
        var run = HomeRoutineRules.Simulate(planned.Value!, Guid.NewGuid().ToString("N")[..12], clock.GetUtcNow());
        state.Runs = state.Runs.Prepend(run).Take(HomeRoutineRules.MaxRuns).ToList();
        await settings.SaveAsync(ownerId, SettingsSections.HomeRoutines, state, cancellationToken);
        return HomeRoutineOperation<RoutineRun>.Ok(run);
    }

    public async Task<HomeRoutineOperation<RoutineRun>> ReverseAsync(Guid ownerId, string runId,
        CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        var index = state.Runs.FindIndex(r => r.Id == runId);
        if (index < 0) return HomeRoutineOperation<RoutineRun>.Missing();
        var run = state.Runs[index];
        if (!run.Steps.Any(s => s is { Outcome: StepOutcome.Simulated, Reversible: true }))
            return HomeRoutineOperation<RoutineRun>.Invalid("run", "Nothing in this run can be undone.");
        var steps = run.Steps.Select(s => s is { Outcome: StepOutcome.Simulated, Reversible: true }
            ? s with { Outcome = StepOutcome.Reversed, Detail = "Undone in the simulation.", Reversible = false }
            : s).ToArray();
        var reversed = run with { Steps = steps, Summary = run.Summary + " The reversible actions were undone." };
        state.Runs[index] = reversed;
        await settings.SaveAsync(ownerId, SettingsSections.HomeRoutines, state, cancellationToken);
        return HomeRoutineOperation<RoutineRun>.Ok(reversed);
    }

    public async Task<IReadOnlyList<RoutineRun>> RunsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await LoadAsync(ownerId, cancellationToken)).Runs;

    private static IReadOnlyList<HomeDevice> Fixture => HomeRoutineRules.Fixture();

    private async Task<HomeRoutineState> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<HomeRoutineState>(ownerId, SettingsSections.HomeRoutines, cancellationToken) ?? new HomeRoutineState();
}
