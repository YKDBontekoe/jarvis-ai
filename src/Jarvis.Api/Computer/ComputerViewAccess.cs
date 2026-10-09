using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Jarvis.Api.Computer;

/// <summary>Who may watch which computer session: the owner of the session, while it is active.</summary>
public sealed record ComputerViewGrant(Guid OwnerId, Guid SessionId);

/// <summary>
/// Live-view access for the sandbox desktop. The app (authenticated) asks for a one-time ticket; the viewer page
/// redeems it once for a short-lived cookie scoped to <see cref="CookiePath"/>, which then authorizes the noVNC files
/// and the VNC WebSocket. A webview cannot send the bearer token on those requests, so this replaces it.
/// </summary>
public sealed class ComputerViewAccess(IDataProtectionProvider protection, IMemoryCache redeemed, TimeProvider time)
{
    public const string CookieName = "jarvis_computer_view";
    public const string CookiePath = "/api/v1/computer-view";
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector _tickets =
        protection.CreateProtector("Jarvis.ComputerView.Ticket.v1").ToTimeLimitedDataProtector();
    private readonly ITimeLimitedDataProtector _cookies =
        protection.CreateProtector("Jarvis.ComputerView.Cookie.v1").ToTimeLimitedDataProtector();

    public string IssueTicket(ComputerViewGrant grant)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return _tickets.Protect($"{grant.OwnerId:N}:{grant.SessionId:N}:{nonce}",
            time.GetUtcNow() + TicketLifetime);
    }

    /// <summary>The grant behind a ticket, once; a second redemption or an expired ticket returns null.</summary>
    public ComputerViewGrant? RedeemTicket(string? ticket)
    {
        if (Unprotect(_tickets, ticket) is not { Length: 3 } parts) return null;
        var nonce = parts[2];
        lock (redeemed)
        {
            if (redeemed.TryGetValue(CacheKey(nonce), out _)) return null;
            redeemed.Set(CacheKey(nonce), true, TicketLifetime + TimeSpan.FromMinutes(1));
        }

        return Grant(parts);
    }

    public string IssueCookie(ComputerViewGrant grant) =>
        _cookies.Protect($"{grant.OwnerId:N}:{grant.SessionId:N}", time.GetUtcNow() + CookieLifetime);

    public ComputerViewGrant? ReadCookie(string? cookie) =>
        Unprotect(_cookies, cookie) is { Length: 2 } parts ? Grant(parts) : null;

    private static string CacheKey(string nonce) => "computer-view-ticket:" + nonce;

    private static string[]? Unprotect(ITimeLimitedDataProtector protector, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2_000) return null;
        try
        {
            return protector.Unprotect(value, out _).Split(':');
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static ComputerViewGrant? Grant(string[] parts) =>
        Guid.TryParseExact(parts[0], "N", out var owner) && Guid.TryParseExact(parts[1], "N", out var session)
            ? new ComputerViewGrant(owner, session)
            : null;
}
