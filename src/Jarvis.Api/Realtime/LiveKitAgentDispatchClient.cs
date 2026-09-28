using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Jarvis.Api.Realtime;

public sealed class LiveKitAgentDispatchClient(HttpClient httpClient, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task DispatchAsync(string room, Guid conversationId, Guid ownerId, string voice,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["LiveKit:ApiKey"];
        var apiSecret = configuration["LiveKit:ApiSecret"];
        var serverUrl = configuration["LiveKit:InternalUrl"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret) ||
            !Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("LiveKit internal server settings are missing or invalid.");

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(2);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Iss, apiKey),
            new(JwtRegisteredClaimNames.Sub, "jarvis-api"),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Nbf, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Exp, expiresAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("video", JsonSerializer.Serialize(new { roomAdmin = true, room }, JsonOptions), JsonClaimValueTypes.Json)
        };
        var token = new JwtSecurityToken(new JwtHeader(credentials), new JwtPayload(claims));

        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(uri, "/twirp/livekit.AgentDispatchService/CreateDispatch"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            agentName = "jarvis-voice",
            room,
            metadata = JsonSerializer.Serialize(new { conversationId, ownerId, voice }, JsonOptions)
        }, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"LiveKit agent dispatch failed ({(int)response.StatusCode}): {body}");
        }

        // A dispatch acknowledgement does not mean the worker's Codex audio transport is usable.
        // Room attributes are shared by LiveKit, so readiness also works across API replicas.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var participantsRequest = new HttpRequestMessage(HttpMethod.Post,
                new Uri(uri, "/twirp/livekit.RoomService/ListParticipants"));
            participantsRequest.Headers.Authorization = request.Headers.Authorization;
            participantsRequest.Content = new StringContent(JsonSerializer.Serialize(new { room }, JsonOptions),
                Encoding.UTF8, "application/json");
            using var participantsResponse = await httpClient.SendAsync(participantsRequest, cancellationToken);
            if (participantsResponse.IsSuccessStatusCode)
            {
                using var participants = JsonDocument.Parse(await participantsResponse.Content.ReadAsStringAsync(cancellationToken));
                if (participants.RootElement.TryGetProperty("participants", out var items))
                    foreach (var participant in items.EnumerateArray())
                        if (participant.TryGetProperty("attributes", out var attributes) &&
                            attributes.TryGetProperty("jarvis.voice.status", out var status))
                        {
                            if (status.GetString() == "ready") return;
                            if (status.GetString() == "unavailable")
                                throw new InvalidOperationException("The voice worker could not initialize its Codex audio transport.");
                        }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException("The voice worker did not become ready before the session deadline.");
    }
}
