using Jarvis.Application.Events;
using Jarvis.Application.Memory;

namespace Jarvis.Application.Proactivity;

public sealed record ProactiveMemoryChoice(long Revision, string Choice, string? FirstContext = null, string? SecondContext = null,
    IReadOnlyDictionary<string, string>? SourceVersions = null);

/// <summary>Owner-reviewed corrections use the existing memory supersession/history path, including pinned protection.</summary>
public sealed class ProactiveMemoryResolution(IProactiveRepository repository, IProactiveSourceReader sources,
    IMemoryReviewService memories, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ProactiveCase?> ResolveAsync(Guid ownerId, Guid id, ProactiveMemoryChoice choice, CancellationToken ct)
    {
        var value = await repository.GetAsync(ownerId, id, ct);
        if (value is null) return null;
        if (value.Revision != choice.Revision) throw new ProactiveConflictException();
        if (value.Kind != "memory_conflict" || value.Terminal) throw new ArgumentException("Choose an open memory comparison.");
        var current = (await sources.ReadAsync(ownerId, value.ProfileId, value.SourceRefs, ct))
            .Where(s => s.Ref.StartsWith("memory:", StringComparison.Ordinal)).ToArray();
        if (current.Length != 2) throw new ArgumentException("Choose exactly two current memories to compare.");
        if (choice.SourceVersions is null || current.Any(s => !choice.SourceVersions.TryGetValue(s.Ref, out var version) || version != s.Version))
            throw new ProactiveConflictException();
        var edits = choice.Choice switch
        {
            "keep_first" => new[] { (Source: current[1], Text: current[0].Detail) },
            "keep_second" => new[] { (Source: current[0], Text: current[1].Detail) },
            "both" when !string.IsNullOrWhiteSpace(choice.FirstContext) && !string.IsNullOrWhiteSpace(choice.SecondContext) =>
                new[] { (Source: current[0], Text: current[0].Detail + " (Applies when: " + choice.FirstContext.Trim() + ")."),
                    (Source: current[1], Text: current[1].Detail + " (Applies when: " + choice.SecondContext.Trim() + ").") },
            _ => throw new ArgumentException("Choose keep_first, keep_second, or both with a context for each memory.")
        };
        if (edits.Any(e => e.Text.Length > 2000)) throw new ArgumentException("Keep each qualified memory under 2,000 characters.");
        if (edits.Any(e => e.Source.IsPinned)) throw new ArgumentException("Pinned memories are protected. Edit or unpin them in Memory first.");
        var runId = Guid.CreateVersion7();
        var claimed = value with { Revision = value.Revision + 1, RunId = runId, LeaseUntil = clock.GetUtcNow().AddMinutes(5) };
        if (!await repository.CompareExchangeAsync(claimed, value.Revision, ct)) throw new ProactiveConflictException();
        var replacements = new Dictionary<string, string>();
        string? failure = null;
        foreach (var edit in edits)
        {
            var result = await memories.CorrectAsync(ownerId, EntityRef.Parse(edit.Source.Ref).Id, edit.Text, ct);
            if (result.Outcome is not (MemoryCorrectionOutcome.Corrected or MemoryCorrectionOutcome.Unchanged) || result.Memory is null)
            {
                failure = result.Outcome == MemoryCorrectionOutcome.Pinned
                    ? "A pinned memory was kept unchanged. Edit or unpin it in Memory before resolving this comparison."
                    : "A memory changed during review. Review the current versions before continuing.";
                break;
            }
            replacements[edit.Source.Ref] = $"memory:{result.Memory.Id}";
        }
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var latest = await repository.GetAsync(ownerId, id, ct);
            if (latest is null) return null;
            var reason = failure is null ? "Applied the owner's reviewed memory choice. Earlier versions remain in Memory history." :
                (replacements.Count > 0 ? "Some reviewed changes were applied; review Memory history. " : "") + failure;
            var receipt = new ProactiveReceipt(Guid.CreateVersion7(), "memory_resolution", reason,
                ProactiveRules.Serialize(current.Select(s => new { s.Ref, s.Detail })), ProactiveRules.Serialize(replacements), clock.GetUtcNow(), latest.Revision + 1);
            var next = latest with { Revision = latest.Revision + 1, UpdatedAt = clock.GetUtcNow(),
                SourceRefs = latest.SourceRefs.Select(r => replacements.GetValueOrDefault(r, r)).Distinct().ToArray(),
                RootRef = latest.RootRef is { } root ? replacements.GetValueOrDefault(root, root) : null,
                Receipts = latest.Receipts.Append(receipt).TakeLast(200).ToArray(), RunId = null, LeaseUntil = null,
                Status = latest.Terminal ? latest.Status : failure is null ? "resolved" : "needs_input",
                Enabled = false, NextCheckAt = null, NextAction = reason, PreparedReport = null };
            next = next with { Context = await sources.ReadAsync(ownerId, next.ProfileId, next.SourceRefs, ct), Findings = [] };
            if (await repository.CompareExchangeAsync(next, latest.Revision, ct)) return next;
        }
        throw new ProactiveConflictException();
    }
}
