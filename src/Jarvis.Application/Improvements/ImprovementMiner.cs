using System.Text.Json;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;

namespace Jarvis.Application.Improvements;

/// <summary>How many proposals one mining pass added.</summary>
public sealed record MiningResult(int Skills, int Reviews, int Lessons = 0, int Refinements = 0,
    int Capabilities = 0);

/// <summary>Drafts the instructions of a skill from a tool sequence Jarvis keeps repeating. Returns null if nothing usable.</summary>
public interface ISkillDrafter
{
    Task<SkillDraft?> DraftAsync(Guid ownerId, ToolSequenceFinding finding, CancellationToken cancellationToken);
}

public interface IImprovementMiner
{
    /// <summary>Looks at recent run traces and ratings and files what it finds as proposals. Does nothing when the owner switched proposals off.</summary>
    Task<MiningResult> RefreshAsync(Guid ownerId, CancellationToken cancellationToken);
}

public sealed class ImprovementMiner(
    ILearningStore learning,
    IImprovementRepository repository,
    IImprovementService improvements,
    ISkillRepository skills,
    IMemoryService memories,
    ISkillDrafter drafter,
    IOwnerSettingsStore settings,
    TimeProvider? timeProvider = null,
    ILessonDrafter? lessonDrafter = null,
    ISkillRefiner? skillRefiner = null) : IImprovementMiner
{
    /// <summary>Each draft is a model call, so a night drafts at most this many.</summary>
    public const int MaxDraftsPerRun = 2;

    private const int MaxTraces = 2_000;
    private const int MaxExcerpt = 80;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<MiningResult> RefreshAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var owner = await settings.GetAsync<LearningSettings>(ownerId, SettingsSections.Learning, cancellationToken)
                    ?? LearningSettings.Default;
        if (!owner.ProposeImprovements) return new MiningResult(0, 0);
        var now = _clock.GetUtcNow();
        var since = now.AddDays(-SkillMinerEngine.WindowDays);
        var traces = await learning.ListTracesAsync(ownerId, since, MaxTraces, cancellationToken);
        var signals = await learning.ListSignalsAsync(ownerId, since, cancellationToken);
        // Count last night's lessons first, so one that hurt is out of force before new ones are weighed against it.
        await CountLessonsAsync(ownerId, traces, signals, now, cancellationToken);
        await CountSkillOutcomesAsync(ownerId, traces, signals, now, cancellationToken);
        var added = new MiningResult(await MineSkillsAsync(ownerId, traces, now, cancellationToken),
            await MineReviewsAsync(ownerId, traces, signals, now, since, cancellationToken),
            await MineLessonsAsync(ownerId, traces, signals, now, owner.AutoApplyLowRiskMemory, cancellationToken),
            await MineRefinementsAsync(ownerId, traces, signals, cancellationToken),
            await MineCapabilityGapsAsync(ownerId, signals, now, cancellationToken));
        return added;
    }

    /// <summary>
    /// Offers each capability Jarvis lacked in several conversations. A gap already offered keeps its build state; one
    /// with a tested draft stays offered after its reports age out, since the draft is still waiting for the owner.
    /// </summary>
    private async Task<int> MineCapabilityGapsAsync(Guid ownerId, IReadOnlyList<LearningSignalRecord> signals,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var candidates = new List<ProposalCandidate>();
        var offered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in CapabilityGapMinerEngine.Mine(signals, now))
        {
            var existing = await repository.FindByFingerprintAsync(ownerId, finding.Fingerprint, cancellationToken);
            var payload = (existing is null ? null : ReadCapability(existing.PayloadJson)) ??
                          new CapabilityPayload(finding.Slug, 0, 0);
            payload = payload with { Reports = finding.Reports, Conversations = finding.Conversations };
            offered.Add(finding.Fingerprint);
            candidates.Add(new ProposalCandidate(ImprovementKinds.Capability, finding.Fingerprint,
                CapabilityText.Title(payload), CapabilityText.Evidence(payload),
                CapabilityGapMinerEngine.Confidence(finding), JsonSerializer.Serialize(payload, JsonOptions)));
        }

        foreach (var waiting in (await repository.ListAsync(ownerId, cancellationToken)).Where(item =>
                     item.Kind == ImprovementKinds.Capability && item.Status == ImprovementStatuses.Pending &&
                     !offered.Contains(item.Fingerprint) && ReadCapability(item.PayloadJson) is { IsBuilt: true }))
            candidates.Add(new ProposalCandidate(waiting.Kind, waiting.Fingerprint, waiting.Title, waiting.Evidence,
                waiting.Confidence, waiting.PayloadJson));

        return await improvements.SyncAsync(ownerId, CapabilityGapRules.FingerprintPrefix, candidates,
            cancellationToken);
    }

    internal static CapabilityPayload? ReadCapability(string json)
    {
        try { return JsonSerializer.Deserialize<CapabilityPayload>(json, JsonOptions) is { Slug.Length: > 0 } payload ? payload : null; }
        catch (JsonException) { return null; }
    }

    /// <summary>Adds last night's runs and how they went to each skill's current version.</summary>
    private async Task CountSkillOutcomesAsync(Guid ownerId, IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var deltas = SkillOutcomeEngine.Tally(traces, signals, await skills.ListAsync(ownerId, cancellationToken), now);
        if (deltas.Count > 0) await skills.AddOutcomesAsync(ownerId, deltas, cancellationToken);
    }

    /// <summary>Offers a redraft of each skill whose runs mostly go badly. Never applied without the owner.</summary>
    private async Task<int> MineRefinementsAsync(Guid ownerId, IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals, CancellationToken cancellationToken)
    {
        if (skillRefiner is null) return 0;
        var candidates = new List<ProposalCandidate>();
        var drafts = 0;
        // Counts were just brought up to date, so read the skills again.
        foreach (var skill in SkillRefineEngine.Candidates(await skills.ListAsync(ownerId, cancellationToken)))
        {
            var fingerprint = SkillRefineRules.Fingerprint(skill);
            var evidence = SkillRefineEngine.Collect(skill, traces, signals);
            var summary = $"“{skill.Name}” went well {skill.OutcomeSuccesses} of {skill.OutcomeRuns} times since it last changed." +
                          (evidence.FailedTools.Count == 0
                              ? string.Empty
                              : $" Tools that failed in it: {string.Join(", ", evidence.FailedTools.Select(tool => $"{tool.Key} ×{tool.Value}"))}.");
            var confidence = Math.Min(0.9, 0.5 + 0.05 * skill.OutcomeRuns);
            var existing = await repository.FindByFingerprintAsync(ownerId, fingerprint, cancellationToken);
            if (existing is not null)
            {
                // Already offered (or decided): carry the saved draft so a pending row keeps following the data.
                candidates.Add(new ProposalCandidate(ImprovementKinds.SkillRefine, fingerprint, existing.Title, summary,
                    confidence, existing.PayloadJson));
                continue;
            }

            if (drafts >= SkillRefineRules.MaxDraftsPerRun) continue;
            drafts++;
            var draft = await skillRefiner.RefineAsync(ownerId, skill, evidence, cancellationToken);
            if (draft is null) continue;
            candidates.Add(new ProposalCandidate(ImprovementKinds.SkillRefine, fingerprint,
                $"Improve “{skill.Name}”", summary, confidence,
                JsonSerializer.Serialize(new SkillRefinePayload(skill.Name, draft.Description, draft.Instructions,
                    skill.Version), JsonOptions)));
        }

        return await improvements.SyncAsync(ownerId, SkillRefineRules.FingerprintPrefix, candidates, cancellationToken);
    }

    private async Task CountLessonsAsync(Guid ownerId, IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var inForce = await improvements.LessonsInForceAsync(ownerId, cancellationToken);
        if (inForce.Count == 0) return;
        var tallies = LessonCounterEngine.Tally(traces, signals,
            inForce.ToDictionary(lesson => lesson.ProposalId, lesson => lesson.Payload), now);
        await improvements.ApplyLessonTalliesAsync(ownerId, tallies, now, cancellationToken);
    }

    private async Task<int> MineLessonsAsync(Guid ownerId, IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals, DateTimeOffset now, bool autoApply,
        CancellationToken cancellationToken)
    {
        if (lessonDrafter is null) return 0;
        var inForce = await improvements.LessonsInForceAsync(ownerId, cancellationToken);
        if (inForce.Count >= LessonRules.MaxInForce) return 0;
        var covered = inForce.SelectMany(lesson => lesson.Payload.Tools).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        var drafts = 0;
        foreach (var finding in LessonMinerEngine.Mine(traces, signals, now))
        {
            if (covered.Contains(finding.Tool)) continue;
            var fingerprint = LessonRules.Fingerprint(finding.Tool, now);
            if (await repository.FindByFingerprintAsync(ownerId, fingerprint, cancellationToken) is not null) continue;
            if (drafts >= LessonRules.MaxDraftsPerRun) break;
            drafts++;
            var text = await lessonDrafter.DraftAsync(ownerId, finding, cancellationToken);
            if (string.IsNullOrWhiteSpace(text)) continue;
            var evidence = $"{finding.Tool} was part of {finding.Incidents} turns in the last {LessonRules.WindowDays} " +
                           $"days that went wrong ({string.Join(", ", finding.Reasons.Select(reason => reason.Replace('_', ' ')))}).";
            var candidate = new ProposalCandidate(ImprovementKinds.Lesson, fingerprint, $"Lesson: {text}", evidence,
                LessonMinerEngine.Confidence(finding),
                JsonSerializer.Serialize(new LessonPayload(text, [finding.Tool]), JsonOptions));
            if (await improvements.ProposeLessonAsync(ownerId, candidate, autoApply, cancellationToken)) added++;
        }

        return added;
    }

    private async Task<int> MineSkillsAsync(Guid ownerId, IReadOnlyList<TurnTraceRecord> traces, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ProposalCandidate>();
        var drafts = 0;
        foreach (var finding in SkillMinerEngine.Mine(traces, now))
        {
            var evidence = $"Used {string.Join(" then ", finding.Tools)} in the same order {finding.Runs} times " +
                           $"across {finding.Conversations} conversations in the last {SkillMinerEngine.WindowDays} days.";
            var existing = await repository.FindByFingerprintAsync(ownerId, finding.Fingerprint, cancellationToken);
            if (existing is not null)
            {
                // Already offered (or decided): carry the saved draft so the pending row keeps following the data.
                candidates.Add(new ProposalCandidate(ImprovementKinds.Skill, finding.Fingerprint, existing.Title,
                    evidence, SkillMinerEngine.Confidence(finding), existing.PayloadJson));
                continue;
            }

            if (drafts >= MaxDraftsPerRun) continue;
            drafts++;
            var draft = await drafter.DraftAsync(ownerId, finding, cancellationToken);
            if (draft is null) continue;
            candidates.Add(new ProposalCandidate(ImprovementKinds.Skill, finding.Fingerprint,
                $"Save “{draft.Name}” as a skill", evidence, SkillMinerEngine.Confidence(finding),
                JsonSerializer.Serialize(new SkillPayload(draft.Name, draft.Description, draft.Instructions),
                    JsonOptions)));
        }

        return await improvements.SyncAsync(ownerId, SkillMinerEngine.FingerprintPrefix, candidates, cancellationToken);
    }

    private async Task<int> MineReviewsAsync(Guid ownerId, IReadOnlyList<TurnTraceRecord> traces,
        IReadOnlyList<LearningSignalRecord> signals, DateTimeOffset now, DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var active = (await skills.ListAsync(ownerId, cancellationToken))
            .Where(skill => skill.Status == SkillStatuses.Active)
            .ToDictionary(skill => skill.Name, StringComparer.Ordinal);
        var candidates = new List<ProposalCandidate>();
        foreach (var finding in ReviewMinerEngine.Mine(traces, signals, now, since))
        {
            var candidate = await ToCandidateAsync(ownerId, finding, active, now, cancellationToken);
            if (candidate is not null) candidates.Add(candidate);
        }

        return await improvements.SyncAsync(ownerId, ReviewMinerEngine.FingerprintPrefix, candidates, cancellationToken);
    }

    private async Task<ProposalCandidate?> ToCandidateAsync(Guid ownerId, ReviewFinding finding,
        IReadOnlyDictionary<string, SkillRecord> activeSkills, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var fingerprint = ReviewMinerEngine.Fingerprint(finding, now);
        switch (finding.Kind)
        {
            case ReviewFinding.SkillKind when activeSkills.ContainsKey(finding.Target):
                return new ProposalCandidate(ImprovementKinds.Review, fingerprint,
                    $"“{finding.Target}” keeps getting thumbs-down",
                    $"It was used in {finding.Count} replies you rated down and {finding.Positive} you rated up. " +
                    "Turning it off keeps it saved; you can switch it back on.",
                    Math.Min(0.9, 0.5 + 0.1 * finding.Count),
                    JsonSerializer.Serialize(new ReviewPayload(ReviewActions.DisableSkill, finding.Target),
                        JsonOptions));
            case ReviewFinding.MemoryKind when Guid.TryParseExact(finding.Target, "N", out var memoryId):
                var memory = await memories.GetAsync(memoryId, ownerId, cancellationToken);
                if (memory is null) return null;
                return new ProposalCandidate(ImprovementKinds.Review, fingerprint,
                    $"Check this memory: “{Excerpt(memory.Content)}”",
                    $"It was in {finding.Count} replies you rated down and {finding.Positive} you rated up. " +
                    "It may be out of date or misleading; review it in Memory.",
                    Math.Min(0.8, 0.4 + 0.1 * finding.Count),
                    JsonSerializer.Serialize(new ReviewPayload(ReviewActions.Note, finding.Target), JsonOptions));
            case ReviewFinding.ToolKind:
                return new ProposalCandidate(ImprovementKinds.Review, fingerprint,
                    $"The {finding.Target} tool is mostly failing",
                    $"{finding.Count} of {finding.Total} calls failed in the last {ReviewMinerEngine.ToolWindowDays} days. " +
                    "A connection or its permissions may need attention.",
                    Math.Min(0.9, 0.5 + (double)finding.Count / finding.Total * 0.4),
                    JsonSerializer.Serialize(new ReviewPayload(ReviewActions.Note, finding.Target), JsonOptions));
            default:
                return null;
        }
    }

    private static string Excerpt(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= MaxExcerpt ? flat : flat[..MaxExcerpt].TrimEnd() + "…";
    }
}
