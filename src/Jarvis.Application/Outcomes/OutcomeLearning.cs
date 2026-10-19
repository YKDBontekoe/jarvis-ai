using Jarvis.Application.Profiles;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Outcomes;

/// <summary>What the owner said about something Jarvis made or suggested. Saying nothing is never stored as acceptance.</summary>
public static class OutcomeSignals
{
    public const string Accepted = "accepted";
    public const string Revised = "revised";
    public const string Dismissed = "dismissed";
    public const string NotUseful = "not_useful";
    public const string WrongAssumption = "wrong_assumption";
    public const string TooMuchEffort = "too_much_effort";
    public const string Successful = "successful";

    public static readonly IReadOnlyList<string> All =
        [Accepted, Revised, Dismissed, NotUseful, WrongAssumption, TooMuchEffort, Successful];
}

public static class OutcomeAspects
{
    public const string TooLong = "too_long";
    public const string TooShort = "too_short";

    public static readonly IReadOnlyList<string> All = [TooLong, TooShort];
}

public static class OutcomeArtifacts
{
    public static readonly IReadOnlyList<string> All =
        ["project_update", "story", "comparison", "mission_result", "trip", "scenario", "opportunity", "suggestion"];

    /// <summary>Kinds Jarvis volunteers; repeated dismissals of these can quiet them.</summary>
    public static readonly IReadOnlyList<string> Volunteered = ["opportunity", "suggestion"];
}

public sealed record OutcomeFeedback(string Id, string ArtifactKind, string? ArtifactId, string Signal, string? Aspect,
    string? Note, Guid? ProfileId, DateTimeOffset At);

public static class ChangeKinds
{
    public const string Style = "style";
    public const string Quiet = "quiet";
}

public static class ChangeStatuses
{
    public const string Pending = "pending";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
    public const string Undone = "undone";
}

/// <summary>
/// A small, explainable change to how Jarvis behaves. It only ever sets a style preference or quiets a kind of
/// suggestion; it carries no tool, permission or profile-scope field, so it cannot widen anything.
/// </summary>
public sealed record BehaviorChange(string Id, string Fingerprint, string Kind, string ArtifactKind, Guid? ProfileId,
    string Value, string Title, string Reason, string Before, string After, bool Synthetic, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt);

public sealed class OutcomeState
{
    public List<OutcomeFeedback> Feedback { get; set; } = [];
    public List<BehaviorChange> Changes { get; set; } = [];
}

public sealed record ChangeEffect(int Before, int After, int DaysSince, string Statement);

public sealed record ChangeView(BehaviorChange Change, ChangeEffect? Effect);

/// <summary>What the owner said about one kind of suggestion in the last 30 days. Only answers are counted: a suggestion
/// nobody rated is unknown, never a yes.</summary>
public sealed record SuggestionStat(string Kind, int Used, int NotUsed, int Rated, string Statement);

public sealed record LearnedCard(IReadOnlyList<ChangeView> Applied, IReadOnlyList<ChangeView> Waiting, string Summary,
    IReadOnlyList<SuggestionStat> Suggestions);

public static class OutcomeRules
{
    public const int MaxFeedback = 300;
    public const int WindowDays = 30;
    public const int QuietWindowDays = 14;
    public const int Threshold = 3;
    public const int MaxNote = 200;
    public const int MinEffectDays = 7;

    public static string Fingerprint(string kind, string artifactKind, Guid? profile, string value) =>
        $"{kind}:{artifactKind}:{profile?.ToString("N") ?? "-"}:{value}";

    public static string Label(string artifactKind) => artifactKind.Replace('_', ' ');

    /// <summary>
    /// Changes the feedback supports. A fingerprint the owner rejected or undid is never offered again, a change that is
    /// already applied or waiting is not duplicated, and feedback from different profiles is never pooled.
    /// </summary>
    public static IReadOnlyList<BehaviorChange> Propose(IReadOnlyList<OutcomeFeedback> feedback,
        IReadOnlyList<BehaviorChange> existing, DateTimeOffset now)
    {
        var closed = existing.Where(c => c.Status is ChangeStatuses.Rejected or ChangeStatuses.Undone)
            .Select(c => c.Fingerprint).ToHashSet();
        var open = existing.Where(c => c.Status is ChangeStatuses.Pending or ChangeStatuses.Applied)
            .Select(c => c.Fingerprint).ToHashSet();
        var found = new List<BehaviorChange>();

        foreach (var group in feedback.GroupBy(f => (f.ArtifactKind, f.ProfileId)))
        {
            var (kind, profile) = group.Key;
            foreach (var (aspect, value) in new[] { (OutcomeAspects.TooLong, "short"), (OutcomeAspects.TooShort, "detailed") })
            {
                var hits = group.Count(f => f.Aspect == aspect && f.At >= now.AddDays(-WindowDays));
                var fingerprint = Fingerprint(ChangeKinds.Style, kind, profile, value);
                if (hits < Threshold || closed.Contains(fingerprint) || open.Contains(fingerprint)) continue;
                var (before, after) = Example(kind, value);
                found.Add(new BehaviorChange(Guid.NewGuid().ToString("N")[..12], fingerprint, ChangeKinds.Style, kind, profile,
                    value, value == "short" ? $"Keep {Label(kind)} short" : $"Give {Label(kind)} more detail",
                    $"You said {Label(kind)} was {(value == "short" ? "too long" : "too short")} {hits} times in the last {WindowDays} days.",
                    before, after, true, ChangeStatuses.Pending, now, null));
            }

            if (OutcomeArtifacts.Volunteered.Contains(kind))
            {
                var dismissed = group.Count(f => f.Signal == OutcomeSignals.Dismissed && f.At >= now.AddDays(-QuietWindowDays));
                var fingerprint = Fingerprint(ChangeKinds.Quiet, kind, profile, "quiet");
                if (dismissed >= Threshold && !closed.Contains(fingerprint) && !open.Contains(fingerprint))
                    found.Add(new BehaviorChange(Guid.NewGuid().ToString("N")[..12], fingerprint, ChangeKinds.Quiet, kind, profile,
                        "quiet", $"Stop volunteering {Label(kind)} suggestions",
                        $"You dismissed {Label(kind)} suggestions {dismissed} times in the last {QuietWindowDays} days.",
                        "Jarvis mentions a new " + Label(kind) + " when it notices one.",
                        "Jarvis mentions a " + Label(kind) + " only when you ask.", true, ChangeStatuses.Pending, now, null));
            }
        }

        return found;
    }

    /// <summary>A made-up example, labelled as one, of how the change reads.</summary>
    public static (string Before, string After) Example(string artifactKind, string value) => (artifactKind, value) switch
    {
        ("project_update", "short") => ("The migration is going well overall. Most of the services have been moved across and tested, and a few smaller items are still outstanding, including the reporting jobs and the old cron entries.",
            "Migration: 80% done. Left: reporting jobs, old cron entries."),
        (_, "short") => ("A long answer that walks through every step and repeats the context before it reaches the point.",
            "The short answer, then one line of context."),
        (_, _) => ("Done. Three items.", "Done. Three items: the first is X because Y; the second is Z; the third needs your decision by Friday.")
    };

    public static ChangeEffect? Effect(BehaviorChange change, IReadOnlyList<OutcomeFeedback> feedback, DateTimeOffset now)
    {
        if (change.Status != ChangeStatuses.Applied || change.DecidedAt is not { } appliedAt ||
            change.Kind != ChangeKinds.Style) return null;
        var aspect = change.Value == "short" ? OutcomeAspects.TooLong : OutcomeAspects.TooShort;
        var matching = feedback.Where(f => f.ArtifactKind == change.ArtifactKind && f.ProfileId == change.ProfileId && f.Aspect == aspect).ToArray();
        var before = matching.Count(f => f.At < appliedAt && f.At >= appliedAt.AddDays(-WindowDays));
        var after = matching.Count(f => f.At >= appliedAt);
        var days = (int)(now - appliedAt).TotalDays;
        var statement = days < MinEffectDays || before < Threshold
            ? "Too early to say whether this helped."
            : $"{before} complaints before, {after} since. That is a small sample and does not show the change caused it.";
        return new ChangeEffect(before, after, days, statement);
    }

    public const int MinRated = 3;

    public static readonly IReadOnlySet<string> UsedSignals = new HashSet<string> { OutcomeSignals.Accepted, OutcomeSignals.Successful };

    public static readonly IReadOnlySet<string> NotUsedSignals = new HashSet<string>
        { OutcomeSignals.Dismissed, OutcomeSignals.NotUseful, OutcomeSignals.WrongAssumption, OutcomeSignals.TooMuchEffort };

    /// <summary>Per volunteered kind: how many suggestions the owner used and how many not. Revisions are neither.</summary>
    public static IReadOnlyList<SuggestionStat> SuggestionStats(IReadOnlyList<OutcomeFeedback> feedback, DateTimeOffset now) =>
        OutcomeArtifacts.Volunteered.Select(kind =>
        {
            var mine = feedback.Where(f => f.ArtifactKind == kind && f.At >= now.AddDays(-WindowDays)).ToArray();
            var used = mine.Count(f => UsedSignals.Contains(f.Signal));
            var notUsed = mine.Count(f => NotUsedSignals.Contains(f.Signal));
            var rated = used + notUsed;
            var statement = rated == 0
                ? "No answers yet, so Jarvis does not know."
                : rated < MinRated
                    ? $"You answered {rated} so far. That is too few to say anything."
                    : $"You used {used} of the {rated} you answered. Suggestions you did not answer are not counted.";
            return new SuggestionStat(kind, used, notUsed, rated, statement);
        }).Where(s => s.Rated > 0).ToArray();

    public static string? StyleNote(OutcomeState state, Guid? profileId)
    {
        var lines = state.Changes
            .Where(c => c.Status == ChangeStatuses.Applied && (c.ProfileId is null || c.ProfileId == profileId))
            .Select(c => c.Kind == ChangeKinds.Quiet
                ? $"Do not volunteer {Label(c.ArtifactKind)} suggestions unless the owner asks."
                : $"For {Label(c.ArtifactKind)}: {(c.Value == "short" ? "keep it short" : "give more detail")}.")
            .ToArray();
        return lines.Length == 0 ? null : "Owner style preferences (approved by the owner): " + string.Join(" ", lines);
    }
}

public sealed record OutcomeOperation<T>(T? Value, string? Field = null, string? Message = null, bool NotFound = false)
{
    public bool Succeeded => Field is null && !NotFound;
    public static OutcomeOperation<T> Ok(T value) => new(value);
    public static OutcomeOperation<T> Invalid(string field, string message) => new(default, field, message);
    public static OutcomeOperation<T> Missing() => new(default, null, null, true);
}

public sealed record FeedbackResult(bool Recorded, string Message);

public interface IOutcomeLearningService
{
    Task<OutcomeOperation<FeedbackResult>> RecordAsync(Guid ownerId, string? artifactKind, string? artifactId, string? signal,
        string? aspect, string? note, Guid? profileId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChangeView>> ChangesAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<OutcomeOperation<ChangeView>> ApplyAsync(Guid ownerId, string id, CancellationToken cancellationToken);
    Task<OutcomeOperation<ChangeView>> RejectAsync(Guid ownerId, string id, CancellationToken cancellationToken);
    Task<OutcomeOperation<ChangeView>> UndoAsync(Guid ownerId, string id, CancellationToken cancellationToken);
    Task<LearnedCard> LearnedAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<string?> StyleNoteAsync(Guid ownerId, Guid? profileId, CancellationToken cancellationToken);
}

public sealed class OutcomeLearningService(IOwnerSettingsStore settings, IAssistantProfileService profiles,
    TimeProvider? timeProvider = null) : IOutcomeLearningService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<OutcomeOperation<FeedbackResult>> RecordAsync(Guid ownerId, string? artifactKind, string? artifactId,
        string? signal, string? aspect, string? note, Guid? profileId, CancellationToken cancellationToken)
    {
        if (artifactKind is null || !OutcomeArtifacts.All.Contains(artifactKind))
            return OutcomeOperation<FeedbackResult>.Invalid("artifactKind", "Use one of: " + string.Join(", ", OutcomeArtifacts.All) + ".");
        if (signal is null || !OutcomeSignals.All.Contains(signal))
            return OutcomeOperation<FeedbackResult>.Invalid("signal", "Use one of: " + string.Join(", ", OutcomeSignals.All) + ".");
        if (aspect is not null && !OutcomeAspects.All.Contains(aspect))
            return OutcomeOperation<FeedbackResult>.Invalid("aspect", "Use too_long or too_short.");
        var cleanNote = CleanNote(note);
        if (cleanNote is { Length: > OutcomeRules.MaxNote })
            return OutcomeOperation<FeedbackResult>.Invalid("note", $"Keep the note under {OutcomeRules.MaxNote} characters.");

        if (profileId is { } id)
        {
            var profile = await profiles.GetAsync(id, ownerId, cancellationToken);
            if (profile is null) return OutcomeOperation<FeedbackResult>.Invalid("profileId", "That profile was not found.");
            if (!profile.ContributeToLearning)
                return OutcomeOperation<FeedbackResult>.Ok(new FeedbackResult(false,
                    "This profile is kept out of learning, so nothing was stored."));
        }

        var learning = await settings.GetAsync<LearningSettings>(ownerId, SettingsSections.Learning, cancellationToken)
                       ?? LearningSettings.Default;
        if (!learning.CaptureSignals)
            return OutcomeOperation<FeedbackResult>.Ok(new FeedbackResult(false, "Learning from feedback is switched off, so nothing was stored."));

        var state = await LoadAsync(ownerId, cancellationToken);
        var now = clock.GetUtcNow();
        state.Feedback.Add(new OutcomeFeedback(Guid.NewGuid().ToString("N")[..12], artifactKind,
            artifactId is { Length: > 0 and <= 64 } ? artifactId : null, signal, aspect, cleanNote, profileId, now));
        state.Feedback = state.Feedback.Where(f => f.At >= now.AddDays(-LearningSettings.SignalRetentionDays))
            .OrderByDescending(f => f.At).Take(OutcomeRules.MaxFeedback).ToList();
        await settings.SaveAsync(ownerId, SettingsSections.Outcomes, state, cancellationToken);
        return OutcomeOperation<FeedbackResult>.Ok(new FeedbackResult(true, "Noted."));
    }

    public async Task<IReadOnlyList<ChangeView>> ChangesAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        var learning = await settings.GetAsync<LearningSettings>(ownerId, SettingsSections.Learning, cancellationToken)
                       ?? LearningSettings.Default;
        var now = clock.GetUtcNow();
        if (learning.ProposeImprovements)
        {
            var fresh = OutcomeRules.Propose(state.Feedback, state.Changes, now);
            if (fresh.Count > 0)
            {
                state.Changes.AddRange(fresh);
                await settings.SaveAsync(ownerId, SettingsSections.Outcomes, state, cancellationToken);
            }
        }
        return state.Changes.Where(c => c.Status is ChangeStatuses.Pending or ChangeStatuses.Applied)
            .OrderBy(c => c.Status == ChangeStatuses.Pending ? 0 : 1).ThenByDescending(c => c.CreatedAt)
            .Select(c => new ChangeView(c, OutcomeRules.Effect(c, state.Feedback, now))).ToArray();
    }

    public Task<OutcomeOperation<ChangeView>> ApplyAsync(Guid ownerId, string id, CancellationToken cancellationToken) =>
        DecideAsync(ownerId, id, ChangeStatuses.Pending, ChangeStatuses.Applied, cancellationToken);

    public Task<OutcomeOperation<ChangeView>> RejectAsync(Guid ownerId, string id, CancellationToken cancellationToken) =>
        DecideAsync(ownerId, id, ChangeStatuses.Pending, ChangeStatuses.Rejected, cancellationToken);

    public Task<OutcomeOperation<ChangeView>> UndoAsync(Guid ownerId, string id, CancellationToken cancellationToken) =>
        DecideAsync(ownerId, id, ChangeStatuses.Applied, ChangeStatuses.Undone, cancellationToken);

    public async Task<LearnedCard> LearnedAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var changes = await ChangesAsync(ownerId, cancellationToken);
        var now = clock.GetUtcNow();
        var applied = changes.Where(c => c.Change.Status == ChangeStatuses.Applied && c.Change.DecidedAt >= now.AddDays(-7)).ToArray();
        var waiting = changes.Where(c => c.Change.Status == ChangeStatuses.Pending).ToArray();
        var summary = applied.Length + waiting.Length == 0
            ? "Nothing new this week. Jarvis changes how it helps only when you have said the same thing a few times."
            : waiting.Length > 0
                ? "Here is what Jarvis would change from what you told it. Nothing changes until you say so."
                : "These changes are on. You can undo any of them.";
        var state = await LoadAsync(ownerId, cancellationToken);
        return new LearnedCard(applied, waiting, summary, OutcomeRules.SuggestionStats(state.Feedback, now));
    }

    public async Task<string?> StyleNoteAsync(Guid ownerId, Guid? profileId, CancellationToken cancellationToken) =>
        OutcomeRules.StyleNote(await LoadAsync(ownerId, cancellationToken), profileId);

    private async Task<OutcomeOperation<ChangeView>> DecideAsync(Guid ownerId, string id, string from, string to,
        CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        var index = state.Changes.FindIndex(c => c.Id == id);
        if (index < 0) return OutcomeOperation<ChangeView>.Missing();
        var change = state.Changes[index];
        if (change.Status != from)
            return OutcomeOperation<ChangeView>.Invalid("status", $"This change is {change.Status}, so it cannot be made {to}.");
        var now = clock.GetUtcNow();
        state.Changes[index] = change with { Status = to, DecidedAt = now };
        await settings.SaveAsync(ownerId, SettingsSections.Outcomes, state, cancellationToken);
        return OutcomeOperation<ChangeView>.Ok(new ChangeView(state.Changes[index], OutcomeRules.Effect(state.Changes[index], state.Feedback, now)));
    }

    private static string? CleanNote(string? note) => string.IsNullOrWhiteSpace(note)
        ? null
        : string.Join(' ', note.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private async Task<OutcomeState> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<OutcomeState>(ownerId, SettingsSections.Outcomes, cancellationToken) ?? new OutcomeState();
}
