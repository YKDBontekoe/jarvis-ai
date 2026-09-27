using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Jarvis.Api.Realtime;
using Jarvis.Application.Conversations;
using Jarvis.Application.Security;
using Jarvis.Application.Workflows;
using Microsoft.IdentityModel.Tokens;

namespace Jarvis.Api.Endpoints;

internal static class VoiceEndpoints
{
    public static RouteGroupBuilder MapVoiceEndpoints(this RouteGroupBuilder api, ILogger logger)
    {
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
                logger.LogError(exception, "Could not dispatch LiveKit voice worker for conversation {ConversationId}.",
                    request.ConversationId);
                return Results.Problem("Voice service is temporarily unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var identity = Guid.CreateVersion7().ToString("N");
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
            var token = CreateRoomToken(apiKey, apiSecret, room, identity, expiresAt);
            return Results.Ok(new VoiceSessionDto(serverUrl!, room, identity, token, expiresAt));
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
                return EndpointHelpers.Invalid("transcript", "Transcript must contain 1 to 32,000 characters.");
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
                    await WriteStreamEventAsync(stream, new { type = "delta", text = delta }, ct);
                var responseText = await completion;
                await WriteStreamEventAsync(stream, new { type = "done", responseText }, ct);
            }, contentType: "application/x-ndjson");
        }).WithName("ProcessVoiceTranscript").AllowAnonymous();

        return api;
    }

    private static string CreateRoomToken(string apiKey, string apiSecret, string room, string identity,
        DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret)),
            SecurityAlgorithms.HmacSha256);
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
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task WriteStreamEventAsync(Stream stream, object value, CancellationToken cancellationToken)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(value);
        await stream.WriteAsync(data, cancellationToken);
        await stream.WriteAsync(new byte[] { (byte)'\n' }, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
