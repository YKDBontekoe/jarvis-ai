using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Api.Errors;
using Jarvis.Api.Realtime;
using Jarvis.Application.Conversations;
using Jarvis.Application.Security;
using Jarvis.Application.Settings;
using Jarvis.Application.Workflows;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Endpoints;

internal static class VoiceEndpoints
{
    public static RouteGroupBuilder MapVoiceEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
        api.MapPost("/voice/session", async (VoiceSessionRequest request, IConversationStore conversations,
            IJarvisTaskRepository tasks, ICurrentUser currentUser, IConfiguration configuration,
            IOwnerSettingsStore settings, VoiceRuntime voiceRuntime, CodexInstallation codex,
            CancellationToken ct) =>
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
                return ApiProblemResults.DependencyUnavailable("LiveKit is not configured.");

            var room = $"jarvis-{request.ConversationId:N}-{Guid.CreateVersion7():N}";
            var voice = await ResolveVoiceAsync(settings, codex, currentUser.OwnerId, ct);
            try
            {
                await voiceRuntime.StartSessionAsync(room, request.ConversationId, currentUser.OwnerId,
                    voice.Voice, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not start the in-process voice runtime for conversation {ConversationId}.",
                    request.ConversationId);
                return ApiProblemResults.DependencyUnavailable("Voice service is temporarily unavailable.");
            }

            var identity = Guid.CreateVersion7().ToString("N");
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
            var token = LiveKitAccess.CreateRoomToken(apiKey, apiSecret, room, identity, expiresAt);
            return Results.Ok(new VoiceSessionDto(serverUrl!, room, identity, token, expiresAt, voice.HandsFree,
                voice.Captions, voice.Voice));
        }).WithName("CreateVoiceSession");

        api.MapGet("/settings/voice", async (IOwnerSettingsStore settings, ICurrentUser currentUser,
                CodexInstallation codex, CancellationToken ct) =>
                Results.Ok(await ToDtoAsync(settings, codex, currentUser.OwnerId, ct)))
            .WithName("GetVoiceSettings");

        api.MapPut("/settings/voice", async (VoiceSettings request, IOwnerSettingsStore settings,
            ICurrentUser currentUser, CodexInstallation codex, CancellationToken ct) =>
        {
            var current = await settings.GetAsync<VoiceSettings>(currentUser.OwnerId, SettingsSections.Voice, ct)
                          ?? VoiceSettings.Default;
            var status = await codex.GetStatusAsync(ct);
            var requested = string.IsNullOrWhiteSpace(request.Voice) ? current.Voice : request.Voice;
            if (status.Voices.Voices.Count > 0 && requested is not null &&
                !status.Voices.Supports(requested))
                return EndpointHelpers.Invalid("voice", "The installed Codex CLI does not support that voice.");
            var selected = VoiceSelection.Resolve(requested, status.Voices.Voices.Select(voice => voice.Id),
                status.Voices.DefaultVoice);
            await settings.SaveAsync(currentUser.OwnerId, SettingsSections.Voice,
                new VoiceSettings(request.HandsFree, request.Captions, selected), ct);
            return Results.Ok(await ToDtoAsync(settings, codex, currentUser.OwnerId, ct));
        }).WithName("SaveVoiceSettings");

        api.MapPost("/voice/internal/{conversationId:guid}/caption", async (Guid conversationId,
            VoiceCaptionRequest request, HttpRequest httpRequest, IConfiguration configuration,
            IHubContext<JarvisEventsHub> hub, CancellationToken ct) =>
        {
            if (!VoiceWorkerAuthorized(httpRequest, configuration)) return Results.Unauthorized();
            var text = request.Text?.Trim() ?? string.Empty;
            if (text.Length > 2_000) text = text[..2_000];
            var role = request.Role is "assistant" ? "assistant" : "user";
            await hub.Clients.Group(JarvisEventsHub.GroupName(conversationId))
                .SendAsync("voice.caption", new { conversationId, role, text, final = request.Final }, ct);
            return Results.NoContent();
        }).WithName("PublishVoiceCaption").AllowAnonymous();

        api.MapGet("/voice/internal/{conversationId:guid}/session", async (Guid conversationId, Guid ownerId,
            HttpRequest httpRequest, IConfiguration configuration, IConversationStore conversations,
            IJarvisTaskRepository tasks, VoiceBackendSession voice, CancellationToken ct) =>
        {
            if (!VoiceWorkerAuthorized(httpRequest, configuration)) return Results.Unauthorized();
            if (await VoiceConversationUnavailableAsync(conversations, tasks, conversationId, ownerId, ct)
                is { } unavailable)
                return unavailable;
            var snapshot = await voice.GetSessionAsync(ownerId, conversationId, ct);
            return Results.Ok(new VoiceSessionBootstrapDto(snapshot.Instructions,
                snapshot.Tools.Select(tool => new VoiceToolDto(tool.Name, tool.Description, tool.InputSchema,
                    tool.RequiresApproval)).ToArray()));
        }).WithName("GetVoiceBackendSession").AllowAnonymous();

        api.MapPost("/voice/internal/{conversationId:guid}/tools/{toolName}", async (Guid conversationId,
            string toolName, VoiceToolCallRequest request, HttpRequest httpRequest, IConfiguration configuration,
            IConversationStore conversations, IJarvisTaskRepository tasks, VoiceBackendSession voice,
            CancellationToken ct) =>
        {
            if (!VoiceWorkerAuthorized(httpRequest, configuration)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(toolName) || toolName.Length > 200)
                return EndpointHelpers.Invalid("toolName", "A Jarvis tool name is required.");
            if (await VoiceConversationUnavailableAsync(conversations, tasks, conversationId, request.OwnerId, ct)
                is { } unavailable)
                return unavailable;
            var arguments = request.Arguments is { ValueKind: JsonValueKind.Object } json
                ? json.GetRawText()
                : "{}";
            var result = await voice.InvokeAsync(request.OwnerId, conversationId, toolName, arguments, ct);
            return Results.Ok(new VoiceToolCallResultDto(result.Result, result.IsError, result.ApprovalRequired));
        }).WithName("InvokeVoiceBackendTool").AllowAnonymous();

        api.MapPost("/voice/internal/{conversationId:guid}/utterance", async (Guid conversationId,
            VoiceUtteranceRequest request, HttpRequest httpRequest, IConfiguration configuration,
            IConversationStore conversations, IJarvisTaskRepository tasks, VoiceBackendSession voice,
            CancellationToken ct) =>
        {
            if (!VoiceWorkerAuthorized(httpRequest, configuration)) return Results.Unauthorized();
            var text = request.Text?.Trim() ?? string.Empty;
            if (request.OwnerId == Guid.Empty || string.IsNullOrWhiteSpace(text) || text.Length > 32_000)
                return EndpointHelpers.Invalid("text", "Utterance must contain 1 to 32,000 characters.");
            if (await VoiceConversationUnavailableAsync(conversations, tasks, conversationId, request.OwnerId, ct)
                is { } unavailable)
                return unavailable;
            await voice.PersistUtteranceAsync(request.OwnerId, conversationId, request.Role ?? "user", text, ct);
            return Results.NoContent();
        }).WithName("PersistVoiceUtterance").AllowAnonymous();

        return api;
    }

    private static bool VoiceWorkerAuthorized(HttpRequest httpRequest, IConfiguration configuration)
    {
        var expectedSecret = configuration["Voice:WorkerSecret"];
        var suppliedSecret = httpRequest.Headers["X-Jarvis-Voice-Secret"].ToString();
        return SecretComparer.FixedTimeEquals(expectedSecret, suppliedSecret);
    }

    private static async Task<IResult?> VoiceConversationUnavailableAsync(IConversationStore conversations,
        IJarvisTaskRepository tasks, Guid conversationId, Guid ownerId, CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty) return Results.Unauthorized();
        if (await conversations.GetAsync(conversationId, ownerId, cancellationToken) is null)
            return Results.NotFound();
        if (await tasks.GetTaskByConversationIdAsync(conversationId, ownerId, cancellationToken) is not null)
            return Results.Conflict(new { message = "Task conversations cannot use realtime voice." });
        return null;
    }

    private static Task<VoiceSettingsDto> ResolveVoiceAsync(IOwnerSettingsStore settings, CodexInstallation codex,
        Guid ownerId, CancellationToken cancellationToken) =>
        ToDtoAsync(settings, codex, ownerId, cancellationToken);

    private static async Task<VoiceSettingsDto> ToDtoAsync(IOwnerSettingsStore settings, CodexInstallation codex,
        Guid ownerId, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAsync<VoiceSettings>(ownerId, SettingsSections.Voice, cancellationToken)
                     ?? VoiceSettings.Default;
        var status = await codex.GetStatusAsync(cancellationToken);
        var selected = VoiceSelection.Resolve(stored.Voice, status.Voices.Voices.Select(voice => voice.Id),
            status.Voices.DefaultVoice);
        return new VoiceSettingsDto(stored.HandsFree, stored.Captions, selected, status.Voices.DefaultVoice,
            status.Voices.Voices.Select(voice => new CodexVoiceDto(voice.Id, voice.Name, voice.IsDefault)).ToArray(),
            status.VoiceError);
    }
}
