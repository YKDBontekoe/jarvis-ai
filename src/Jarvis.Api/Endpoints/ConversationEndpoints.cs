using System.Globalization;
using System.Text;
using Jarvis.Api.Conversations;
using Jarvis.Api.Errors;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Profiles;
using Jarvis.Application.Projects;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;

namespace Jarvis.Api.Endpoints;

internal static class ConversationEndpoints
{
    public static RouteGroupBuilder MapConversationEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/conversations", async (IConversationStore store, IAssistantProfileService profiles,
            IProjectStore projects, ICurrentUser currentUser, CreateConversationRequest request, CancellationToken ct) =>
        {
            var title = string.IsNullOrWhiteSpace(request.Title) ? "New conversation" : request.Title.Trim();
            if (title.Length > 200) return EndpointHelpers.Invalid("title", "Title must be 200 characters or fewer.");
            if (request.ProjectId is { } projectId && await projects.GetAsync(projectId, currentUser.OwnerId, ct) is null)
                return EndpointHelpers.Invalid("projectId", "Project was not found.");
            try
            {
                var binding = await profiles.CaptureBindingAsync(currentUser.OwnerId, request.ProfileId, ct);
                var conversation = await store.CreateAsync(currentUser.OwnerId, title, ct, binding);
                if (request.ProjectId is { } project)
                {
                    await projects.AssignConversationAsync(conversation.Id, project, currentUser.OwnerId, ct);
                    conversation.MoveToProject(project);
                }
                return Results.Created($"/api/v1/conversations/{conversation.Id}",
                    ToDto(conversation, binding.Name, false));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("profileId", exception.Message);
            }
        }).WithName("CreateConversation");

        api.MapGet("/conversations", async (IConversationStore store, IAssistantProfileService profiles,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            var live = (await profiles.ListAsync(currentUser.OwnerId, ct)).ToDictionary(item => item.Id);
            return Results.Ok((await store.ListAsync(currentUser.OwnerId, ct))
                .Select(item => ToDto(item, live)));
        }).WithName("ListConversations");

        api.MapGet("/conversations/{conversationId:guid}", async (Guid conversationId, bool? includeMessages,
            IConversationStore store, IAssistantProfileService profiles,
            IJarvisTaskRepository tasks, ICurrentUser currentUser, RemoteQueryHost queries, CancellationToken ct) =>
        {
            var conversation = await store.GetAsync(conversationId, currentUser.OwnerId, ct);
            if (conversation is null) return Results.NotFound();
            if (await tasks.GetTaskByConversationIdAsync(conversationId, currentUser.OwnerId, ct) is not null)
                return Results.NotFound();
            IReadOnlyList<Jarvis.Domain.Conversations.Message> messages = includeMessages == false
                ? Array.Empty<Jarvis.Domain.Conversations.Message>()
                : await store.GetMessagesAsync(conversationId, ct);
            var live = (await profiles.ListAsync(currentUser.OwnerId, ct)).ToDictionary(item => item.Id);
            return Results.Ok(new ConversationDetailsDto(conversation.Id, conversation.Title, conversation.CreatedAt,
                conversation.UpdatedAt, messages.Select(message => message.ToDto()).ToArray(),
                queries.IsResponding(conversationId), conversation.ProfileId, ProfileName(conversation, live),
                conversation.ProfileVersion, ProfileDeleted(conversation, live), conversation.PinnedAt is not null,
                conversation.ProjectId));
        }).WithName("GetConversation");

        api.MapPatch("/conversations/{conversationId:guid}", async (Guid conversationId,
            UpdateConversationRequest request, IConversationStore store, IAssistantProfileService profiles,
            IJarvisTaskRepository tasks, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (request.Title is null && request.Pinned is null)
                return EndpointHelpers.Invalid("title", "Send a title, pinned, or both.");
            if (request.Title is not null && string.IsNullOrWhiteSpace(request.Title))
                return EndpointHelpers.Invalid("title", "Title is required.");
            if (request.Title is { Length: > Conversation.MaximumTitleLength })
                return EndpointHelpers.Invalid("title", "Title must be 200 characters or fewer.");
            if (await tasks.GetTaskByConversationIdAsync(conversationId, currentUser.OwnerId, ct) is not null)
                return Results.NotFound();
            var conversation = await store.UpdateAsync(conversationId, currentUser.OwnerId, request.Title,
                request.Pinned, ct);
            if (conversation is null) return Results.NotFound();
            var live = (await profiles.ListAsync(currentUser.OwnerId, ct)).ToDictionary(item => item.Id);
            return Results.Ok(ToDto(conversation, live));
        }).WithName("UpdateConversation");

        api.MapGet("/conversations/{conversationId:guid}/messages", async (Guid conversationId, int? limit,
            string? cursor, IConversationStore store, IJarvisTaskRepository tasks, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            const int defaultLimit = 50;
            const int maximumLimit = MessagePage.MaximumSize;
            var pageSize = limit ?? defaultLimit;
            if (pageSize is < 1 or > maximumLimit)
                return EndpointHelpers.Invalid("limit", $"Limit must be between 1 and {maximumLimit}.");
            if (!TryDecodeCursor(cursor, out var before))
                return EndpointHelpers.Invalid("cursor", "Cursor is invalid.");

            var conversation = await store.GetAsync(conversationId, currentUser.OwnerId, ct);
            if (conversation is null ||
                await tasks.GetTaskByConversationIdAsync(conversationId, currentUser.OwnerId, ct) is not null)
                return Results.NotFound();

            var page = await store.GetMessagePageAsync(conversationId, before, pageSize, ct);
            return Results.Ok(new MessagePageDto(page.Items.Select(message => message.ToDto()).ToArray(),
                page.NextCursor is null ? null : EncodeCursor(page.NextCursor), page.HasMore));
        }).WithName("ListConversationMessages");

        api.MapDelete("/conversations/{conversationId:guid}", async (Guid conversationId,
            IConversationStore store, IConversationRunLock runLock, ICurrentUser currentUser, CancellationToken ct) =>
        {
            await using var lease = await runLock.AcquireAsync(conversationId, ct);
            var result = await store.DeleteAsync(conversationId, currentUser.OwnerId, ct);
            if (result == ConversationDeleteResult.NotFound) return Results.NotFound();
            if (result == ConversationDeleteResult.TaskBacked)
                return Results.Conflict(new { message = "Task conversations are managed from the Tasks section." });
            return Results.NoContent();
        }).WithName("DeleteConversation");

        api.MapPost("/conversations/{conversationId:guid}/messages", async (Guid conversationId,
            SendMessageRequest request, RemoteQueryExecutor remote, IFileService files, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var content = request.Content?.Trim();
            var imageIds = request.ImageFileIds?.Distinct().ToArray() ?? [];
            if (imageIds.Length > MessageAttachments.MaxPerMessage)
                return EndpointHelpers.Invalid("imageFileIds",
                    $"Send at most {MessageAttachments.MaxPerMessage} photos with one message.");
            if (content is { Length: > 32_000 } || string.IsNullOrWhiteSpace(content) && imageIds.Length == 0)
                return EndpointHelpers.Invalid("content", "Message content must contain 1 to 32,000 characters.");
            var attachments = new List<MessageAttachment>(imageIds.Length);
            foreach (var imageId in imageIds)
            {
                var file = await files.GetAsync(imageId, currentUser.OwnerId, ct);
                if (file is null)
                    return EndpointHelpers.Invalid("imageFileIds", "A photo could not be found. Upload it again.");
                if (!MessageAttachments.IsSupportedImage(file.ContentType))
                    return EndpointHelpers.Invalid("imageFileIds", "Only JPEG, PNG, and WebP photos can be sent.");
                if (file.SizeBytes > MessageAttachments.MaxImageBytes)
                    return EndpointHelpers.Invalid("imageFileIds", "A photo is larger than 8 MB.");
                attachments.Add(new MessageAttachment(file.Id, file.FileName, file.ContentType));
            }
            var text = string.IsNullOrWhiteSpace(content)
                ? MessageAttachments.DefaultContent(attachments.Count)
                : content;
            // The request abort token is intentionally unused. Closing the phone drops this connection,
            // and the query still runs until it is stored.
            return await RemoteQueryResults.ExecuteAsync(() =>
                remote.SendAsync(currentUser.OwnerId, conversationId, text, attachments: attachments));
        }).WithName("SendMessage");

        api.MapPost("/conversations/{conversationId:guid}/regenerate", async (Guid conversationId,
            RemoteQueryExecutor remote, ICurrentUser currentUser) =>
            await RemoteQueryResults.ExecuteAsync(() => remote.RegenerateAsync(currentUser.OwnerId, conversationId)))
            .WithName("RegenerateReply");

        api.MapPost("/conversations/{conversationId:guid}/summary", async (Guid conversationId,
            IConversationStore store, IConversationSummarizer summarizer, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (await store.GetAsync(conversationId, currentUser.OwnerId, ct) is null)
                return Results.NotFound();
            var messages = await store.GetMessagesAsync(conversationId, ct);
            if (ConversationSummaries.CountSpeakerMessages(messages) < ConversationSummaries.MinimumMessages)
                return ApiProblemResults.Conflict("This conversation is too short to summarize.");
            var summary = await summarizer.SummarizeAsync(currentUser.OwnerId, messages, ct);
            return summary is null
                ? ApiProblemResults.DependencyUnavailable("Jarvis could not summarize this conversation right now.")
                : Results.Ok(new ConversationSummaryDto(summary.Summary, summary.KeyPoints, summary.ActionItems,
                    summary.MessageCount));
        }).WithName("SummarizeConversation");

        api.MapPost("/conversations/{conversationId:guid}/cancel", async (Guid conversationId,
            IConversationStore store, ICurrentUser currentUser, RemoteQueryHost queries, CancellationToken ct) =>
        {
            if (await store.GetAsync(conversationId, currentUser.OwnerId, ct) is null)
                return Results.NotFound();
            queries.TryCancel(currentUser.OwnerId, conversationId);
            return Results.NoContent();
        }).WithName("CancelConversationRun");

        api.MapGet("/conversations/{conversationId:guid}/sources", async (Guid conversationId,
                IConversationStore store, IConversationFileContextRepository sources, IDocumentCollectionRepository collections,
                IFileRepository files, ICurrentUser currentUser, CancellationToken ct) =>
            {
                if (await store.GetAsync(conversationId, currentUser.OwnerId, ct) is null)
                    return Results.NotFound();
                var attached = await sources.GetSourcesAsync(conversationId, currentUser.OwnerId, ct);
                var fileDtos = new List<ConversationFileSourceDto>();
                foreach (var file in attached.Files)
                {
                    var stored = await files.GetAsync(file.FileId, currentUser.OwnerId, ct);
                    if (stored is null) continue;
                    fileDtos.Add(new ConversationFileSourceDto(stored.Id, stored.FileName, stored.ProcessingStatus,
                        file.AttachedAt));
                }

                var collectionDtos = new List<ConversationCollectionSourceDto>();
                foreach (var collection in attached.Collections)
                {
                    var record = await collections.GetAsync(collection.CollectionId, currentUser.OwnerId, ct);
                    if (record is null) continue;
                    collectionDtos.Add(new ConversationCollectionSourceDto(record.Id, record.Name, collection.AttachedAt));
                }

                return Results.Ok(new ConversationSourcesDto(fileDtos, collectionDtos));
            })
            .WithName("GetConversationSources");

        api.MapPost("/conversations/{conversationId:guid}/sources/files/{fileId:guid}",
                async (Guid conversationId, Guid fileId, IConversationFileContextRepository sources,
                    ICurrentUser currentUser, CancellationToken ct) =>
                    await sources.AttachFileAsync(conversationId, fileId, currentUser.OwnerId, ct)
                        ? Results.NoContent()
                        : Results.NotFound())
            .WithName("AttachConversationFile");

        api.MapDelete("/conversations/{conversationId:guid}/sources/files/{fileId:guid}",
                async (Guid conversationId, Guid fileId, IConversationFileContextRepository sources,
                    ICurrentUser currentUser, CancellationToken ct) =>
                    await sources.DetachFileAsync(conversationId, fileId, currentUser.OwnerId, ct)
                        ? Results.NoContent()
                        : Results.NotFound())
            .WithName("DetachConversationFile");

        api.MapPost("/conversations/{conversationId:guid}/sources/collections/{collectionId:guid}",
                async (Guid conversationId, Guid collectionId, IConversationFileContextRepository sources,
                    ICurrentUser currentUser, CancellationToken ct) =>
                    await sources.AttachCollectionAsync(conversationId, collectionId, currentUser.OwnerId, ct)
                        ? Results.NoContent()
                        : Results.NotFound())
            .WithName("AttachConversationCollection");

        api.MapDelete("/conversations/{conversationId:guid}/sources/collections/{collectionId:guid}",
                async (Guid conversationId, Guid collectionId, IConversationFileContextRepository sources,
                    ICurrentUser currentUser, CancellationToken ct) =>
                    await sources.DetachCollectionAsync(conversationId, collectionId, currentUser.OwnerId, ct)
                        ? Results.NoContent()
                        : Results.NotFound())
            .WithName("DetachConversationCollection");

        api.MapPut("/conversations/{conversationId:guid}/profile", async (Guid conversationId,
            SwitchConversationProfileRequest request, IConversationStore store, IAssistantProfileService profiles,
            IJarvisTaskRepository tasks, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var conversation = await store.GetAsync(conversationId, currentUser.OwnerId, ct);
            if (conversation is null ||
                await tasks.GetTaskByConversationIdAsync(conversationId, currentUser.OwnerId, ct) is not null)
                return Results.NotFound();
            try
            {
                var currentBinding = conversation.ProfileId is { } currentId
                    ? new ProfileBinding(currentId, conversation.ProfileVersion ?? 0,
                        conversation.ProfileSnapshotJson ?? "", conversation.Title)
                    : null;
                var current = await profiles.ResolveSnapshotAsync(currentBinding, currentUser.OwnerId, ct);
                var nextBinding = await profiles.CaptureBindingAsync(currentUser.OwnerId, request.ProfileId, ct);
                var next = ProfileJson.Deserialize(nextBinding.SnapshotJson)!;
                var change = profiles.CompareScope(current, next);
                if (change.RequiresConfirmation && !request.Confirm)
                    return Results.Json(new
                    {
                        message = "Switching this profile changes tools or knowledge scope. Confirm to continue.",
                        skillsChanged = change.SkillsChanged,
                        filesChanged = change.FilesChanged,
                        memoryChanged = change.MemoryChanged,
                        details = change.Details
                    }, statusCode: StatusCodes.Status409Conflict);
                await store.BindProfileAsync(conversationId, currentUser.OwnerId, nextBinding, ct);
                conversation = await store.GetAsync(conversationId, currentUser.OwnerId, ct);
                return Results.Ok(ToDto(conversation!, nextBinding.Name, false));
            }
            catch (ArgumentException exception)
            {
                return EndpointHelpers.Invalid("profileId", exception.Message);
            }
        }).WithName("SwitchConversationProfile");

        return api;
    }

    private static string EncodeCursor(MessageCursor cursor)
    {
        var value = $"1|{cursor.CreatedAt.ToUniversalTime():O}|{cursor.Id:D}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string? encoded, out MessageCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(encoded)) return true;
        if (encoded.Length > 256) return false;
        try
        {
            var value = encoded.Replace('-', '+').Replace('_', '/');
            value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split('|');
            if (parts.Length != 3 || parts[0] != "1" ||
                !DateTimeOffset.TryParseExact(parts[1], "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var createdAt) ||
                !Guid.TryParseExact(parts[2], "D", out var id))
                return false;
            cursor = new MessageCursor(createdAt, id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static ConversationDto ToDto(Conversation conversation, IReadOnlyDictionary<Guid, AssistantProfileRecord> live)
    {
        var snapshot = ProfileJson.Deserialize(conversation.ProfileSnapshotJson);
        var profileId = conversation.ProfileId ?? snapshot?.ProfileId;
        var name = snapshot?.Name;
        if (profileId is { } id && live.TryGetValue(id, out var liveProfile) && name is null)
            name = liveProfile.Name;
        if (name is null && snapshot is not null) name = snapshot.Name;
        var deleted = profileId is { } missing && !live.ContainsKey(missing);
        return new ConversationDto(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt,
            profileId, name, conversation.ProfileVersion ?? snapshot?.Version, deleted, conversation.PinnedAt is not null,
            conversation.ProjectId);
    }

    private static ConversationDto ToDto(Conversation conversation, string name, bool deleted) =>
        new(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt,
            conversation.ProfileId, name, conversation.ProfileVersion, deleted, conversation.PinnedAt is not null,
            conversation.ProjectId);

    private static string? ProfileName(Conversation conversation, IReadOnlyDictionary<Guid, AssistantProfileRecord> live) =>
        ToDto(conversation, live).ProfileName;

    private static bool ProfileDeleted(Conversation conversation, IReadOnlyDictionary<Guid, AssistantProfileRecord> live) =>
        ToDto(conversation, live).ProfileDeleted;

    public static RouteGroupBuilder MapApprovalEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/approvals", async (IToolApprovalStore approvals, ICurrentUser currentUser, CancellationToken ct) =>
                Results.Ok((await approvals.ListActionableAsync(currentUser.OwnerId, ct)).ToDtos()))
            .WithName("ListPendingApprovals");

        api.MapGet("/approvals/standing", async (IStandingApprovalService standing, ICurrentUser currentUser,
                CancellationToken ct) =>
            Results.Ok((await standing.ListAsync(currentUser.OwnerId, ct))
                .Select(grant => new StandingApprovalDto(grant.Category, grant.Label, grant.GrantedAt, grant.ExpiresAt,
                    grant.Scope))))
            .WithName("ListStandingApprovals");

        api.MapDelete("/approvals/standing", async (string? category, IStandingApprovalService standing,
                ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(category) || !ApprovalCategories.IsSafeKey(category))
                return EndpointHelpers.Invalid("category", "Choose an always-allowed action to turn off.");
            return await standing.RevokeAsync(currentUser.OwnerId, category, ct)
                ? Results.NoContent()
                : Results.NotFound();
        }).WithName("RevokeStandingApproval");

        api.MapPost("/approvals/{approvalId:guid}/decision", async (Guid approvalId, ApprovalDecisionRequest request,
                RemoteQueryExecutor remote, ICurrentUser currentUser) =>
        {
            if (request.RememberHours is < 1)
                return EndpointHelpers.Invalid("rememberHours", "Choose at least one hour.");
            var terms = request.RememberHours is null && request.RememberScope is null
                ? null
                : new GrantTerms(request.RememberHours is { } hours ? TimeSpan.FromHours(hours) : null,
                    request.RememberScope);
            return await RemoteQueryResults.ExecuteAsync(() =>
                remote.DecideAsync(currentUser.OwnerId, approvalId, request.Approved, request.RememberCategory,
                    terms));
        }).WithName("DecideToolApproval");

        return api;
    }
}
