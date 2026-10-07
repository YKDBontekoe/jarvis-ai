using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Memory;
using Jarvis.Application.Settings;
using Jarvis.Application.Skills;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Improvements;

namespace Jarvis.Application.Improvements;

public sealed class ImprovementService(
    IImprovementRepository repository,
    IMemoryService memories,
    ISkillRepository skills,
    INotificationRepository notifications,
    IOwnerSettingsStore settings,
    IAuditEventStore audit,
    TimeProvider? timeProvider = null) : IImprovementService
{
    private const int MaxTitle = 300;
    private const int MaxEvidence = 600;
    private const int MaxRecentlyChanged = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<ImprovementView>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var all = await repository.ListAsync(ownerId, cancellationToken);
        var pending = all.Where(item => item.Status == ImprovementStatuses.Pending)
            .OrderByDescending(item => item.Confidence).ThenBy(item => item.CreatedAt);
        var recent = all.Where(item => ImprovementStatuses.CanUndo(item.Status) &&
                                       now - item.UpdatedAt <= ImprovementRules.UndoWindow)
            .OrderByDescending(item => item.UpdatedAt).Take(MaxRecentlyChanged);
        return [.. pending.Concat(recent).Select(ToView)];
    }

    public async Task<int> SyncAsync(Guid ownerId, string fingerprintPrefix, IReadOnlyList<ProposalCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var existing = await repository.ListAsync(ownerId, cancellationToken);
        var byFingerprint = existing.ToDictionary(item => item.Fingerprint, StringComparer.Ordinal);
        var pendingCount = existing.Count(item => item.Status == ImprovementStatuses.Pending);
        var offered = new HashSet<string>(StringComparer.Ordinal);
        var added = 0;
        foreach (var candidate in candidates)
        {
            if (!ImprovementKinds.IsValid(candidate.Kind) ||
                !candidate.Fingerprint.StartsWith(fingerprintPrefix, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(candidate.Title) || string.IsNullOrWhiteSpace(candidate.PayloadJson))
                continue;
            offered.Add(candidate.Fingerprint);
            if (byFingerprint.TryGetValue(candidate.Fingerprint, out var current))
            {
                // A decided row is the owner's word; only a pending one follows the data.
                if (current.Status == ImprovementStatuses.Pending)
                    await repository.UpdateAsync(current with
                    {
                        Title = Limit(candidate.Title, MaxTitle), Evidence = Limit(candidate.Evidence, MaxEvidence),
                        Confidence = Math.Clamp(candidate.Confidence, 0, 1), PayloadJson = candidate.PayloadJson,
                        UpdatedAt = now
                    }, cancellationToken);
                continue;
            }

            if (pendingCount + added >= ImprovementRules.MaxPending) continue;
            await repository.AddAsync(NewProposal(ownerId, candidate, ImprovementStatuses.Pending, null, now),
                cancellationToken);
            added++;
        }

        var gone = existing.Where(item => item.Status == ImprovementStatuses.Pending &&
                                          item.Fingerprint.StartsWith(fingerprintPrefix, StringComparison.Ordinal) &&
                                          !offered.Contains(item.Fingerprint))
            .Select(item => item.Id).ToArray();
        if (gone.Length > 0) await repository.DeleteManyAsync(ownerId, gone, cancellationToken);

        if (added > 0) await NotifyAsync(ownerId, added, cancellationToken);
        return added;
    }

    public async Task<MemoryOutcome> SaveOrProposeMemoryAsync(Guid ownerId, MemoryCandidate candidate, bool autoApply,
        CancellationToken cancellationToken)
    {
        if (candidate.Confidence < ImprovementRules.MemoryReviewMinConfidence ||
            string.IsNullOrWhiteSpace(candidate.Content))
            return MemoryOutcome.Dropped;

        var fingerprint = ImprovementRules.MemoryFingerprint(candidate.Kind, candidate.Content);
        var existing = await repository.FindByFingerprintAsync(ownerId, fingerprint, cancellationToken);
        var canApply = autoApply && candidate.Confidence >= ImprovementRules.MemoryAutoApplyMinConfidence;
        if (existing is not null)
        {
            // Refused, already saved, or already waiting: never save it again behind the owner's back.
            if (existing.Status != ImprovementStatuses.Pending) return MemoryOutcome.Dropped;
            if (!canApply) return MemoryOutcome.Proposed;
            var memory = await CreateMemoryAsync(ownerId, JsonSerializer.Deserialize<MemoryPayload>(
                existing.PayloadJson, JsonOptions)!, cancellationToken);
            await repository.UpdateAsync(existing with
            {
                Status = ImprovementStatuses.Applied, ResultingRef = memory.Id, UpdatedAt = _clock.GetUtcNow()
            }, cancellationToken);
            await AuditAsync(ownerId, "improvement.applied", existing.Id, existing.Kind, memory.Id, cancellationToken);
            return MemoryOutcome.Saved;
        }

        var payload = new MemoryPayload(candidate.Kind, candidate.Content.Trim(), candidate.Importance,
            candidate.Confidence, candidate.ProfileId, candidate.SourceId);
        var now = _clock.GetUtcNow();
        var proposalCandidate = new ProposalCandidate(ImprovementKinds.Memory, fingerprint,
            candidate.Content.Trim(), candidate.Evidence, candidate.Confidence,
            JsonSerializer.Serialize(payload, JsonOptions));
        if (canApply)
        {
            var memory = await CreateMemoryAsync(ownerId, payload, cancellationToken);
            var proposal = NewProposal(ownerId, proposalCandidate, ImprovementStatuses.Applied, memory.Id, now);
            await repository.AddAsync(proposal, cancellationToken);
            await AuditAsync(ownerId, "improvement.applied", proposal.Id, proposal.Kind, memory.Id, cancellationToken);
            return MemoryOutcome.Saved;
        }

        var pending = (await repository.ListAsync(ownerId, cancellationToken))
            .Count(item => item.Status == ImprovementStatuses.Pending);
        if (pending >= ImprovementRules.MaxPending) return MemoryOutcome.Dropped;
        await repository.AddAsync(NewProposal(ownerId, proposalCandidate, ImprovementStatuses.Pending, null, now),
            cancellationToken);
        await NotifyAsync(ownerId, 1, cancellationToken);
        return MemoryOutcome.Proposed;
    }

    public async Task<ImprovementView?> AcceptAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var proposal = await repository.GetAsync(id, ownerId, cancellationToken);
        if (proposal is null || ImprovementStatuses.IsRefusal(proposal.Status)) return null;
        if (proposal.Status != ImprovementStatuses.Pending) return ToView(proposal);

        Guid? resulting;
        try
        {
            resulting = await ApplyAsync(ownerId, proposal, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            // A payload that no longer validates is not something the owner can fix by retrying.
            return null;
        }

        if (resulting is null && proposal.Kind != ImprovementKinds.Review) return null;
        var accepted = proposal with
        {
            Status = ImprovementStatuses.Accepted, ResultingRef = resulting, UpdatedAt = _clock.GetUtcNow()
        };
        await repository.UpdateAsync(accepted, cancellationToken);
        await AuditAsync(ownerId, "improvement.accepted", proposal.Id, proposal.Kind, resulting, cancellationToken);
        return ToView(accepted);
    }

    public async Task<bool> DismissAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var proposal = await repository.GetAsync(id, ownerId, cancellationToken);
        if (proposal is null || ImprovementStatuses.CanUndo(proposal.Status)) return false;
        if (proposal.Status is ImprovementStatuses.Dismissed or ImprovementStatuses.Undone) return true;
        return await repository.UpdateAsync(proposal with
        {
            Status = ImprovementStatuses.Dismissed, UpdatedAt = _clock.GetUtcNow()
        }, cancellationToken);
    }

    public async Task<bool> UndoAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var proposal = await repository.GetAsync(id, ownerId, cancellationToken);
        if (proposal is null || !ImprovementStatuses.CanUndo(proposal.Status)) return false;
        await RevertAsync(ownerId, proposal, cancellationToken);
        var undone = await repository.UpdateAsync(proposal with
        {
            Status = ImprovementStatuses.Undone, UpdatedAt = _clock.GetUtcNow()
        }, cancellationToken);
        if (undone)
            await AuditAsync(ownerId, "improvement.undone", proposal.Id, proposal.Kind, proposal.ResultingRef,
                cancellationToken);
        return undone;
    }

    private async Task<Guid?> ApplyAsync(Guid ownerId, ImprovementProposal proposal,
        CancellationToken cancellationToken)
    {
        switch (proposal.Kind)
        {
            case ImprovementKinds.Memory:
            {
                var payload = JsonSerializer.Deserialize<MemoryPayload>(proposal.PayloadJson, JsonOptions)
                              ?? throw new JsonException("Empty memory payload.");
                return (await CreateMemoryAsync(ownerId, payload, cancellationToken)).Id;
            }
            case ImprovementKinds.Skill:
            {
                var payload = JsonSerializer.Deserialize<SkillPayload>(proposal.PayloadJson, JsonOptions)
                              ?? throw new JsonException("Empty skill payload.");
                var draft = SkillMarkdown.Validate(new SkillDraft(payload.Name, payload.Description,
                    payload.Instructions));
                var saved = await skills.UpsertAsync(ownerId, draft, SkillSources.Learned, SkillStatuses.Active,
                    respectLock: true, "Accepted improvement", cancellationToken);
                return saved?.Id;
            }
            case ImprovementKinds.Review:
            {
                var payload = JsonSerializer.Deserialize<ReviewPayload>(proposal.PayloadJson, JsonOptions)
                              ?? throw new JsonException("Empty review payload.");
                if (payload.Action != ReviewActions.DisableSkill || string.IsNullOrWhiteSpace(payload.Target))
                    return null;
                var skill = await skills.FindByNameAsync(ownerId, payload.Target, cancellationToken);
                if (skill is null) return null;
                await skills.SetStatusAsync(skill.Id, ownerId, SkillStatuses.Disabled, cancellationToken);
                return skill.Id;
            }
            default:
                return null;
        }
    }

    private async Task RevertAsync(Guid ownerId, ImprovementProposal proposal, CancellationToken cancellationToken)
    {
        if (proposal.ResultingRef is not { } reference) return;
        switch (proposal.Kind)
        {
            case ImprovementKinds.Memory:
                await memories.DeleteAsync(reference, ownerId, cancellationToken);
                break;
            case ImprovementKinds.Skill:
                // The skill stays with its history; turning it off is enough to take it out of use.
                await skills.SetStatusAsync(reference, ownerId, SkillStatuses.Disabled, cancellationToken);
                break;
            case ImprovementKinds.Review:
                await skills.SetStatusAsync(reference, ownerId, SkillStatuses.Active, cancellationToken);
                break;
        }
    }

    private Task<Jarvis.Domain.Memory.MemoryRecord> CreateMemoryAsync(Guid ownerId, MemoryPayload payload,
        CancellationToken cancellationToken) =>
        memories.CreateAsync(ownerId, payload.Kind, payload.Content, payload.Importance, payload.Confidence, null,
            false, cancellationToken, sourceType: "conversation", sourceId: payload.SourceId,
            profileId: payload.ProfileId);

    /// <summary>One push for new suggestions, at most weekly, that only counts them: memory text never goes in a push.</summary>
    private async Task NotifyAsync(Guid ownerId, int added, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var state = await settings.GetAsync<ImprovementState>(ownerId, SettingsSections.Improvements,
            cancellationToken) ?? new ImprovementState(null);
        if (state.LastNotifiedAt is { } last && now - last < ImprovementRules.NotifyInterval) return;
        try
        {
            await notifications.CreateAsync(ownerId, ImprovementRules.NotificationType, "Jarvis has a suggestion",
                added == 1 ? "1 improvement is waiting for your review." : $"{added} improvements are waiting for your review.",
                null, cancellationToken);
            await settings.SaveAsync(ownerId, SettingsSections.Improvements, new ImprovementState(now),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The proposals are stored; a failed push must not lose them or fail the pass that found them.
        }
    }

    private async Task AuditAsync(Guid ownerId, string action, Guid proposalId, string kind, Guid? reference,
        CancellationToken cancellationToken)
    {
        try
        {
            // Ids and kind only: the proposal text can be a memory the owner did not want in a log.
            await audit.AppendAsync(ownerId, "improvements", action, "low", true, null,
                JsonSerializer.Serialize(new { proposalId, kind, resultingRef = reference }, JsonOptions),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private ImprovementProposal NewProposal(Guid ownerId, ProposalCandidate candidate, string status, Guid? reference,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(), ownerId, candidate.Kind, candidate.Fingerprint, Limit(candidate.Title, MaxTitle),
            Limit(candidate.Evidence, MaxEvidence), Math.Clamp(candidate.Confidence, 0, 1), candidate.PayloadJson,
            status, reference, now, now);

    private static ImprovementView ToView(ImprovementProposal proposal) =>
        new(proposal.Id, proposal.Kind, proposal.Title, proposal.Evidence, proposal.Confidence, proposal.Status,
            proposal.CreatedAt, proposal.UpdatedAt);

    private static string Limit(string? text, int max)
    {
        var clean = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= max ? clean : clean[..(max - 1)].TrimEnd() + "…";
    }
}
