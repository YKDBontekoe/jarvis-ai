using System.Text.Json;
using Jarvis.Application.Audit;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;

namespace Jarvis.Api.Endpoints;

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
                new FileSearchHitDto(hit.FileId, hit.FileName, hit.ChunkIndex, hit.Content, hit.Score)));
        }).WithName("SearchFiles");

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
                    return Results.UnprocessableEntity(new { message = "The uploaded file was rejected by malware scanning." });
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Could not store an uploaded file for user {OwnerId}.", currentUser.OwnerId);
                    return Results.Problem("File storage is temporarily unavailable.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
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

        return api;
    }
}
