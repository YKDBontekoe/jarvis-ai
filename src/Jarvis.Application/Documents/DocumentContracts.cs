using System.Text;
using Jarvis.Application.Inbox;
using Jarvis.Application.Library;
using Jarvis.Application.Projects;
using Jarvis.Domain.Inbox;
using Jarvis.Domain.Library;

namespace Jarvis.Application.Documents;

public static class DocumentKinds
{
    public const string ProjectUpdate = "project_update";
}

public static class DocumentRules
{
    public const int MaxDrafts = 20;
    public const int MaxBody = 4_000;
    public const int MaxSources = 3;
    public const int MaxPromises = 6;
    public const string Placeholder = "[ADD";

    public static readonly string[] Audiences = ["team", "client", "self"];
    public static readonly string[] Languages = ["en", "nl"];
}

/// <summary>
/// One section of a draft. <see cref="Refs"/> say where its facts came from (<c>project:</c>, <c>commitment:</c>,
/// <c>library:</c>); <see cref="Previous"/> keeps the text before the last change so it can be undone.
/// </summary>
public sealed record DocSection(
    string Id,
    string Heading,
    string Body,
    IReadOnlyList<string> Refs,
    bool Edited = false,
    string? Previous = null);

public sealed record DocDraft(
    Guid Id,
    string Title,
    string Kind,
    Guid ProjectId,
    string Audience,
    string Language,
    IReadOnlyList<DocSection> Sections,
    IReadOnlyList<string> Unknowns,
    int Version,
    int SavedVersion,
    Guid? LibraryItemId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Owner settings section <see cref="Section"/>: drafts, newest first.</summary>
public sealed record DocumentState(IReadOnlyList<DocDraft> Drafts)
{
    public const string Section = "documents.drafts";
}

public sealed record DocumentRequest(string? Kind, Guid? ProjectId, string? Audience, string? Language);

public sealed record SectionEditRequest(string? Body);

public sealed record SectionReviseRequest(string? Action);

public sealed record ChecklistItem(string Text, bool Ok);

public sealed record DocumentView(DocDraft Draft, IReadOnlyList<ChecklistItem> Checklist);

public sealed record DocumentExport(string FileName, string ContentType, string Content);

/// <summary>What a project update is built from, gathered by the service so the builder stays pure.</summary>
public sealed record ProjectUpdateSources(
    ProjectRecord Project,
    string? NextAction,
    IReadOnlyList<Commitment> Promises,
    IReadOnlyList<LibraryItem> Notes);

/// <summary>
/// Builds the sections of a one-page project update from the owner's own records. Unknowns stay visible as
/// <c>[ADD: …]</c> placeholders and in the open-questions list; nothing is invented.
/// </summary>
public static class ProjectUpdateBuilder
{
    private static readonly Dictionary<string, string[]> Headings = new()
    {
        ["en"] = ["Where we are", "Next step", "Open promises", "Background", "Open questions"],
        ["nl"] = ["Waar we staan", "Volgende stap", "Openstaande beloftes", "Achtergrond", "Open vragen"]
    };

    public static (IReadOnlyList<DocSection> Sections, IReadOnlyList<string> Unknowns) Build(
        ProjectUpdateSources sources, string audience, string language, DateOnly today)
    {
        var h = Headings.GetValueOrDefault(language, Headings["en"]);
        var project = sources.Project;
        var unknowns = new List<string>();
        var projectRef = $"project:{project.Id}";

        string where;
        if (string.IsNullOrWhiteSpace(project.Outcome))
        {
            unknowns.Add("What this project is for (no outcome is set).");
            where = $"{project.Name}: [ADD: what this project is for]";
        }
        else where = $"{project.Name}: {project.Outcome} (status: {project.Status}).";
        if (audience == "client") where += language == "nl" ? " Dit is een tussenstand." : " This is a progress update.";

        var next = string.IsNullOrWhiteSpace(sources.NextAction) ? null : sources.NextAction;
        if (next is null) unknowns.Add("The next step (none is set).");

        var promiseLines = sources.Promises.Take(DocumentRules.MaxPromises).Select(p =>
            (p.Direction == CommitmentDirections.IOwe ? "We owe " : "Owed to us by ") + p.Counterparty + ": " +
            p.Description + (p.DueOn is { } due ? $" (due {due:d MMM yyyy}{(due < today ? ", late" : "")})" : "")).ToArray();
        if (promiseLines.Length == 0) unknowns.Add("Open promises (none are linked to this project).");

        var notes = sources.Notes.Take(DocumentRules.MaxSources).ToArray();
        if (notes.Length == 0) unknowns.Add("Background sources (nothing is saved for this project).");

        var sections = new List<DocSection>
        {
            new("where", h[0], where, [projectRef]),
            new("next", h[1], next ?? "[ADD: the next step]", next is null ? [] : [projectRef]),
            new("promises", h[2],
                promiseLines.Length == 0 ? "[ADD: promises to mention, if any]" : string.Join("\n", promiseLines.Select(l => "- " + l)),
                sources.Promises.Take(DocumentRules.MaxPromises).Select(p => $"commitment:{p.Id}").ToArray()),
            new("background", h[3],
                notes.Length == 0 ? "[ADD: a source to cite]" : string.Join("\n", notes.Select(n => $"- {n.Title}: {n.Summary}")),
                notes.Select(n => $"library:{n.Id}").ToArray()),
            new("questions", h[4],
                unknowns.Count == 0 ? "None." : string.Join("\n", unknowns.Select(u => "- " + u)), [])
        };
        return (sections, unknowns);
    }

    /// <summary>The review list for this artifact: specific, short, and about what could go wrong when it is shared.</summary>
    public static IReadOnlyList<ChecklistItem> Checklist(DocDraft draft)
    {
        var placeholders = draft.Sections.Count(s => s.Body.Contains(DocumentRules.Placeholder, StringComparison.Ordinal));
        var uncited = draft.Sections.Where(s => s.Id != "questions" && s.Refs.Count == 0 &&
                                                !s.Body.Contains(DocumentRules.Placeholder, StringComparison.Ordinal))
            .Select(s => s.Heading).ToArray();
        var names = draft.Sections.Any(s => s.Id == "promises" && s.Refs.Count > 0);
        return
        [
            new(placeholders == 0 ? "No placeholders left." : $"{placeholders} section(s) still have [ADD…] placeholders to fill or remove.",
                placeholders == 0),
            new(uncited.Length == 0 ? "Every filled section cites where it came from." : $"No source for: {string.Join(", ", uncited)}.",
                uncited.Length == 0),
            new(names ? "Names of people appear in the promises: check they should be seen by this audience." : "No people are named.", !names),
            new("Saving or exporting does not send anything. Sharing is a separate step you take yourself.", true)
        ];
    }

    public static string ToMarkdown(DocDraft draft)
    {
        var text = new StringBuilder();
        text.Append("# ").AppendLine(draft.Title);
        text.AppendLine().AppendLine($"_Version {draft.Version}. Built from your own records; nothing was sent._");
        foreach (var section in draft.Sections)
        {
            text.AppendLine().Append("## ").AppendLine(section.Heading).AppendLine().AppendLine(section.Body);
            if (section.Refs.Count > 0)
                text.AppendLine().Append("_Sources: ").Append(string.Join(", ", section.Refs)).AppendLine("_");
        }
        return text.ToString().TrimEnd();
    }

    public static string ToPlainText(DocDraft draft)
    {
        var text = new StringBuilder();
        text.AppendLine(draft.Title.ToUpperInvariant()).AppendLine();
        foreach (var section in draft.Sections)
        {
            text.AppendLine(section.Heading).AppendLine(new string('-', section.Heading.Length)).AppendLine(section.Body);
            if (section.Refs.Count > 0) text.Append("Sources: ").AppendLine(string.Join(", ", section.Refs));
            text.AppendLine();
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>Keeps the first two lines (or two sentences) of a section.</summary>
    public static string Shorten(string body)
    {
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 2) return string.Join("\n", lines.Take(2));
        if (lines.Length == 1)
        {
            var parts = lines[0].Split(". ", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2) return string.Join(". ", parts.Take(2)).TrimEnd('.') + ".";
        }
        return body;
    }
}

public enum DocumentFailure
{
    None,
    NotFound,
    Invalid,
    Conflict
}

public sealed record DocumentOperation<T>(T? Value, DocumentFailure Failure = DocumentFailure.None,
    string? Field = null, string? Message = null)
{
    public bool Succeeded => Failure == DocumentFailure.None;
    public static DocumentOperation<T> Ok(T value) => new(value);
    public static DocumentOperation<T> NotFound() => new(default, DocumentFailure.NotFound);
    public static DocumentOperation<T> Invalid(string field, string message) =>
        new(default, DocumentFailure.Invalid, field, message);
    public static DocumentOperation<T> Conflict(string field, string message) =>
        new(default, DocumentFailure.Conflict, field, message);
}

public interface IDocumentService
{
    Task<IReadOnlyList<DocDraft>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<DocumentView?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<DocumentOperation<DocumentView>> CreateAsync(Guid ownerId, DocumentRequest request,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);

    /// <summary>Replaces one section's text with the owner's words. The rest of the draft is untouched.</summary>
    Task<DocumentOperation<DocumentView>> EditSectionAsync(Guid ownerId, Guid id, string sectionId, string? body,
        CancellationToken cancellationToken);

    /// <summary><c>shorten</c> or <c>rebuild</c> (from the sources again) one section; the previous text is kept.</summary>
    Task<DocumentOperation<DocumentView>> ReviseSectionAsync(Guid ownerId, Guid id, string sectionId, string? action,
        CancellationToken cancellationToken);

    Task<DocumentOperation<DocumentView>> UndoSectionAsync(Guid ownerId, Guid id, string sectionId,
        CancellationToken cancellationToken);

    /// <summary>Saves this version to the Library as a report in the project. A new version is a new item.</summary>
    Task<DocumentOperation<DocumentView>> SaveAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);

    Task<DocumentOperation<DocumentExport>> ExportAsync(Guid ownerId, Guid id, string? format,
        CancellationToken cancellationToken);
}
