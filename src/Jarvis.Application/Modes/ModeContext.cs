using Jarvis.Application.Settings;

namespace Jarvis.Application.Modes;

/// <summary>One "start focus" session: what the owner chose to work on, how long, and what they said at the end.</summary>
public sealed record FocusSession(string Id, string Intention, DateTimeOffset StartedAt, DateTimeOffset EndsAt,
    DateTimeOffset? EndedAt, string? Reflection);

/// <summary>Stored per owner (<see cref="SettingsSections.Focus"/>): the running or just-finished session and a short history.</summary>
public sealed record FocusState(FocusSession? Current = null, List<FocusSession>? Recent = null);

/// <summary>
/// The small card for the mode the owner is in: why, how long, what is quiet, what still gets through, how to
/// leave and how to correct a wrong guess. Changing mode changes pushes and reply tone only; it grants nothing.
/// </summary>
public sealed record ModeContextCard(
    string Mode,
    string Label,
    string Source,
    string Reason,
    DateTimeOffset? Until,
    int? MinutesLeft,
    string Notifications,
    string Muted,
    IReadOnlyList<string> StillReaches,
    string Effects,
    bool CanCorrect,
    string? ExitLabel,
    FocusSession? Focus,
    bool FocusRunning,
    bool ReflectionDue,
    IReadOnlyList<string> AlwaysAvailable);

public static class ModeContextRules
{
    public const int MinFocusMinutes = 5;
    public const int MaxFocusMinutes = 240;
    public const int DefaultFocusMinutes = 45;
    public const int MaxIntentionLength = 200;
    public const int MaxReflectionLength = 500;
    public const int MaxRecent = 10;

    public const string Effects =
        "A mode only changes which pushes reach your phone and how short Jarvis replies are. It never grants a permission, approves anything or does anything for you.";

    public static readonly IReadOnlyList<string> Essentials =
        ["Approvals stay one tap away.", "You can switch the mode off at any time.", "Reminders and problems still arrive."];

    public static string Muted(string level) => level switch
    {
        NotificationLevels.None => "No pushes reach your phone; everything waits in the app.",
        NotificationLevels.Important => "Only things that cannot wait reach your phone; everything else waits in the app.",
        _ => "Nothing is muted."
    };

    public static IReadOnlyList<string> StillReaches(string level) => level switch
    {
        NotificationLevels.None => [],
        NotificationLevels.Important => ["Reminders", "Approvals", "Triggered watches"],
        _ => ["Everything"]
    };

    public static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= max ? clean : clean[..max].TrimEnd();
    }

    /// <summary>A session only counts as running while the owner's own focus is still on and unexpired.</summary>
    public static bool IsRunning(FocusSession? session, ModeDecision decision, DateTimeOffset now) =>
        session is { EndedAt: null } running && running.EndsAt > now &&
        decision is { Mode: ModeIds.Focus, Source: ModeSources.Manual };

    public static ModeContextCard Describe(ModeState state, FocusState focus, DateTimeOffset now)
    {
        var decision = state.Current;
        var definition = state.Modes.First(x => x.Definition.Id == decision.Mode).Definition;
        var running = IsRunning(focus.Current, decision, now);
        var due = focus.Current is { EndedAt: null } && !running;
        int? left = decision.Until is { } until && until > now ? Math.Max(1, (int)Math.Round((until - now).TotalMinutes)) : null;
        return new ModeContextCard(decision.Mode, definition.Label, decision.Source, decision.Reason, decision.Until,
            left, state.Policy.Notifications, Muted(state.Policy.Notifications),
            StillReaches(state.Policy.Notifications), Effects,
            decision.Source == ModeSources.Auto, decision.Mode == ModeIds.Normal ? null : ExitLabel(decision),
            focus.Current, running, due, Essentials);
    }

    private static string ExitLabel(ModeDecision decision) => decision.Source == ModeSources.Manual
        ? "Switch it off"
        : "Not right now";
}

public interface IModeContextService
{
    Task<ModeContextCard> GetAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<ModeOperation<ModeContextCard>> StartFocusAsync(Guid ownerId, string? intention, int? minutes,
        CancellationToken cancellationToken);

    Task<ModeOperation<ModeContextCard>> EndFocusAsync(Guid ownerId, string? reflection,
        CancellationToken cancellationToken);

    /// <summary>The owner says an automatic mode is wrong: Normal until that mode would have ended.</summary>
    Task<ModeOperation<ModeContextCard>> CorrectAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FocusSession>> RecentAsync(Guid ownerId, CancellationToken cancellationToken);
}

public sealed class ModeContextService(IModeService modes, IOwnerSettingsStore settings, TimeProvider? timeProvider = null)
    : IModeContextService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ModeContextCard> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
        ModeContextRules.Describe(await modes.GetStateAsync(ownerId, cancellationToken),
            await LoadAsync(ownerId, cancellationToken), clock.GetUtcNow());

    public async Task<ModeOperation<ModeContextCard>> StartFocusAsync(Guid ownerId, string? intention, int? minutes,
        CancellationToken cancellationToken)
    {
        var length = minutes ?? ModeContextRules.DefaultFocusMinutes;
        if (length is < ModeContextRules.MinFocusMinutes or > ModeContextRules.MaxFocusMinutes)
            return ModeOperation<ModeContextCard>.Invalid("minutes",
                $"Pick between {ModeContextRules.MinFocusMinutes} and {ModeContextRules.MaxFocusMinutes} minutes.");
        var chosen = ModeContextRules.Clean(intention, ModeContextRules.MaxIntentionLength + 1);
        if (chosen is { Length: > ModeContextRules.MaxIntentionLength })
            return ModeOperation<ModeContextCard>.Invalid("intention",
                $"Keep it under {ModeContextRules.MaxIntentionLength} characters.");

        var now = clock.GetUtcNow();
        var focus = await LoadAsync(ownerId, cancellationToken);
        var before = await modes.GetStateAsync(ownerId, cancellationToken);
        if (ModeContextRules.IsRunning(focus.Current, before.Current, now))
            return ModeOperation<ModeContextCard>.Invalid("focus", "A focus session is already running. End it first.");

        var switched = await modes.SetModeAsync(ownerId, ModeIds.Focus, length, cancellationToken);
        if (!switched.Succeeded)
            return ModeOperation<ModeContextCard>.Invalid(switched.Field ?? "mode", switched.Message ?? "Could not start focus.");

        var recent = Archive(focus, now);
        var session = new FocusSession(Guid.NewGuid().ToString("N")[..12], chosen ?? "", now, now.AddMinutes(length), null, null);
        focus = new FocusState(session, recent);
        await settings.SaveAsync(ownerId, SettingsSections.Focus, focus, cancellationToken);
        return ModeOperation<ModeContextCard>.Ok(ModeContextRules.Describe(switched.Value!, focus, now));
    }

    public async Task<ModeOperation<ModeContextCard>> EndFocusAsync(Guid ownerId, string? reflection,
        CancellationToken cancellationToken)
    {
        var note = ModeContextRules.Clean(reflection, ModeContextRules.MaxReflectionLength + 1);
        if (note is { Length: > ModeContextRules.MaxReflectionLength })
            return ModeOperation<ModeContextCard>.Invalid("reflection",
                $"Keep it under {ModeContextRules.MaxReflectionLength} characters.");
        var now = clock.GetUtcNow();
        var focus = await LoadAsync(ownerId, cancellationToken);
        if (focus.Current is not { EndedAt: null } session)
            return ModeOperation<ModeContextCard>.Invalid("focus", "There is no focus session to end.");

        var state = await modes.GetStateAsync(ownerId, cancellationToken);
        // Only the owner's own focus is switched off; an automatic or newer manual mode is left alone.
        if (state.Current is { Mode: ModeIds.Focus, Source: ModeSources.Manual })
            state = (await modes.SetModeAsync(ownerId, "auto", null, cancellationToken)).Value ?? state;

        var ended = session with { EndedAt = now < session.EndsAt ? now : session.EndsAt, Reflection = note };
        var recent = new List<FocusSession>(focus.Recent ?? []) { ended };
        focus = new FocusState(null, Trim(recent));
        await settings.SaveAsync(ownerId, SettingsSections.Focus, focus, cancellationToken);
        return ModeOperation<ModeContextCard>.Ok(ModeContextRules.Describe(state, focus, now));
    }

    public async Task<ModeOperation<ModeContextCard>> CorrectAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var state = await modes.GetStateAsync(ownerId, cancellationToken);
        if (state.Current.Source != ModeSources.Auto)
            return ModeOperation<ModeContextCard>.Invalid("mode",
                state.Current.Source == ModeSources.Manual
                    ? "You chose this mode yourself. Switch it off instead."
                    : "Nothing was guessed, so there is nothing to correct.");
        var minutes = state.Current.Until is { } until
            ? (int)Math.Clamp(Math.Ceiling((until - now).TotalMinutes), 15, 6 * 60)
            : 60;
        var result = await modes.SetModeAsync(ownerId, ModeIds.Normal, minutes, cancellationToken);
        if (!result.Succeeded)
            return ModeOperation<ModeContextCard>.Invalid(result.Field ?? "mode", result.Message ?? "Could not correct the mode.");
        return ModeOperation<ModeContextCard>.Ok(ModeContextRules.Describe(result.Value!,
            await LoadAsync(ownerId, cancellationToken), now));
    }

    public async Task<IReadOnlyList<FocusSession>> RecentAsync(Guid ownerId, CancellationToken cancellationToken) =>
        ((await LoadAsync(ownerId, cancellationToken)).Recent ?? []).OrderByDescending(x => x.StartedAt).ToArray();

    private async Task<FocusState> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<FocusState>(ownerId, SettingsSections.Focus, cancellationToken) ?? new FocusState();

    /// <summary>An unfinished session that ran out on its own is kept as ended at its planned end, without a reflection.</summary>
    private static List<FocusSession> Archive(FocusState focus, DateTimeOffset now)
    {
        var recent = new List<FocusSession>(focus.Recent ?? []);
        if (focus.Current is { EndedAt: null } old)
            recent.Add(old with { EndedAt = old.EndsAt < now ? old.EndsAt : now });
        return Trim(recent);
    }

    private static List<FocusSession> Trim(List<FocusSession> recent) =>
        recent.OrderByDescending(x => x.StartedAt).Take(ModeContextRules.MaxRecent).ToList();
}
