using Jarvis.Application.Inbox;
using Jarvis.Application.Library;
using Jarvis.Application.Projects;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Documents;

/// <summary>
/// Drafts built from the owner's own records, kept in owner settings. Saving writes a Library report and exporting
/// returns a file; neither sends or shares anything.
/// </summary>
public sealed class DocumentService(
    IOwnerSettingsStore settings,
    IProjectStore projects,
    IProjectFocusService projectFocus,
    ICommitmentService commitments,
    ILibraryService library,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null,
    Proactivity.IProactiveSourceReader? sourceReader = null) : IDocumentService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<DocDraft>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await LoadAsync(ownerId, cancellationToken)).OrderByDescending(draft => draft.UpdatedAt).ToArray();

    public async Task<DocumentView?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        (await LoadAsync(ownerId, cancellationToken)).FirstOrDefault(draft => draft.Id == id) is { } draft
            ? await CurrentViewAsync(ownerId, draft, cancellationToken)
            : null;

    public async Task<DocumentOperation<DocumentView>> CreateAsync(Guid ownerId, DocumentRequest request,
        CancellationToken cancellationToken)
    {
        if ((request.Kind ?? DocumentKinds.ProjectUpdate) != DocumentKinds.ProjectUpdate)
            return DocumentOperation<DocumentView>.Invalid("kind", "Only a project update can be made so far.");
        if (request.ProjectId is not { } projectId)
            return DocumentOperation<DocumentView>.Invalid("projectId", "Pick the project the update is about.");
        var audience = request.Audience ?? "team";
        if (!DocumentRules.Audiences.Contains(audience))
            return DocumentOperation<DocumentView>.Invalid("audience", "Choose team, client or self.");
        var language = request.Language ?? "en";
        if (!DocumentRules.Languages.Contains(language))
            return DocumentOperation<DocumentView>.Invalid("language", "Choose en or nl.");
        var all = await LoadAsync(ownerId, cancellationToken);
        if (all.Count >= DocumentRules.MaxDrafts)
            return DocumentOperation<DocumentView>.Conflict("drafts", $"Keep at most {DocumentRules.MaxDrafts} drafts. Delete one first.");

        var sources = await GatherAsync(ownerId, projectId, cancellationToken);
        if (sources is null) return DocumentOperation<DocumentView>.Invalid("projectId", "That project was not found.");
        var today = await TodayAsync(ownerId, cancellationToken);
        var (sections, unknowns) = ProjectUpdateBuilder.Build(sources, audience, language, today);
        var now = clock.GetUtcNow();
        var draft = new DocDraft(Guid.NewGuid(), $"{sources.Project.Name}: update {today:d MMM yyyy}",
            DocumentKinds.ProjectUpdate, projectId, audience, language, sections, unknowns, 1, 0, null, now, now);
        if (sourceReader is not null)
            draft = draft with { SourceVersions = await VersionsAsync(ownerId, sections, cancellationToken) };
        await SaveStateAsync(ownerId, [draft, .. all], cancellationToken);
        return DocumentOperation<DocumentView>.Ok(ViewOf(draft));
    }

    public async Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken)
    {
        var all = await LoadAsync(ownerId, cancellationToken);
        if (all.All(draft => draft.Id != id)) return false;
        await SaveStateAsync(ownerId, all.Where(draft => draft.Id != id).ToArray(), cancellationToken);
        return true;
    }

    public Task<DocumentOperation<DocumentView>> EditSectionAsync(Guid ownerId, Guid id, string sectionId,
        string? body, CancellationToken cancellationToken) =>
        MutateSectionAsync(ownerId, id, sectionId, (_, section) =>
        {
            var text = (body ?? string.Empty).Trim();
            if (text.Length == 0)
                return Task.FromResult(DocumentOperation<DocSection>.Invalid("body", "A section cannot be empty. Delete the draft or write something."));
            if (text.Length > DocumentRules.MaxBody)
                return Task.FromResult(DocumentOperation<DocSection>.Invalid("body", $"Keep a section under {DocumentRules.MaxBody} characters."));
            return Task.FromResult(DocumentOperation<DocSection>.Ok(section with
            {
                Body = text, Edited = true, Previous = section.Body
            }));
        }, cancellationToken);

    public Task<DocumentOperation<DocumentView>> ReviseSectionAsync(Guid ownerId, Guid id, string sectionId,
        string? action, CancellationToken cancellationToken) =>
        MutateSectionAsync(ownerId, id, sectionId, async (draft, section) =>
        {
            switch (action)
            {
                case "shorten":
                    var shorter = ProjectUpdateBuilder.Shorten(section.Body);
                    return shorter == section.Body
                        ? DocumentOperation<DocSection>.Conflict("action", "That section is already short.")
                        : DocumentOperation<DocSection>.Ok(section with { Body = shorter, Edited = true, Previous = section.Body });
                case "rebuild":
                    var sources = await GatherAsync(ownerId, draft.ProjectId, cancellationToken);
                    if (sources is null)
                        return DocumentOperation<DocSection>.Invalid("projectId", "The project no longer exists.");
                    var (fresh, _) = ProjectUpdateBuilder.Build(sources, draft.Audience, draft.Language,
                        await TodayAsync(ownerId, cancellationToken));
                    var rebuilt = fresh.First(s => s.Id == section.Id);
                    return DocumentOperation<DocSection>.Ok(rebuilt with
                    {
                        Edited = false, Previous = section.Body == rebuilt.Body ? section.Previous : section.Body
                    });
                default:
                    return DocumentOperation<DocSection>.Invalid("action", "Choose shorten or rebuild.");
            }
        }, cancellationToken);

    public Task<DocumentOperation<DocumentView>> UndoSectionAsync(Guid ownerId, Guid id, string sectionId,
        CancellationToken cancellationToken) =>
        MutateSectionAsync(ownerId, id, sectionId, (_, section) => Task.FromResult(
            section.Previous is null
                ? DocumentOperation<DocSection>.Conflict("previous", "There is nothing to undo in this section.")
                : DocumentOperation<DocSection>.Ok(section with { Body = section.Previous, Previous = section.Body, Edited = true })),
            cancellationToken);

    public async Task<DocumentOperation<DocumentView>> SaveAsync(Guid ownerId, Guid id,
        CancellationToken cancellationToken)
    {
        var all = await LoadAsync(ownerId, cancellationToken);
        var draft = all.FirstOrDefault(item => item.Id == id);
        if (draft is null) return DocumentOperation<DocumentView>.NotFound();
        if (draft.SavedVersion == draft.Version && draft.LibraryItemId is not null)
            return DocumentOperation<DocumentView>.Ok(ViewOf(draft)); // this version is already saved
        var saved = await library.AddNoteAsync(ownerId,
            new NoteDraft($"{draft.Title} (v{draft.Version})", ProjectUpdateBuilder.ToMarkdown(draft),
                LibraryKinds.Report, null, ["project-update"], LibraryOrigins.App, draft.ProjectId), cancellationToken);
        if (!saved.Succeeded)
            return DocumentOperation<DocumentView>.Invalid(saved.Field ?? "library", saved.Message ?? "Could not save to the Library.");
        var updated = draft with { SavedVersion = draft.Version, LibraryItemId = saved.Value!.Id };
        await SaveStateAsync(ownerId, all.Select(item => item.Id == id ? updated : item).ToArray(), cancellationToken);
        return DocumentOperation<DocumentView>.Ok(ViewOf(updated));
    }

    public async Task<DocumentOperation<DocumentExport>> ExportAsync(Guid ownerId, Guid id, string? format,
        CancellationToken cancellationToken)
    {
        var draft = (await LoadAsync(ownerId, cancellationToken)).FirstOrDefault(item => item.Id == id);
        if (draft is null) return DocumentOperation<DocumentExport>.NotFound();
        var slug = new string(draft.Title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');
        return (format ?? "md") switch
        {
            "md" => DocumentOperation<DocumentExport>.Ok(new DocumentExport($"{slug}-v{draft.Version}.md",
                "text/markdown; charset=utf-8", ProjectUpdateBuilder.ToMarkdown(draft))),
            "txt" => DocumentOperation<DocumentExport>.Ok(new DocumentExport($"{slug}-v{draft.Version}.txt",
                "text/plain; charset=utf-8", ProjectUpdateBuilder.ToPlainText(draft))),
            _ => DocumentOperation<DocumentExport>.Invalid("format", "Export as md or txt.")
        };
    }

    private async Task<DocumentOperation<DocumentView>> MutateSectionAsync(Guid ownerId, Guid id, string sectionId,
        Func<DocDraft, DocSection, Task<DocumentOperation<DocSection>>> change, CancellationToken cancellationToken)
    {
        var all = await LoadAsync(ownerId, cancellationToken);
        var draft = all.FirstOrDefault(item => item.Id == id);
        var section = draft?.Sections.FirstOrDefault(item => item.Id == sectionId);
        if (draft is null || section is null) return DocumentOperation<DocumentView>.NotFound();
        var result = await change(draft, section);
        if (!result.Succeeded)
            return new DocumentOperation<DocumentView>(default, result.Failure, result.Field, result.Message);
        var updated = draft with
        {
            Sections = draft.Sections.Select(item => item.Id == sectionId ? result.Value! : item).ToArray(),
            Version = draft.Version + 1,
            UpdatedAt = clock.GetUtcNow()
        };
        if (sourceReader is not null && !result.Value!.Edited)
        {
            var versions = new Dictionary<string, string>(draft.SourceVersions ?? new Dictionary<string, string>());
            foreach (var pair in await VersionsAsync(ownerId, [result.Value], cancellationToken)) versions[pair.Key] = pair.Value;
            updated = updated with { SourceVersions = versions };
        }
        await SaveStateAsync(ownerId, all.Select(item => item.Id == id ? updated : item).ToArray(), cancellationToken);
        return DocumentOperation<DocumentView>.Ok(await CurrentViewAsync(ownerId, updated, cancellationToken));
    }

    private async Task<ProjectUpdateSources?> GatherAsync(Guid ownerId, Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, ownerId, cancellationToken);
        if (project is null) return null;
        var next = await projectFocus.DescribeAsync(ownerId, project, cancellationToken);
        var open = await commitments.ListAsync(ownerId, null, CommitmentStatuses.Open, false, cancellationToken);
        var notes = await library.ListAsync(ownerId, new LibraryQuery(null, null, null, 100), cancellationToken);
        return new ProjectUpdateSources(project,
            next is { State: ProjectNextActionStates.Open } ? next.Title ?? next.Note : null,
            open.Where(item => item.ProjectId == projectId).OrderBy(item => item.DueOn ?? DateOnly.MaxValue).ToArray(),
            notes.Where(item => item.ProjectId == projectId).ToArray());
    }

    private static DocumentView ViewOf(DocDraft draft) => new(draft, ProjectUpdateBuilder.Checklist(draft));

    private async Task<IReadOnlyDictionary<string, string>> VersionsAsync(Guid ownerId, IReadOnlyList<DocSection> sections, CancellationToken ct)
    {
        var refs = sections.SelectMany(s => s.Refs).Distinct().Take(40).ToArray();
        var current = await sourceReader!.ReadAsync(ownerId, null, refs, ct);
        return sections.SelectMany(s => s.Refs.Select(reference => new { Key = s.Id + "|" + reference,
                Version = current.FirstOrDefault(c => c.Ref == reference)?.Version ?? "missing" }))
            .DistinctBy(s => s.Key).ToDictionary(s => s.Key, s => s.Version);
    }

    private async Task<DocumentView> CurrentViewAsync(Guid ownerId, DocDraft draft, CancellationToken ct)
    {
        if (sourceReader is null || draft.SourceVersions is null) return ViewOf(draft);
        var current = await VersionsAsync(ownerId, draft.Sections, ct);
        var changed = current.Where(pair => draft.SourceVersions.TryGetValue(pair.Key, out var old) && old != pair.Value)
            .Select(pair => pair.Key[(pair.Key.IndexOf('|') + 1)..]).Distinct().ToArray();
        return new(draft, ProjectUpdateBuilder.Checklist(draft), changed.Length > 0, changed);
    }

    private async Task<IReadOnlyList<DocDraft>> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await settings.GetAsync<DocumentState>(ownerId, DocumentState.Section, cancellationToken))?.Drafts ?? [];

    private Task SaveStateAsync(Guid ownerId, IReadOnlyList<DocDraft> drafts, CancellationToken cancellationToken) =>
        settings.SaveAsync(ownerId, DocumentState.Section, new DocumentState(drafts), cancellationToken);

    private async Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zone = (await Preferences.OwnerZoneResolver.ResolveAsync(settings, briefings, ownerId, cancellationToken)).Zone;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }
}
