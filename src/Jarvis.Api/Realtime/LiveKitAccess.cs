using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Jarvis.Api.Realtime;

internal static class LiveKitAccess
{
    public static string ToWebSocketUrl(string serverUrl)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("LiveKit URL is missing or invalid.");
        var builder = new UriBuilder(uri);
        builder.Scheme = uri.Scheme switch
        {
            "http" => "ws",
            "https" => "wss",
            "ws" or "wss" => uri.Scheme,
            _ => throw new InvalidOperationException("LiveKit URL must be ws, wss, http, or https.")
        };
        return builder.Uri.ToString().TrimEnd('/');
    }

    public static string LoopbackApiUrl(IConfiguration configuration)
    {
        var urls = configuration["ASPNETCORE_URLS"];
        if (string.IsNullOrWhiteSpace(urls)) urls = "http://127.0.0.1:5082";
        var first = urls.Split(';', 2)[0].Trim();
        first = first.Replace("://+", "://127.0.0.1", StringComparison.Ordinal)
            .Replace("://*", "://127.0.0.1", StringComparison.Ordinal)
            .Replace("://0.0.0.0", "://127.0.0.1", StringComparison.Ordinal);
        if (!Uri.TryCreate(first, UriKind.Absolute, out var uri))
            return "http://127.0.0.1:5082";
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static string CreateRoomToken(string apiKey, string apiSecret, string room, string identity,
        DateTimeOffset expiresAt, bool canUpdateOwnMetadata = false)
    {
        var now = DateTimeOffset.UtcNow;
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret)),
            SecurityAlgorithms.HmacSha256);
        var video = new Dictionary<string, object?>
        {
            ["roomJoin"] = true,
            ["room"] = room,
            ["canPublish"] = true,
            ["canSubscribe"] = true,
            ["canPublishData"] = true
        };
        if (canUpdateOwnMetadata) video["canUpdateOwnMetadata"] = true;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Iss, apiKey),
            new(JwtRegisteredClaimNames.Sub, identity),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Nbf, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Exp, expiresAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("video", JsonSerializer.Serialize(video), JsonClaimValueTypes.Json)
        };
        var token = new JwtSecurityToken(new JwtHeader(credentials), new JwtPayload(claims));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
