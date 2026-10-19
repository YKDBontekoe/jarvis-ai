using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Documents;

namespace Jarvis.Api.Endpoints;

/// <summary>
/// The document studio's first artifact: a one-page project update built from the owner's own records, with
/// editable sections, reversible changes, a cited Library save and a file export. Creating, saving and exporting
/// never send or share anything.
/// </summary>
internal static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/documents").WithTags("Documents");

        group.MapGet("", async (IDocumentService documents, ICurrentUser user, CancellationToken ct) =>
            Results.Ok((await documents.ListAsync(user.OwnerId, ct)).Select(draft => new
            {
                draft.Id, draft.Title, draft.Kind, draft.ProjectId, draft.Version, draft.SavedVersion, draft.UpdatedAt
            }))).WithName("ListDocuments");

        group.MapPost("", async (DocumentRequest request, IDocumentService documents, ICurrentUser user,
            CancellationToken ct) =>
        {
            var created = await documents.CreateAsync(user.OwnerId, request, ct);
            return created.Succeeded
                ? Results.Created($"/api/v1/documents/{created.Value!.Draft.Id}", created.Value)
                : Failure(created);
        }).WithName("CreateDocument");

        group.MapGet("/{id:guid}", async (Guid id, IDocumentService documents, ICurrentUser user,
            CancellationToken ct) =>
            await documents.GetAsync(user.OwnerId, id, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .WithName("GetDocument");

        group.MapDelete("/{id:guid}", async (Guid id, IDocumentService documents, ICurrentUser user,
            CancellationToken ct) =>
            await documents.DeleteAsync(user.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteDocument");

        group.MapPut("/{id:guid}/sections/{sectionId}", async (Guid id, string sectionId, SectionEditRequest request,
            IDocumentService documents, ICurrentUser user, CancellationToken ct) =>
            Done(await documents.EditSectionAsync(user.OwnerId, id, sectionId, request.Body, ct)))
            .WithName("EditDocumentSection");

        group.MapPost("/{id:guid}/sections/{sectionId}/revise", async (Guid id, string sectionId,
            SectionReviseRequest request, IDocumentService documents, ICurrentUser user, CancellationToken ct) =>
            Done(await documents.ReviseSectionAsync(user.OwnerId, id, sectionId, request.Action, ct)))
            .WithName("ReviseDocumentSection");

        group.MapPost("/{id:guid}/sections/{sectionId}/undo", async (Guid id, string sectionId,
            IDocumentService documents, ICurrentUser user, CancellationToken ct) =>
            Done(await documents.UndoSectionAsync(user.OwnerId, id, sectionId, ct)))
            .WithName("UndoDocumentSection");

        group.MapPost("/{id:guid}/save", async (Guid id, IDocumentService documents, ICurrentUser user,
            CancellationToken ct) => Done(await documents.SaveAsync(user.OwnerId, id, ct)))
            .WithName("SaveDocument");

        group.MapGet("/{id:guid}/export", async (Guid id, string? format, IDocumentService documents,
            ICurrentUser user, CancellationToken ct) =>
        {
            var export = await documents.ExportAsync(user.OwnerId, id, format, ct);
            return export.Succeeded
                ? Results.File(Encoding.UTF8.GetBytes(export.Value!.Content), export.Value.ContentType,
                    export.Value.FileName)
                : Failure(export);
        }).WithName("ExportDocument");

        return api;
    }

    private static IResult Done(DocumentOperation<DocumentView> operation) =>
        operation.Succeeded ? Results.Ok(operation.Value) : Failure(operation);

    private static IResult Failure<T>(DocumentOperation<T> operation) => operation.Failure switch
    {
        DocumentFailure.NotFound => Results.NotFound(),
        DocumentFailure.Conflict => Results.Conflict(new { message = operation.Message }),
        _ => EndpointHelpers.Invalid(operation.Field ?? "document", operation.Message ?? "The request is invalid.")
    };
}
