using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Jarvis.Agents;
using Jarvis.Api.Realtime;
using Jarvis.Api.Notifications;
using Jarvis.Api.Security;
using Jarvis.Application.Conversations;
using Jarvis.Application.Approvals;
using Jarvis.Application.Audit;
using Jarvis.Application.Memory;
using Jarvis.Application.Workflows;
using Jarvis.Application.Files;
using Jarvis.Application.Integrations;
using Jarvis.Application.Security;
using Jarvis.Domain.Conversations;
using Jarvis.Infrastructure;
using Jarvis.Infrastructure.Persistence;
using Jarvis.Memory;
using Jarvis.Mcp;
using Jarvis.Workflows;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Authority"]) ||
     string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Audience"])))
    throw new InvalidOperationException("Configure OIDC Authentication:Authority and Authentication:Audience before running Jarvis outside Development.");

if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(builder.Configuration["Antivirus:Host"]))
    throw new InvalidOperationException("Configure Antivirus:Host with a private ClamAV daemon before running Jarvis outside Development.");

var dataProtectionKeysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(dataProtectionKeysDirectory))
    throw new InvalidOperationException("Configure DataProtection:KeysDirectory as a persistent, private writable directory before running Jarvis outside Development.");

if (!builder.Environment.IsDevelopment() &&
    new[] { "LiveKit:ApiKey", "LiveKit:ApiSecret", "LiveKit:InternalUrl", "LiveKit:PublicUrl", "Voice:WorkerSecret" }
        .Any(key => string.IsNullOrWhiteSpace(builder.Configuration[key])))
    throw new InvalidOperationException("Configure LiveKit and Voice:WorkerSecret before running Jarvis outside Development.");

if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication("Bearer").AddJwtBearer("Bearer", options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.Audience = builder.Configuration["Authentication:Audience"];
        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst("sub")?.Value))
                    context.Fail("The access token must identify an OIDC subject.");
                return Task.CompletedTask;
            },
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.Request.Path.StartsWithSegments("/hubs/events"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
    builder.Services.AddAuthorization();
}
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.AddServiceDefaults();
builder.Services.AddJarvisInfrastructure(builder.Configuration);
builder.Services.AddJarvisMemory();
builder.Services.AddSingleton<TemporalReminderScheduler>();
builder.Services.AddSingleton<IFileProcessingScheduler>(serviceProvider => serviceProvider.GetRequiredService<TemporalReminderScheduler>());
builder.Services.AddSingleton<IConditionWatchScheduler>(serviceProvider => serviceProvider.GetRequiredService<TemporalReminderScheduler>());
builder.Services.AddSingleton<IDailyBriefingScheduler>(serviceProvider => serviceProvider.GetRequiredService<TemporalReminderScheduler>());
builder.Services.AddScoped<IReminderService, ReminderService>();
builder.Services.AddScoped<IConditionWatchService, ConditionWatchService>();
builder.Services.AddScoped<IDailyBriefingService, DailyBriefingService>();
builder.Services.AddScoped<IJarvisTaskRepository, WorkflowRepository>();
builder.Services.AddScoped<IJarvisTaskService, JarvisTaskService>();
builder.Services.AddSingleton<ITaskRunAbort, TaskRunAbort>();
builder.Services.AddJarvisAgent(builder.Configuration);
builder.Services.AddScoped<McpToolHost>();
builder.Services.AddScoped<AgentRunCoordinator>();
builder.Services.AddSingleton<VoiceConversationCoordinator>();
builder.Services.AddHttpClient<LiveKitAgentDispatchClient>();
builder.Services.AddHttpClient("firebase-messaging", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHostedService<NotificationPushWorker>();
builder.Services.AddHostedService<NotificationRealtimeWorker>();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5137", "http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Database:ApplyMigrationsAtStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<JarvisDbContext>();
    await database.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();
if (!app.Environment.IsDevelopment())
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.MapDefaultEndpoints();
var hub = app.MapHub<JarvisEventsHub>("/hubs/events");
if (!app.Environment.IsDevelopment()) hub.RequireAuthorization();

var api = app.MapGroup("/api/v1");
if (!app.Environment.IsDevelopment()) api.RequireAuthorization();
api.MapPost("/conversations", async (IConversationStore store, ICurrentUser currentUser, CreateConversationRequest request, CancellationToken ct) =>
{
    var title = string.IsNullOrWhiteSpace(request.Title) ? "New conversation" : request.Title.Trim();
    if (title.Length > 200) return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title must be 200 characters or fewer."] });
    var conversation = await store.CreateAsync(currentUser.OwnerId, title, ct);
    return Results.Created($"/api/v1/conversations/{conversation.Id}", new ConversationDto(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt));
}).WithName("CreateConversation");

api.MapGet("/conversations", async (IConversationStore store, ICurrentUser currentUser, CancellationToken ct) =>
    Results.Ok((await store.ListAsync(currentUser.OwnerId, ct)).Select(x => new ConversationDto(x.Id, x.Title, x.CreatedAt, x.UpdatedAt))))
    .WithName("ListConversations");

api.MapGet("/conversations/{conversationId:guid}", async (Guid conversationId, IConversationStore store,
    IJarvisTaskRepository tasks, ICurrentUser currentUser, CancellationToken ct) =>
{
    var conversation = await store.GetAsync(conversationId, currentUser.OwnerId, ct);
    if (conversation is null) return Results.NotFound();
    if (await tasks.GetTaskByConversationIdAsync(conversationId, currentUser.OwnerId, ct) is not null)
        return Results.NotFound();
    var messages = await store.GetMessagesAsync(conversationId, ct);
    return Results.Ok(new ConversationDetailsDto(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt,
        messages.Select(ToDto).ToArray()));
}).WithName("GetConversation");

api.MapDelete("/conversations/{conversationId:guid}", async (Guid conversationId,
    IConversationStore store, IConversationRunLock runLock,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    await using var lease = await runLock.AcquireAsync(conversationId, ct);
    var result = await store.DeleteAsync(conversationId, currentUser.OwnerId, ct);
    if (result == ConversationDeleteResult.NotFound) return Results.NotFound();
    if (result == ConversationDeleteResult.TaskBacked)
        return Results.Conflict(new { message = "Task conversations are managed from the Tasks section." });
    return Results.NoContent();
}).WithName("DeleteConversation");

api.MapPost("/conversations/{conversationId:guid}/messages", async (
    Guid conversationId,
    SendMessageRequest request,
    IConversationStore store,
    IConversationRunLock runLock,
    IJarvisTaskRepository tasks,
    IToolApprovalStore approvals,
    ICurrentUser currentUser,
    IJarvisAgent agent,
    AgentRunCoordinator coordinator,
    IHubContext<JarvisEventsHub> hub,
    CancellationToken ct) =>
{
    var content = request.Content?.Trim();
    if (string.IsNullOrWhiteSpace(content) || content.Length > 32_000)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["Message content must contain 1 to 32,000 characters."] });

    var conversation = await store.GetAsync(conversationId, currentUser.OwnerId, ct);
    if (conversation is null) return Results.NotFound();
    if (await tasks.GetTaskByConversationIdAsync(conversationId, currentUser.OwnerId, ct) is not null)
        return Results.Conflict(new { message = "Long-running task sessions are managed from the Tasks section." });

    await using var runLease = await runLock.AcquireAsync(conversationId, ct);
    var existingMessages = await store.GetMessagesAsync(conversationId, ct);
    var lastMessage = existingMessages.Count > 0 ? existingMessages[^1] : null;
    var isSameUserTurn = lastMessage is { Role: "user" } && lastMessage.Content == content;
    var pendingApprovals = await approvals.ListActionableForConversationAsync(currentUser.OwnerId, conversationId, ct);
    if (pendingApprovals.Count > 0)
    {
        if (!isSameUserTurn)
            return Results.Conflict(new { message = "Decide the pending tool call for this conversation first." });
        var stillPending = pendingApprovals.Where(x => x.Status == "pending").ToList();
        if (stillPending.Count > 0)
            return Results.Accepted("/api/v1/approvals", stillPending.Select(ToApprovalDto));
        return Results.Conflict(new { message = "A previous tool decision is still finishing for this conversation." });
    }

    var userMessage = isSameUserTurn ? lastMessage! : new Message(conversationId, "user", content);
    if (!isSameUserTurn)
        await store.AddMessageAsync(userMessage, ct);
    if (ReferenceEquals(userMessage, lastMessage) &&
        await coordinator.TryRecoverCompletedAssistantAsync(conversationId, content, ct) is { } recovered)
    {
        await coordinator.PublishRecoveredAssistantAsync(conversationId, recovered, ct);
        return Results.Ok(ToDto(recovered));
    }
    if (ReferenceEquals(userMessage, lastMessage) &&
        await coordinator.TryRecoverPendingApprovalsAsync(currentUser.OwnerId, conversationId, null, ct)
            is { PendingApprovals.Count: > 0 } recoveredApprovals)
        return Results.Accepted("/api/v1/approvals", recoveredApprovals.PendingApprovals.Select(ToApprovalDto));
    try
    {
        var outcome = await coordinator.RunAsync(currentUser.OwnerId, conversationId,
            agent.StreamReplyAsync(conversationId, userMessage, ct), userMessage.Content, ct,
            memorySourceId: userMessage.Id);
        if (outcome.PendingApprovals.Count != 0)
            return Results.Accepted("/api/v1/approvals", outcome.PendingApprovals.Select(ToApprovalDto));
        return Results.Ok(ToDto(outcome.AssistantMessage!));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Agent run failed for conversation {ConversationId}", conversationId);
        await PublishAgentFailedAsync(hub, app.Logger, conversationId,
            "Jarvis could not complete this response.");
        return Results.Problem("Jarvis could not complete this response.", statusCode: StatusCodes.Status502BadGateway);
    }

}).WithName("SendMessage");

api.MapGet("/approvals", async (IToolApprovalStore approvals, ICurrentUser currentUser, CancellationToken ct) =>
    Results.Ok((await approvals.ListActionableAsync(currentUser.OwnerId, ct)).Select(ToApprovalDto)))
    .WithName("ListPendingApprovals");

api.MapPost("/approvals/{approvalId:guid}/decision", async (
    Guid approvalId,
    ApprovalDecisionRequest request,
    IToolApprovalStore approvals,
    IConversationStore conversations,
    ICurrentUser currentUser,
    IJarvisAgent agent,
    AgentRunCoordinator coordinator,
    ILogger<AgentRunCoordinator> logger,
    IConversationRunLock runLock,
    IJarvisTaskService tasks,
    IJarvisTaskRepository taskRepository,
    ITaskRunAbort taskRunAbort,
    IServiceScopeFactory scopes,
    IHubContext<JarvisEventsHub> hub,
    CancellationToken ct) =>
{
    var ownerId = currentUser.OwnerId;
    var pending = await approvals.GetActionableAsync(approvalId, ownerId, ct);
    if (pending is null) return Results.NotFound();
    await using var runLease = await runLock.AcquireAsync(pending.ConversationId, ct);
    pending = await approvals.GetActionableAsync(approvalId, ownerId, ct);
    if (pending is null) return Results.Conflict(new { message = "This approval is already complete or being resumed." });
    if (await conversations.GetAsync(pending.ConversationId, ownerId, ct) is null) return Results.NotFound();
    if (pending.Status == "pending")
    {
        var earlier = (await approvals.ListActionableForConversationAsync(ownerId, pending.ConversationId, ct))
            .FirstOrDefault(x => x.Status == "pending");
        if (earlier is not null && earlier.Id != pending.Id)
            return Results.Conflict(new { message = "Decide the earlier pending tool call for this conversation first." });
    }
    if (pending.TaskId is { } boundTaskId)
    {
        var task = await taskRepository.GetTaskAsync(boundTaskId, ownerId, ct);
        if (task is null || task.Status != "needs_approval")
            return Results.Conflict(new { message = "This approval is no longer attached to an active task." });
    }
    ToolApprovalRecord? decided;
    if (pending.Status == "pending")
        decided = await approvals.DecideAsync(approvalId, ownerId, request.Approved, ct);
    else if (pending.Approved == request.Approved)
        decided = pending;
    else
        return Results.Conflict(new { message = "Retry must use the decision already recorded for this approval." });
    if (decided is null) return Results.Conflict(new { message = "This approval was already decided." });
    if (!await approvals.TryStartResumeAsync(approvalId, ownerId, ct))
        return Results.Conflict(new { message = "This approval is already being resumed." });

    using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
    using var abortLease = decided.TaskId is { } resumeTaskId
        ? taskRunAbort.Register(resumeTaskId, abort)
        : null;
    var runCt = abort.Token;
    using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(runCt);
    if (decided.TaskId is { } watchTaskId)
        _ = WatchTaskCancellationAsync(scopes, logger, watchTaskId, ownerId, abort, watchCts.Token);
    using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(runCt);
    _ = HeartbeatApprovalResumeAsync(scopes, logger, approvalId, ownerId, heartbeatCts.Token);

    try
    {
        if (pending.Status != "pending")
        {
            var messages = await conversations.GetMessagesAsync(decided.ConversationId, runCt);
            var last = messages.Count > 0 ? messages[^1] : null;
            if (last is { Role: "assistant" } &&
                decided.DecidedAt is { } decidedAt &&
                last.CreatedAt >= decidedAt)
            {
                var session = await conversations.GetAgentSessionAsync(decided.ConversationId, runCt);
                if (session is not null &&
                    AgentSessionJson.TryGetPendingApprovals(session, out var pendingFromSession, out _))
                {
                    foreach (var pendingRequest in pendingFromSession)
                    {
                        try
                        {
                            await approvals.CreateAsync(ownerId, decided.ConversationId, pendingRequest.RequestId,
                                pendingRequest.ToolCallId, pendingRequest.ToolName, pendingRequest.ArgumentsJson,
                                decided.TaskId, runCt);
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }
                }
                if (!await TaskStillNeedsApprovalAsync(taskRepository, approvals, decided.TaskId, ownerId,
                        approvalId, CancellationToken.None))
                    return Results.Conflict(new { message = "This task was cancelled." });
                var remaining = (await approvals.ListActionableForConversationAsync(ownerId, decided.ConversationId, runCt))
                    .Where(x => x.Id != approvalId)
                    .ToList();
                if (remaining.Count != 0)
                {
                    await approvals.MarkResumeCompletedAsync(approvalId, ownerId, runCt);
                    return Results.Accepted("/api/v1/approvals", remaining.Select(ToApprovalDto));
                }
                if (decided.Approved == true)
                    await tasks.CompleteAfterApprovalAsync(decided.TaskId, ownerId, last.Content, runCt);
                else
                    await tasks.FailAfterRejectedApprovalAsync(decided.TaskId, ownerId, last.Content, runCt);
                await approvals.MarkResumeCompletedAsync(approvalId, ownerId, runCt);
                return Results.Ok(ToDto(last));
            }
        }

        var outcome = await coordinator.RunAsync(ownerId, decided.ConversationId,
            agent.ResumeReplyAsync(decided.ConversationId, decided.ToReply(), runCt), null, runCt, decided.TaskId);
        if (outcome.PendingApprovals.Count != 0)
        {
            if (!await TaskStillNeedsApprovalAsync(taskRepository, approvals, decided.TaskId, ownerId,
                    approvalId, CancellationToken.None))
                return Results.Conflict(new { message = "This task was cancelled." });
            await approvals.MarkResumeCompletedAsync(approvalId, ownerId, runCt);
            return Results.Accepted("/api/v1/approvals", outcome.PendingApprovals.Select(ToApprovalDto));
        }
        if (decided.Approved == true)
        {
            await tasks.CompleteAfterApprovalAsync(decided.TaskId, ownerId,
                outcome.AssistantMessage?.Content ?? "The approved task step finished.", runCt);
        }
        else
        {
            await tasks.FailAfterRejectedApprovalAsync(decided.TaskId, ownerId,
                outcome.AssistantMessage?.Content ?? "The tool call was declined.", runCt);
        }
        await approvals.MarkResumeCompletedAsync(approvalId, ownerId, runCt);
        return Results.Ok(ToDto(outcome.AssistantMessage!));
    }
    catch (OperationCanceledException)
    {
        await approvals.MarkResumeFailedAsync(approvalId, ownerId, CancellationToken.None);
        await PublishAgentFailedAsync(hub, logger, decided.ConversationId, "This task was cancelled.");
        if (ct.IsCancellationRequested) throw;
        return Results.Conflict(new { message = "This task was cancelled." });
    }
    catch (Exception exception)
    {
        await approvals.MarkResumeFailedAsync(approvalId, ownerId, CancellationToken.None);
        logger.LogError(exception, "Tool approval {ApprovalId} was decided but its agent resume failed.", approvalId);
        await PublishAgentFailedAsync(hub, logger, decided.ConversationId,
            "Jarvis could not complete this response.");
        return Results.Problem("Jarvis could not resume this decided tool call. It can be retried from Tool approvals.",
            statusCode: StatusCodes.Status502BadGateway);
    }
    finally
    {
        try { watchCts.Cancel(); }
        catch (ObjectDisposedException) { }
        try { heartbeatCts.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}).WithName("DecideToolApproval");

api.MapGet("/reminders", async (IReminderService reminders, ICurrentUser currentUser, CancellationToken ct) =>
    Results.Ok((await reminders.ListAsync(currentUser.OwnerId, ct)).Select(ToReminderDto)))
    .WithName("ListReminders");

api.MapGet("/reminders/{id:guid}", async (Guid id, IReminderService reminders,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    var reminder = await reminders.GetAsync(id, currentUser.OwnerId, ct);
    return reminder is null ? Results.NotFound() : Results.Ok(ToReminderDto(reminder));
}).WithName("GetReminder");

api.MapPost("/reminders", async (IReminderService reminders, ICurrentUser currentUser, ReminderRequest request, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 300)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title must contain 1 to 300 characters."] });
    try
    {
        var reminder = await reminders.CreateAsync(currentUser.OwnerId, request.Title, request.DueAt, ct);
        return Results.Created($"/api/v1/reminders/{reminder.Id}", ToReminderDto(reminder));
    }
    catch (ArgumentOutOfRangeException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["dueAt"] = [exception.Message] });
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        app.Logger.LogError(exception, "Temporal could not schedule a reminder for user {OwnerId}.", currentUser.OwnerId);
        return Results.Problem("Reminder service is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).WithName("CreateReminder");

api.MapDelete("/reminders/{id:guid}", async (Guid id, IReminderService reminders, ICurrentUser currentUser, CancellationToken ct) =>
{
    var reminder = await reminders.CancelAsync(id, currentUser.OwnerId, ct);
    return reminder is null ? Results.NotFound() : Results.Ok(ToReminderDto(reminder));
}).WithName("CancelReminder");

api.MapGet("/watches", async (IConditionWatchService watches, ICurrentUser currentUser, CancellationToken ct) =>
    Results.Ok((await watches.ListAsync(currentUser.OwnerId, ct)).Select(ToConditionWatchDto)))
    .WithName("ListConditionWatches");

api.MapGet("/watches/{id:guid}", async (Guid id, IConditionWatchService watches,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    var watch = await watches.GetAsync(id, currentUser.OwnerId, ct);
    return watch is null ? Results.NotFound() : Results.Ok(ToConditionWatchDto(watch));
}).WithName("GetConditionWatch");

api.MapPost("/watches", async (CreateConditionWatchRequest request, IConditionWatchService watches,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    try
    {
        var watch = await watches.CreateAsync(currentUser.OwnerId, request, ct);
        return Results.Created($"/api/v1/watches/{watch.Id}", ToConditionWatchDto(watch));
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["watch"] = [exception.Message] });
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        app.Logger.LogError(exception, "Temporal could not schedule a condition watch for user {OwnerId}.", currentUser.OwnerId);
        return Results.Problem("Condition watch service is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).WithName("CreateConditionWatch");

api.MapDelete("/watches/{id:guid}", async (Guid id, IConditionWatchService watches,
    ICurrentUser currentUser, CancellationToken ct) =>
    await watches.CancelAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
    .WithName("CancelConditionWatch");

api.MapGet("/mcp-servers", async (IUserMcpServerRegistry servers, ICurrentUser currentUser,
    CancellationToken ct) => Results.Ok(await servers.ListAsync(currentUser.OwnerId, ct)))
    .WithName("ListUserMcpServers");

api.MapPost("/mcp-servers", async (AddUserMcpServerRequest request, IUserMcpServerRegistry servers,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    try
    {
        var server = await servers.AddAsync(currentUser.OwnerId, request, ct);
        return Results.Created($"/api/v1/mcp-servers/{server.Id}", server);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["server"] = [exception.Message] });
    }
}).WithName("AddUserMcpServer");

api.MapPut("/mcp-servers/{id}", async (string id, AddUserMcpServerRequest request,
    IUserMcpServerRegistry servers, ICurrentUser currentUser, CancellationToken ct) =>
{
    try
    {
        var server = await servers.UpdateAsync(currentUser.OwnerId, id, request, ct);
        return server is null ? Results.NotFound() : Results.Ok(server);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["server"] = [exception.Message] });
    }
}).WithName("UpdateUserMcpServer");

api.MapDelete("/mcp-servers/{id}", async (string id, IUserMcpServerRegistry servers,
    ICurrentUser currentUser, CancellationToken ct) =>
    await servers.RemoveAsync(currentUser.OwnerId, id, ct) ? Results.NoContent() : Results.NotFound())
    .WithName("RemoveUserMcpServer");

api.MapGet("/integrations/credentials", async (IIntegrationCredentialStore credentials,
    ICurrentUser currentUser, CancellationToken ct) =>
    Results.Ok((await credentials.ListAsync(currentUser.OwnerId, ct))
        .Where(status => !IntegrationCredentialProviders.IsUserMcpManaged(status.Provider))))
    .WithName("ListIntegrationCredentialStatuses");

api.MapGet("/integrations/connections", async (McpToolHost mcpToolHost, CancellationToken ct) =>
{
    await mcpToolHost.InitializeAsync(ct);
    return Results.Ok(mcpToolHost.Statuses);
}).WithName("ListIntegrationConnectionStatuses");

api.MapGet("/integrations/{provider}/credentials", async (string provider,
    IIntegrationCredentialStore credentials, ICurrentUser currentUser, CancellationToken ct) =>
{
    if (IntegrationCredentialProviders.IsUserMcpManaged(provider)) return Results.NotFound();
    var status = await credentials.GetStatusAsync(currentUser.OwnerId, provider, ct);
    return status is null ? Results.NotFound() : Results.Ok(status);
}).WithName("GetIntegrationCredentialStatus");

api.MapPut("/integrations/{provider}/credentials", async (string provider,
    SaveIntegrationCredentialsRequest request, IIntegrationCredentialStore credentials,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    if (RejectUserMcpCredentialRoute(provider) is { } rejected) return rejected;
    try
    {
        await credentials.SaveAsync(currentUser.OwnerId, provider, request.Secrets, ct);
        return Results.Ok(await credentials.GetStatusAsync(currentUser.OwnerId, provider, ct));
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = [exception.Message] });
    }
}).WithName("SaveIntegrationCredentials");

api.MapPut("/integrations/{provider}/credentials/{secretName}", async (string provider, string secretName,
    SaveIntegrationSecretRequest request, IIntegrationCredentialStore credentials,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    if (RejectUserMcpCredentialRoute(provider, secretName) is { } rejected) return rejected;
    try
    {
        if (IntegrationCredentialProviders.IsUserMcpManaged(provider) &&
            !await UserMcpServerExistsAsync(credentials, currentUser.OwnerId, provider, ct))
            return Results.NotFound();
        await credentials.SaveSecretAsync(currentUser.OwnerId, provider, secretName, request.Value, ct);
        return Results.Ok(PublicCredentialStatus(
            (await credentials.GetStatusAsync(currentUser.OwnerId, provider, ct))!));
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["credential"] = [exception.Message] });
    }
}).WithName("SaveIntegrationSecret");

api.MapDelete("/integrations/{provider}/credentials/{secretName}", async (string provider, string secretName,
    IIntegrationCredentialStore credentials, ICurrentUser currentUser, CancellationToken ct) =>
{
    if (RejectUserMcpCredentialRoute(provider, secretName) is { } rejected) return rejected;
    try
    {
        if (IntegrationCredentialProviders.IsUserMcpManaged(provider) &&
            !await UserMcpServerExistsAsync(credentials, currentUser.OwnerId, provider, ct))
            return Results.NotFound();
        return await credentials.DeleteSecretAsync(currentUser.OwnerId, provider, secretName, ct)
            ? Results.NoContent() : Results.NotFound();
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["credential"] = [exception.Message] });
    }
}).WithName("DeleteIntegrationSecret");

api.MapDelete("/integrations/{provider}/credentials", async (string provider,
    IIntegrationCredentialStore credentials, ICurrentUser currentUser, CancellationToken ct) =>
{
    if (RejectUserMcpCredentialRoute(provider) is { } rejected) return rejected;
    return await credentials.DeleteAsync(currentUser.OwnerId, provider, ct) ? Results.NoContent() : Results.NotFound();
}).WithName("DeleteIntegrationCredentials");

api.MapGet("/briefings/daily", async (IDailyBriefingService briefings, ICurrentUser currentUser,
    CancellationToken ct) =>
{
    var preference = await briefings.GetAsync(currentUser.OwnerId, ct);
    return preference is null
        ? Results.Ok(new DailyBriefingPreferenceRecord(currentUser.OwnerId, false,
            new TimeOnly(8, 0), "UTC", string.Empty, null, null))
        : Results.Ok(preference);
}).WithName("GetDailyBriefing");

api.MapPut("/briefings/daily", async (SaveDailyBriefingRequest request,
    IDailyBriefingService briefings, ICurrentUser currentUser, CancellationToken ct) =>
{
    try { return Results.Ok(await briefings.SaveAsync(currentUser.OwnerId, request, ct)); }
    catch (ArgumentException exception)
    { return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZoneId"] = [exception.Message] }); }
}).WithName("SaveDailyBriefing");

api.MapGet("/tasks", async (IJarvisTaskService tasks, ICurrentUser currentUser, CancellationToken ct) =>
    Results.Ok((await tasks.ListAsync(currentUser.OwnerId, ct)).Select(ToJarvisTaskDto)))
    .WithName("ListTasks");

api.MapGet("/tasks/{id:guid}", async (Guid id, IJarvisTaskRepository tasks,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    var task = await tasks.GetTaskAsync(id, currentUser.OwnerId, ct);
    return task is null ? Results.NotFound() : Results.Ok(ToJarvisTaskDto(task));
}).WithName("GetTask");

api.MapGet("/tasks/{id:guid}/messages", async (Guid id, IJarvisTaskRepository tasks,
    IConversationStore conversations, ICurrentUser currentUser, CancellationToken ct) =>
{
    var task = await tasks.GetTaskAsync(id, currentUser.OwnerId, ct);
    if (task is null) return Results.NotFound();
    var messages = await conversations.GetMessagesAsync(task.ConversationId, ct);
    return Results.Ok(messages.Select(ToDto));
}).WithName("GetTaskMessages");

api.MapPost("/tasks", async (CreateJarvisTaskRequest request, IJarvisTaskService tasks,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title must contain 1 to 200 characters."] });
    if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 32_000)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["prompt"] = ["Instructions must contain 1 to 32,000 characters."] });
    try
    {
        var task = await tasks.CreateAsync(currentUser.OwnerId, request.Title, request.Prompt, ct);
        return Results.Created($"/api/v1/tasks/{task.Id}", ToJarvisTaskDto(task));
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        app.Logger.LogError(exception, "Temporal could not start a task for user {OwnerId}.", currentUser.OwnerId);
        return Results.Problem("Task service is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).WithName("CreateTask");

api.MapDelete("/tasks/{id:guid}", async (Guid id, IJarvisTaskService tasks,
    ICurrentUser currentUser, CancellationToken ct) =>
    await tasks.CancelAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
    .WithName("CancelTask");

api.MapGet("/notifications", async (INotificationRepository notifications, ICurrentUser currentUser, CancellationToken ct) =>
    Results.Ok((await notifications.ListNotificationsAsync(currentUser.OwnerId, ct)).Select(ToNotificationDto)))
    .WithName("ListNotifications");

api.MapPut("/push-devices", async (PushDeviceRequest request, IPushDeviceRepository devices,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    var token = request.Token?.Trim();
    var platform = request.Platform?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 ||
        platform is not ("android" or "ios"))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [string.IsNullOrWhiteSpace(token) || token.Length > 4096 ? "token" : "platform"] =
                [string.IsNullOrWhiteSpace(token) || token.Length > 4096
                    ? "A device token of 1 to 4,096 characters is required."
                    : "Platform must be android or ios."]
        });
    var device = await devices.RegisterAsync(currentUser.OwnerId, token, platform, ct);
    return Results.Ok(device);
}).WithName("RegisterPushDevice");

api.MapDelete("/push-devices", async ([Microsoft.AspNetCore.Mvc.FromBody] PushDeviceRequest request, IPushDeviceRepository devices,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    var token = request.Token?.Trim();
    if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
        return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["token"] = ["A device token of 1 to 4,096 characters is required."] });
    return await devices.RemoveAsync(currentUser.OwnerId, token, ct)
        ? Results.NoContent()
        : Results.NotFound();
}).WithName("RemovePushDevice");

api.MapPost("/voice/session", async (VoiceSessionRequest request, IConversationStore conversations,
    IJarvisTaskRepository tasks, ICurrentUser currentUser, IConfiguration configuration,
    LiveKitAgentDispatchClient dispatchClient, CancellationToken ct) =>
{
    if (await conversations.GetAsync(request.ConversationId, currentUser.OwnerId, ct) is null)
        return Results.NotFound();
    if (await tasks.GetTaskByConversationIdAsync(request.ConversationId, currentUser.OwnerId, ct) is not null)
        return Results.Conflict(new { message = "Task conversations cannot start a voice session." });

    var apiKey = configuration["LiveKit:ApiKey"];
    var apiSecret = configuration["LiveKit:ApiSecret"];
    var serverUrl = configuration["LiveKit:PublicUrl"];
    var workerSecret = configuration["Voice:WorkerSecret"];
    if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret) ||
        string.IsNullOrWhiteSpace(workerSecret) ||
        !Uri.TryCreate(serverUrl, UriKind.Absolute, out var parsedUrl) ||
        parsedUrl.Scheme is not ("ws" or "wss"))
        return Results.Problem("LiveKit is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

    var room = $"jarvis-{request.ConversationId:N}-{Guid.CreateVersion7():N}";
    try
    {
        await dispatchClient.DispatchAsync(room, request.ConversationId, currentUser.OwnerId, ct);
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        app.Logger.LogError(exception, "Could not dispatch LiveKit voice worker for conversation {ConversationId}.",
            request.ConversationId);
        return Results.Problem("Voice service is temporarily unavailable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var identity = Guid.CreateVersion7().ToString("N");
    var now = DateTimeOffset.UtcNow;
    var expiresAt = now.AddMinutes(10);
    var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret));
    var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Iss, apiKey),
        new(JwtRegisteredClaimNames.Sub, identity),
        new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        new(JwtRegisteredClaimNames.Nbf, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        new(JwtRegisteredClaimNames.Exp, expiresAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        new("video", JsonSerializer.Serialize(new
        {
            roomJoin = true,
            room,
            canPublish = true,
            canSubscribe = true,
            canPublishData = true
        }), JsonClaimValueTypes.Json)
    };
    var token = new JwtSecurityToken(new JwtHeader(credentials), new JwtPayload(claims));
    return Results.Ok(new VoiceSessionDto(serverUrl!, room, identity,
        new JwtSecurityTokenHandler().WriteToken(token), expiresAt));
}).WithName("CreateVoiceSession");

api.MapPost("/voice/internal/{conversationId:guid}/transcript", async (Guid conversationId,
    VoiceWorkerTranscriptRequest request, HttpRequest httpRequest, IConfiguration configuration,
    IConversationStore conversations, IJarvisTaskRepository tasks,
    VoiceConversationCoordinator coordinator, CancellationToken ct) =>
{
    var expectedSecret = configuration["Voice:WorkerSecret"];
    var suppliedSecret = httpRequest.Headers["X-Jarvis-Voice-Secret"].ToString();
    if (!SecretComparer.FixedTimeEquals(expectedSecret, suppliedSecret))
        return Results.Unauthorized();
    if (request.OwnerId == Guid.Empty || string.IsNullOrWhiteSpace(request.Transcript) ||
        request.Transcript.Length > 32_000)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["transcript"] = ["Transcript must contain 1 to 32,000 characters."]
        });
    if (await conversations.GetAsync(conversationId, request.OwnerId, ct) is null)
        return Results.NotFound();
    if (await tasks.GetTaskByConversationIdAsync(conversationId, request.OwnerId, ct) is not null)
        return Results.Conflict(new { message = "Task conversations cannot use realtime voice." });

    var deltas = Channel.CreateBounded<string>(new BoundedChannelOptions(32)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    var agentRun = coordinator.HandleTranscriptAsync(request.OwnerId, conversationId,
        request.Transcript, ct, (delta, token) => deltas.Writer.WriteAsync(delta, token).AsTask());

    async Task<string?> CompleteAgentRunAsync()
    {
        try
        {
            var result = await agentRun;
            deltas.Writer.TryComplete();
            return result;
        }
        catch (Exception exception)
        {
            deltas.Writer.TryComplete(exception);
            throw;
        }
    }

    var completion = CompleteAgentRunAsync();
    return Results.Stream(async stream =>
    {
        await foreach (var delta in deltas.Reader.ReadAllAsync(ct))
            await WriteVoiceStreamEventAsync(stream, new { type = "delta", text = delta }, ct);
        var responseText = await completion;
        await WriteVoiceStreamEventAsync(stream, new { type = "done", responseText }, ct);
    }, contentType: "application/x-ndjson");
}).WithName("ProcessVoiceTranscript").AllowAnonymous();

api.MapGet("/audit", async (int? limit, IAuditEventStore audit, ICurrentUser currentUser, CancellationToken ct) =>
    Results.Ok((await audit.ListAsync(currentUser.OwnerId, Math.Clamp(limit ?? 100, 1, 200), ct))
        .Select(ToAuditEventDto)))
    .WithName("ListAuditEvents");

api.MapPost("/notifications/{id:guid}/read", async (Guid id, INotificationRepository notifications, ICurrentUser currentUser, CancellationToken ct) =>
    await notifications.MarkReadAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
    .WithName("MarkNotificationRead");

api.MapGet("/files", async (IFileService files, ICurrentUser currentUser, CancellationToken ct) =>
    Results.Ok((await files.ListAsync(currentUser.OwnerId, ct)).Select(ToFileDto)))
    .WithName("ListFiles");

api.MapGet("/files/search", async (string? query, IFileSearchService files, ICurrentUser currentUser, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(query) || query.Length > 2_000)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Query must contain 1 to 2,000 characters."] });
    var hits = await files.SearchAsync(currentUser.OwnerId, query, ct);
    return Results.Ok(hits.Select(hit => new FileSearchHitDto(hit.FileId, hit.FileName, hit.ChunkIndex, hit.Content, hit.Score)));
}).WithName("SearchFiles");

api.MapPost("/files/{id:guid}/reprocess", async (Guid id, IFileService files, ICurrentUser currentUser,
    CancellationToken ct) =>
    await files.RetryIndexingAsync(id, currentUser.OwnerId, ct) ? Results.NoContent() : Results.NotFound())
    .WithName("RetryFileIndexing");

api.MapPost("/files", async (IFormFile? file, IFileService files, IAuditEventStore audit,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    if (file is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Choose a file to upload."] });
    await using var content = file.OpenReadStream();
    try
    {
        var stored = await files.UploadAsync(currentUser.OwnerId, file.FileName, file.ContentType,
            file.Length, content, ct);
        await audit.AppendAsync(currentUser.OwnerId, "files", "file.uploaded", "moderate", true, null,
            JsonSerializer.Serialize(new { resourceId = stored.Id }), ct);
        return Results.Created($"/api/v1/files/{stored.Id}", ToFileDto(stored));
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [exception.Message] });
    }
    catch (MalwareDetectedException)
    {
        await audit.AppendAsync(currentUser.OwnerId, "files", "file.malware_rejected", "high", false,
            null, null, ct);
        return Results.UnprocessableEntity(new { message = "The uploaded file was rejected by malware scanning." });
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        app.Logger.LogError(exception, "Could not store an uploaded file for user {OwnerId}.", currentUser.OwnerId);
        return Results.Problem("File storage is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
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
{
    if (!await files.DeleteAsync(id, currentUser.OwnerId, ct)) return Results.NotFound();
    return Results.NoContent();
})
    .WithName("DeleteFile");

api.MapGet("/memory", async (IMemoryService memory, ICurrentUser currentUser, string? kind, CancellationToken ct) =>
{
    if (!IsMemoryKindValid(kind)) return Results.ValidationProblem(new Dictionary<string, string[]>
        { ["kind"] = ["Choose a supported memory category."] });
    return Results.Ok((await memory.ListAsync(currentUser.OwnerId, kind, ct)).Select(ToMemoryDto));
})
    .WithName("ListMemories");

api.MapGet("/memory/search", async (IMemoryService memory, ICurrentUser currentUser, string query,
    string? kind, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(query) || query.Length > 2_000)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Query must contain 1 to 2,000 characters."] });
    if (!IsMemoryKindValid(kind)) return Results.ValidationProblem(new Dictionary<string, string[]>
        { ["kind"] = ["Choose a supported memory category."] });
    return Results.Ok((await memory.SearchAsync(currentUser.OwnerId, query, ct, kind))
        .Select(hit => new MemoryHitDto(ToMemoryDto(hit.Memory), hit.Score)));
})
    .WithName("SearchMemories");

api.MapPost("/memory", async (IMemoryService memory, IAuditEventStore audit,
    ICurrentUser currentUser, MemoryRequest request, CancellationToken ct) =>
{
    if (!ValidateMemoryRequest(request, out var errors)) return Results.ValidationProblem(errors);
    var record = await memory.CreateAsync(currentUser.OwnerId, request.Kind!, request.Content!, request.Importance,
        request.Confidence, request.ValidUntil, request.IsPinned, ct);
    await audit.AppendAsync(currentUser.OwnerId, "memory", "memory.created", "moderate", true, null,
        JsonSerializer.Serialize(new { resourceId = record.Id, kind = record.Kind }), ct);
    return Results.Created($"/api/v1/memory/{record.Id}", ToMemoryDto(record));
}).WithName("CreateMemory");

api.MapGet("/memory/{id:guid}", async (Guid id, IMemoryService memory, ICurrentUser currentUser, CancellationToken ct) =>
{
    var record = await memory.GetAsync(id, currentUser.OwnerId, ct);
    return record is null ? Results.NotFound() : Results.Ok(ToMemoryDto(record));
}).WithName("GetMemory");

api.MapPut("/memory/{id:guid}", async (Guid id, IMemoryService memory, IAuditEventStore audit,
    ICurrentUser currentUser, MemoryRequest request, CancellationToken ct) =>
{
    if (!ValidateMemoryRequest(request, out var errors)) return Results.ValidationProblem(errors);
    if (await memory.GetAsync(id, currentUser.OwnerId, ct) is null) return Results.NotFound();
    var record = await memory.UpdateAsync(id, currentUser.OwnerId, request.Kind!, request.Content!, request.Importance,
        request.Confidence, request.ValidUntil, request.IsPinned, ct);
    if (record is null) return Results.NotFound();
    await audit.AppendAsync(currentUser.OwnerId, "memory", "memory.updated", "moderate", true, null,
        JsonSerializer.Serialize(new { resourceId = id, kind = record.Kind }), ct);
    return Results.Ok(ToMemoryDto(record));
}).WithName("UpdateMemory");

api.MapDelete("/memory/{id:guid}", async (Guid id, IMemoryService memory, IAuditEventStore audit,
    ICurrentUser currentUser, CancellationToken ct) =>
{
    if (await memory.GetAsync(id, currentUser.OwnerId, ct) is null) return Results.NotFound();
    await memory.DeleteAsync(id, currentUser.OwnerId, ct);
    await audit.AppendAsync(currentUser.OwnerId, "memory", "memory.deleted", "high", true, null,
        JsonSerializer.Serialize(new { resourceId = id }), ct);
    return Results.NoContent();
}).WithName("DeleteMemory");

app.Run();

static IResult? RejectUserMcpCredentialRoute(string provider, string? secretName = null)
{
    if (!IntegrationCredentialProviders.IsUserMcpManaged(provider)) return null;
    if (IntegrationCredentialProviders.IsUserMcpTokenSecret(secretName)) return null;
    return Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["provider"] = ["MCP servers are managed from /api/v1/mcp-servers, not integration credentials."]
    });
}

static async Task<bool> UserMcpServerExistsAsync(IIntegrationCredentialStore credentials, Guid ownerId,
    string provider, CancellationToken cancellationToken)
{
    var secrets = await credentials.GetSecretsAsync(ownerId, provider, cancellationToken);
    return secrets is not null &&
           secrets.ContainsKey(IntegrationCredentialProviders.UserMcpConfigSecret);
}

static IntegrationCredentialStatus PublicCredentialStatus(IntegrationCredentialStatus status) =>
    IntegrationCredentialProviders.IsUserMcpManaged(status.Provider)
        ? status with
        {
            SecretNames = status.SecretNames
                .Where(IntegrationCredentialProviders.IsUserMcpTokenSecret)
                .ToArray()
        }
        : status;

static async Task PublishAgentFailedAsync(IHubContext<JarvisEventsHub> hub, ILogger logger, Guid conversationId,
    string message)
{
    try
    {
        await hub.Clients.Group(JarvisEventsHub.GroupName(conversationId))
            .SendAsync("agent.failed", new { conversationId, message }, CancellationToken.None);
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "Could not publish agent.failed for conversation {ConversationId}.",
            conversationId);
    }
}

static async Task HeartbeatApprovalResumeAsync(IServiceScopeFactory scopes, ILogger logger, Guid approvalId,
    Guid ownerId, CancellationToken cancellationToken)
{
    try
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IToolApprovalStore>()
                    .HeartbeatResumeAsync(approvalId, ownerId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Approval resume heartbeat failed for {ApprovalId}.", approvalId);
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (ObjectDisposedException)
    {
    }
}

static async Task WatchTaskCancellationAsync(IServiceScopeFactory scopes, ILogger logger, Guid taskId, Guid ownerId,
    CancellationTokenSource abort, CancellationToken cancellationToken)
{
    try
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var tasks = scope.ServiceProvider.GetRequiredService<IJarvisTaskRepository>();
                var task = await tasks.GetTaskAsync(taskId, ownerId, cancellationToken);
                if (task is null || task.Status == "cancelled")
                {
                    try { abort.Cancel(); }
                    catch (ObjectDisposedException) { }
                    return;
                }
                if (task.Status is "completed" or "failed")
                    return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Task cancellation watch failed for {TaskId}.", taskId);
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (ObjectDisposedException)
    {
    }
}

static async Task<bool> TaskStillNeedsApprovalAsync(IJarvisTaskRepository tasks, IToolApprovalStore approvals,
    Guid? taskId, Guid ownerId, Guid approvalId, CancellationToken cancellationToken)
{
    if (taskId is null) return true;
    var task = await tasks.GetTaskAsync(taskId.Value, ownerId, cancellationToken);
    if (task is { Status: "needs_approval" }) return true;
    await approvals.CancelIncompleteForTaskAsync(taskId.Value, ownerId, CancellationToken.None);
    await approvals.MarkResumeFailedAsync(approvalId, ownerId, CancellationToken.None);
    return false;
}

static MessageDto ToDto(Message message) => new(message.Id, message.Role, message.Content, message.CreatedAt);
static async Task WriteVoiceStreamEventAsync(Stream stream, object value, CancellationToken cancellationToken)
{
    var data = JsonSerializer.SerializeToUtf8Bytes(value);
    await stream.WriteAsync(data, cancellationToken);
    await stream.WriteAsync(new byte[] { (byte)'\n' }, cancellationToken);
    await stream.FlushAsync(cancellationToken);
}
static ToolApprovalDto ToApprovalDto(ToolApprovalRecord approval) => new(approval.Id, approval.ConversationId,
    approval.ToolName, approval.ArgumentsJson, approval.Status, approval.Approved, approval.ResumeStatus,
    approval.CreatedAt);
static ReminderDto ToReminderDto(ReminderRecord reminder) => new(reminder.Id, reminder.Title, reminder.DueAt,
    reminder.Status, reminder.CreatedAt, reminder.CompletedAt);
static ConditionWatchDto ToConditionWatchDto(ConditionWatchRecord watch) => new(watch.Id, watch.Title,
    watch.Url, watch.JsonPath, watch.Comparison, watch.Threshold, watch.IntervalMinutes, watch.Status,
    watch.CreatedAt, watch.LastCheckedAt, watch.LastValue);
static JarvisTaskDto ToJarvisTaskDto(JarvisTaskRecord task) => new(task.Id, task.Title, task.Prompt,
    task.Status, task.ConversationId, task.CreatedAt, task.StartedAt, task.CompletedAt, task.Summary);
static NotificationDto ToNotificationDto(NotificationRecord notification) => new(notification.Id, notification.Type,
    notification.Title, notification.Body, notification.SourceId, notification.CreatedAt, notification.ReadAt);
static AuditEventDto ToAuditEventDto(AuditEventRecord item) => new(item.Id, item.AgentRunId, item.Tool,
    item.Action, item.RiskClass, item.ApprovalId, item.Timestamp, item.Success, item.MetadataJson);
static FileDto ToFileDto(Jarvis.Domain.Files.StoredFile file) => new(file.Id, file.FileName, file.ContentType,
    file.SizeBytes, file.Sha256, file.CreatedAt, file.ProcessingStatus);
static MemoryDto ToMemoryDto(Jarvis.Domain.Memory.MemoryRecord memory) => new(memory.Id, memory.Kind, memory.Content,
    memory.Importance, memory.Confidence, memory.CreatedAt, memory.UpdatedAt, memory.ValidUntil, memory.IsPinned,
    memory.SourceType);
static bool ValidateMemoryRequest(MemoryRequest request, out Dictionary<string, string[]> errors)
{
    errors = [];
    if (string.IsNullOrWhiteSpace(request.Kind)) errors["kind"] = ["Kind is required."];
    if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 8_000) errors["content"] = ["Content must contain 1 to 8,000 characters."];
    if (request.Importance is < 0 or > 1) errors["importance"] = ["Importance must be between 0 and 1."];
    if (request.Confidence is < 0 or > 1) errors["confidence"] = ["Confidence must be between 0 and 1."];
    if (request.Kind is not null && request.Kind is not ("preference" or "fact" or "decision" or "project" or "event" or "relationship" or "technical" or "routine" or "other"))
        errors["kind"] = ["Unknown memory kind."];
    return errors.Count == 0;
}

static bool IsMemoryKindValid(string? kind) => kind is null || kind is
    "preference" or "fact" or "decision" or "project" or "event" or "relationship" or "technical" or "routine" or "other";

public sealed record CreateConversationRequest(string? Title);
public sealed record VoiceSessionRequest(Guid ConversationId);
public sealed record VoiceSessionDto(string ServerUrl, string Room, string Identity, string Token,
    DateTimeOffset ExpiresAt);
public sealed record VoiceWorkerTranscriptRequest(Guid OwnerId, string? Transcript);
public sealed record SendMessageRequest([Required, StringLength(32_000, MinimumLength = 1)] string? Content);
public sealed record ConversationDto(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ConversationDetailsDto(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<MessageDto> Messages);
public sealed record MessageDto(Guid Id, string Role, string Content, DateTimeOffset CreatedAt);
public sealed record ApprovalDecisionRequest(bool Approved);
public sealed record ToolApprovalDto(Guid Id, Guid ConversationId, string ToolName, string ArgumentsJson,
    string Status, bool? Approved, string ResumeStatus, DateTimeOffset CreatedAt);
public sealed record ReminderRequest(string? Title, DateTimeOffset DueAt);
public sealed record ReminderDto(Guid Id, string Title, DateTimeOffset DueAt, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record ConditionWatchDto(Guid Id, string Title, string Url, string JsonPath, string Comparison,
    double Threshold, int IntervalMinutes, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? LastCheckedAt, double? LastValue);
public sealed record JarvisTaskDto(Guid Id, string Title, string Prompt, string Status, Guid ConversationId,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? Summary);
public sealed record NotificationDto(Guid Id, string Type, string Title, string Body, Guid? SourceId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record PushDeviceRequest(string? Token, string? Platform);
public sealed record SaveIntegrationCredentialsRequest(Dictionary<string, string> Secrets);
public sealed record SaveIntegrationSecretRequest(string Value);
public sealed record AuditEventDto(Guid Id, Guid? AgentRunId, string Tool, string Action, string RiskClass,
    Guid? ApprovalId, DateTimeOffset Timestamp, bool Success, string? MetadataJson);
public sealed record FileDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Sha256,
    DateTimeOffset CreatedAt, string ProcessingStatus);
public sealed record FileSearchHitDto(Guid FileId, string FileName, int ChunkIndex, string Content, double Score);
public sealed record MemoryRequest(string? Kind, string? Content, float Importance = 0.5f, float Confidence = 0.8f,
    DateTimeOffset? ValidUntil = null, bool IsPinned = false);
public sealed record MemoryDto(Guid Id, string Kind, string Content, float Importance, float Confidence,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ValidUntil, bool IsPinned, string? SourceType);
public sealed record MemoryHitDto(MemoryDto Memory, double Score);
