using System.Text;
using System.Text.RegularExpressions;
using Jarvis.Application.Library;
using Jarvis.Application.Settings;
using Jarvis.Domain.Missions;

namespace Jarvis.Application.Missions;

public sealed record DeliverableSection(string StepKey, string Title, string Role, string Text);

public sealed record DeliverableEvidence(string Url, string Host, string StepTitle);

/// <summary>A check that was run by code, as opposed to a judgement about quality.</summary>
public sealed record DeliverableCheck(string Id, string Label, bool Ok);

public sealed record MissionDeliverable(
    Guid MissionId,
    string Title,
    string Status,
    string? Headline,
    IReadOnlyList<DeliverableSection> Sections,
    IReadOnlyList<DeliverableEvidence> Evidence,
    IReadOnlyList<DeliverableCheck> Checks,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> NextDecisions,
    string Effort,
    string ReviewNote,
    Guid? LibraryItemId);

/// <summary>Owner settings section <see cref="Section"/>: missions saved to the Library, so saving twice is one item.</summary>
public sealed record MissionSaves(IReadOnlyDictionary<string, Guid>? Items = null)
{
    public const string Section = "missions.saved";
}

public static partial class DeliverableBuilder
{
    public const int MaxSectionLength = 1_500;
    public const int MaxEvidence = 12;

    public const string ReviewNote =
        "These checks only show that the work exists and is complete. They cannot tell whether it is right: read it before you rely on it.";

    [GeneratedRegex(@"https?://[^\s)\]>""'<]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    public static MissionDeliverable Build(MissionDetail detail, Guid? libraryItemId)
    {
        var mission = detail.Mission;
        var steps = detail.Steps.OrderBy(s => s.Ordinal).ToArray();
        var done = steps.Where(s => s.Status == StepStatuses.Completed && !string.IsNullOrWhiteSpace(s.Result)).ToArray();
        var sections = done.Select(s => new DeliverableSection(s.Key, s.Title, s.Role,
            MissionRules.ClipText(s.Result, MaxSectionLength))).ToArray();

        var evidence = new List<DeliverableEvidence>();
        foreach (var step in done)
            foreach (Match match in UrlPattern().Matches(step.Result!))
            {
                var url = match.Value.TrimEnd('.', ',', ';', ':');
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
                if (evidence.Any(e => e.Url == url)) continue;
                evidence.Add(new DeliverableEvidence(url, uri.Host, step.Title));
                if (evidence.Count >= MaxEvidence) break;
            }

        var failed = steps.Where(s => s.Status is StepStatuses.Failed or StepStatuses.Cancelled).ToArray();
        var skipped = steps.Where(s => s.Status == StepStatuses.Skipped).ToArray();
        var finished = steps.Length > 0 && steps.All(s => StepStatuses.IsFinal(s.Status));
        var headline = !string.IsNullOrWhiteSpace(mission.Summary)
            ? MissionRules.ClipText(mission.Summary, MaxSectionLength)
            : done.LastOrDefault() is { } last ? MissionRules.ClipText(last.Result, MaxSectionLength) : null;

        var checks = new[]
        {
            new DeliverableCheck("finished", "Every step has finished", finished),
            new DeliverableCheck("nothing-failed", "No step failed or was stopped", failed.Length == 0),
            new DeliverableCheck("result", "There is a final result", headline is not null),
            new DeliverableCheck("sources", "At least one source is named", evidence.Count > 0)
        };

        var limitations = new List<string>();
        foreach (var step in failed)
            limitations.Add($"“{step.Title}” did not finish, so its part is missing.");
        foreach (var step in skipped)
            limitations.Add($"You skipped “{step.Title}”; later steps carried on without its result.");
        if (evidence.Count == 0 && done.Length > 0)
            limitations.Add("No web links were found in the result, so nothing here can be checked against a source.");

        var decisions = new List<string>();
        foreach (var step in failed) decisions.Add($"Retry or skip “{step.Title}”.");
        foreach (var note in detail.Notes.Where(n => Regex.IsMatch(n.Key, "question|decision|todo|open", RegexOptions.IgnoreCase)))
            decisions.Add(MissionRules.ClipText($"{note.Key}: {note.Value}", 200));
        if (mission.Status == MissionStatuses.Completed && decisions.Count == 0)
            decisions.Add("Decide what to do with this: save it, refine it, or start a follow-up mission.");

        return new MissionDeliverable(mission.Id, mission.Title, mission.Status, headline, sections,
            evidence, checks, limitations, decisions, Effort(mission), ReviewNote, libraryItemId);
    }

    /// <summary>Only what is known: elapsed time. Model usage is not tracked per mission, so none is invented.</summary>
    private static string Effort(Mission mission)
    {
        if (mission.StartedAt is not { } start) return "Not started yet. Cost is not estimated.";
        var end = mission.CompletedAt ?? DateTimeOffset.UtcNow;
        var minutes = Math.Max(1, (int)Math.Round((end - start).TotalMinutes));
        return $"Took about {minutes} minute{(minutes == 1 ? "" : "s")}. Model cost is not tracked per mission, so none is shown.";
    }

    public static string ToMarkdown(MissionDeliverable deliverable)
    {
        var text = new StringBuilder("# ").AppendLine(deliverable.Title);
        if (deliverable.Headline is not null) text.AppendLine().AppendLine(deliverable.Headline);
        foreach (var section in deliverable.Sections)
            text.AppendLine().Append("## ").AppendLine(section.Title).AppendLine(section.Text);
        if (deliverable.Evidence.Count > 0)
        {
            text.AppendLine().AppendLine("## Sources");
            foreach (var item in deliverable.Evidence) text.Append("- ").AppendLine(item.Url);
        }
        if (deliverable.Limitations.Count > 0)
        {
            text.AppendLine().AppendLine("## Limitations");
            foreach (var limit in deliverable.Limitations) text.Append("- ").AppendLine(limit);
        }
        return text.AppendLine().Append('_').Append(deliverable.ReviewNote).AppendLine("_").ToString();
    }
}

public interface IMissionDeliverableService
{
    Task<MissionDeliverable?> GetAsync(Guid ownerId, Guid missionId, CancellationToken cancellationToken);

    /// <summary>Saves a private Library report. Saving again returns the same item.</summary>
    Task<MissionOperation<MissionDeliverable>> SaveAsync(Guid ownerId, Guid missionId,
        CancellationToken cancellationToken);
}

public sealed class MissionDeliverableService(
    IMissionService missions,
    ILibraryService library,
    IOwnerSettingsStore settings) : IMissionDeliverableService
{
    public async Task<MissionDeliverable?> GetAsync(Guid ownerId, Guid missionId, CancellationToken cancellationToken)
    {
        var detail = await missions.GetAsync(missionId, ownerId, cancellationToken);
        if (detail is null) return null;
        var saves = await settings.GetAsync<MissionSaves>(ownerId, MissionSaves.Section, cancellationToken);
        Guid? item = saves?.Items is { } map && map.TryGetValue(missionId.ToString(), out var id) ? id : null;
        return DeliverableBuilder.Build(detail, item);
    }

    public async Task<MissionOperation<MissionDeliverable>> SaveAsync(Guid ownerId, Guid missionId,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(ownerId, missionId, cancellationToken);
        if (current is null) return MissionOperation<MissionDeliverable>.NotFound();
        if (current.LibraryItemId is not null) return MissionOperation<MissionDeliverable>.Ok(current);
        if (current.Sections.Count == 0 && current.Headline is null)
            return MissionOperation<MissionDeliverable>.Invalid("mission", "There is no result to save yet.");
        var saved = await library.AddNoteAsync(ownerId, new NoteDraft($"Mission result: {current.Title}",
            DeliverableBuilder.ToMarkdown(current), LibraryKinds.Report, null, ["mission", "private"],
            LibraryOrigins.App, null, false), cancellationToken);
        if (!saved.Succeeded)
            return MissionOperation<MissionDeliverable>.Invalid(saved.Field ?? "mission",
                saved.Message ?? "It could not be saved.");
        var map = new Dictionary<string, Guid>(
            (await settings.GetAsync<MissionSaves>(ownerId, MissionSaves.Section, cancellationToken))?.Items ??
            new Dictionary<string, Guid>()) { [missionId.ToString()] = saved.Value!.Id };
        await settings.SaveAsync(ownerId, MissionSaves.Section, new MissionSaves(map), cancellationToken);
        return MissionOperation<MissionDeliverable>.Ok(current with { LibraryItemId = saved.Value.Id });
    }
}
