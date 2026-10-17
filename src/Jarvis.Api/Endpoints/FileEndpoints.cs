using System.Text.Json;
using Jarvis.Api.Errors;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Profiles;

namespace Jarvis.Api.Endpoints;

public sealed record AskAboutFileRequest(Guid? ProfileId = null);

/// <summary><see cref="Status"/> is the file's indexing status: a file that is not ready yet answers poorly.</summary>
public sealed record AskAboutFileDto(Guid ConversationId, Guid FileId, string FileName, string Status);

internal static class FileEndpoints
{
    public static RouteGroupBuilder MapFileEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        api.MapGet("/files", async (IFileService files, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await files.ListAsync(currentUser.OwnerId, ct)).Select(file => file.ToDto())))
            .WithName("ListFiles");

        api.MapGet("/files/search", async (string? query, IFileSearchService files, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length > 2_000)
                return EndpointHelpers.Invalid("query", "Query must contain 1 to 2,000 characters.");
            var hits = await files.SearchAsync(currentUser.OwnerId, query, ct);
            return Results.Ok(hits.Select(hit =>
                new FileSearchHitDto(hit.FileId, hit.FileName, hit.ChunkId, hit.ChunkIndex, hit.SanitizedExcerpt,
                    hit.Score, hit.ExcerptStart, hit.ExcerptEnd, hit.PageNumber, hit.SanitizedExcerpt)));
        }).WithName("SearchFiles");

        // "Ask Jarvis about this": a new chat with the file already attached as its only source.
        api.MapPost("/files/{id:guid}/ask", async (Guid id, AskAboutFileRequest? request, IFileService files,
            IConversationStore conversations, IConversationFileContextRepository sources,
            IDocumentCollectionRepository collections, IAssistantProfileService profiles,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var file = await files.GetAsync(id, currentUser.OwnerId, ct);
            if (file is null || file.ProcessingStatus == "deleting") return Results.NotFound();
            ProfileBinding binding;
            try
            {
                binding = await profiles.CaptureBindingAsync(currentUser.OwnerId, request?.ProfileId, ct);
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("profileId", exception.Message);
            }
            // A profile that limits files to some collections must not be widened by attaching a file outside them.
            var snapshot = await profiles.ResolveSnapshotAsync(binding, currentUser.OwnerId, ct);
            if (snapshot.RestrictFiles)
            {
                var allowed = await collections.ListFileIdsAsync(currentUser.OwnerId, snapshot.AllowedCollectionIds, ct);
                if (!allowed.Contains(file.Id))
                    return Results.Conflict(new { message = "That assistant profile cannot read this file." });
            }

            var name = file.FileName.Length <= 80 ? file.FileName : file.FileName[..79] + "…";
            var conversation = await conversations.CreateAsync(currentUser.OwnerId, "About " + name, ct, binding);
            if (!await sources.AttachFileAsync(conversation.Id, file.Id, currentUser.OwnerId, ct))
            {
                await conversations.DeleteAsync(conversation.Id, currentUser.OwnerId, ct);
                return Results.NotFound();
            }
            return Results.Created($"/api/v1/conversations/{conversation.Id}",
                new AskAboutFileDto(conversation.Id, file.Id, file.FileName, file.ProcessingStatus));
        }).WithName("AskAboutFile");

        api.MapPost("/files/{id:guid}/reprocess", async (Guid id, IFileService files, ICurrentUser currentUser,
                CancellationToken ct) =>
            await files.RetryIndexingAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("RetryFileIndexing");

        api.MapPost("/files", async (IFormFile? file, IFileService files, IAuditEventStore audit,
                ICurrentUser currentUser, CancellationToken ct) =>
            {
                if (file is null) return EndpointHelpers.Invalid("file", "Choose a file to upload.");
                await using var content = file.OpenReadStream();
                try
                {
                    var stored = await files.UploadAsync(currentUser.OwnerId, file.FileName, file.ContentType,
                        file.Length, content, ct);
                    await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "files",
                        "file.uploaded", "moderate", true, null,
                        JsonSerializer.Serialize(new { resourceId = stored.Id }), ct);
                    return Results.Created($"/api/v1/files/{stored.Id}", stored.ToDto());
                }
                catch (ArgumentException exception)
                {
                    return EndpointHelpers.Invalid("file", exception.Message);
                }
                catch (MalwareDetectedException)
                {
                    await EndpointHelpers.TryAppendAuditAsync(audit, logger, currentUser.OwnerId, "files",
                        "file.malware_rejected", "high", false, null, null, ct);
                    return ApiProblemResults.MalwareRejected();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Could not store an uploaded file for user {OwnerId}.", currentUser.OwnerId);
                    return ApiProblemResults.DependencyUnavailable("File storage is temporarily unavailable.");
                }
            })
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(22 * 1024 * 1024))
            .WithName("UploadFile");

        api.MapGet("/files/{id:guid}/content", async (Guid id, IFileService files, ICurrentUser currentUser,
            HttpResponse response, CancellationToken ct) =>
        {
            var download = await files.OpenReadAsync(id, currentUser.OwnerId, ct);
            if (download is null) return Results.NotFound();
            response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(download.Value.Content, download.Value.File.ContentType,
                download.Value.File.FileName, enableRangeProcessing: false);
        }).WithName("DownloadFile");

        api.MapDelete("/files/{id:guid}", async (Guid id, IFileService files,
                ICurrentUser currentUser, CancellationToken ct) =>
            await files.DeleteAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteFile");

        api.MapGet("/files/citations/{chunkId:guid}", async (Guid chunkId, IFileCitationResolver citations,
                ICurrentUser currentUser, CancellationToken ct) =>
            {
                var citation = await citations.ResolveAsync(chunkId, currentUser.OwnerId, ct);
                return citation is null ? Results.NotFound() : Results.Ok(citation.ToDto());
            })
            .WithName("ResolveFileCitation");

        return api;
    }
}
