using System.Text.Json;
using Jarvis.Application.Learning;
using Jarvis.Application.Memory;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;

namespace Jarvis.Application.Improvements;

/// <summary>How many proposals one mining pass added.</summary>
public sealed record MiningResult(int Skills, int Reviews);

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
    TimeProvider? timeProvider = null) : IImprovementMiner
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
        var added = new MiningResult(await MineSkillsAsync(ownerId, traces, now, cancellationToken),
            await MineReviewsAsync(ownerId, traces, signals, now, since, cancellationToken));
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
