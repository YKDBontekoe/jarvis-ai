using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Jarvis.Api.Computer;
using Jarvis.Application.Browser;
using Jarvis.Application.Computer;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Realtime;
using Jarvis.Infrastructure.Computer;

namespace Jarvis.Api.Endpoints;

public sealed record ComputerViewDto(string ViewerUrl);
public sealed record ComputerControlRequest(string Mode);

/// <summary>
/// Computer use: step screenshots, taking over from Jarvis, and the live noVNC view of the sandbox desktop. The view
/// is proxied here so the sandbox never publishes a port; see <see cref="ComputerViewAccess"/> for how it is authorized.
/// </summary>
internal static partial class ComputerEndpoints
{
    public static RouteGroupBuilder MapComputerEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/browser-sessions/{id:guid}/steps/{ordinal:int}/screenshot", async (Guid id, int ordinal,
            IBrowserSessionStore sessions, IObjectStorage objects, ICurrentUser currentUser, HttpContext http,
            CancellationToken ct) =>
        {
            var session = await sessions.GetAsync(currentUser.OwnerId, id, ct);
            var key = session?.Steps.FirstOrDefault(step => step.Ordinal == ordinal)?.ScreenshotKey;
            if (session is null || !ComputerScreenshots.BelongsTo(key, currentUser.OwnerId, id))
                return Results.NotFound();
            var content = await objects.GetAsync(key!, ct);
            if (content is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=86400, immutable";
            return Results.Stream(content, ComputerScreenshots.MediaTypeOf(key!));
        }).WithName("GetComputerStepScreenshot");

        api.MapPost("/browser-sessions/{id:guid}/view", async (Guid id, IBrowserSessionStore sessions,
            ComputerSandboxOptions options, ComputerViewAccess access, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (options.ViewUrl is null) return Results.NotFound();
            var session = await sessions.GetAsync(currentUser.OwnerId, id, ct);
            if (!IsLive(session)) return Results.NotFound();
            var ticket = access.IssueTicket(new ComputerViewGrant(currentUser.OwnerId, id));
            return Results.Ok(new ComputerViewDto(
                $"{ComputerViewAccess.CookiePath}/start?ticket={Uri.EscapeDataString(ticket)}"));
        }).WithName("CreateComputerView");

        api.MapPost("/browser-sessions/{id:guid}/control", async (Guid id, ComputerControlRequest request,
            IBrowserSessionStore sessions, IRealtimePublisher realtime, ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (!ComputerControlModes.IsKnown(request.Mode))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["mode"] = ["Use agent or user."]
                });
            var session = await sessions.GetAsync(currentUser.OwnerId, id, ct);
            if (!IsLive(session) || !await sessions.SetControlModeAsync(currentUser.OwnerId, id, request.Mode, ct))
                return Results.NotFound();
            await realtime.PublishToConversationAsync(session!.ConversationId, "computer.control",
                new { sessionId = id, controlMode = request.Mode }, ct);
            return Results.NoContent();
        }).WithName("SetComputerControl");

        return api;
    }

    /// <summary>The viewer: anonymous routes authorized by the one-time ticket and then the scoped cookie.</summary>
    public static IEndpointRouteBuilder MapComputerViewEndpoints(this IEndpointRouteBuilder app)
    {
        var view = app.MapGroup(ComputerViewAccess.CookiePath).AllowAnonymous().ExcludeFromDescription();

        view.MapGet("/start", async (string? ticket, ComputerViewAccess access, IBrowserSessionStore sessions,
            HttpContext http, CancellationToken ct) =>
        {
            var grant = access.RedeemTicket(ticket);
            if (grant is null || !IsLive(await sessions.GetAsync(grant.OwnerId, grant.SessionId, ct)))
                return Results.Unauthorized();
            http.Response.Cookies.Append(ComputerViewAccess.CookieName, access.IssueCookie(grant), new CookieOptions
            {
                HttpOnly = true,
                Secure = http.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = ComputerViewAccess.CookiePath,
                MaxAge = ComputerViewAccess.CookieLifetime
            });
            var viewOnly = http.Request.Query["viewOnly"] == "0" ? "0" : "1";
            var path = Uri.EscapeDataString(ComputerViewAccess.CookiePath.TrimStart('/') + "/websockify");
            return Results.Redirect(
                $"{ComputerViewAccess.CookiePath}/vnc.html?autoconnect=1&reconnect=1&resize=scale&view_only={viewOnly}&path={path}");
        });

        view.MapGet("/websockify", async (HttpContext http, ComputerViewAccess access, IBrowserSessionStore sessions,
            ComputerSandboxOptions options, ILoggerFactory loggers) =>
        {
            if (!http.WebSockets.IsWebSocketRequest || options.ViewUrl is null) return Results.BadRequest();
            var grant = await LiveGrantAsync(http, access, sessions);
            if (grant is null) return Results.Unauthorized();
            await ProxyWebSocketAsync(http, grant, sessions, options.ViewUrl,
                loggers.CreateLogger("Jarvis.Api.Computer.View"));
            return Results.Empty;
        });

        view.MapGet("/{**path}", async (string path, HttpContext http, ComputerViewAccess access,
            IBrowserSessionStore sessions, ComputerSandboxOptions options, IHttpClientFactory clients) =>
        {
            if (options.ViewUrl is null || !IsViewerFile(path)) return Results.NotFound();
            if (await LiveGrantAsync(http, access, sessions) is null) return Results.Unauthorized();
            var client = clients.CreateClient("computer-view");
            using var response = await client.GetAsync(new Uri(options.ViewUrl, path), http.RequestAborted);
            if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
            var bytes = await response.Content.ReadAsByteArrayAsync(http.RequestAborted);
            http.Response.Headers.CacheControl = "private, no-cache";
            return Results.Bytes(bytes, response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream");
        });

        return app;
    }

    /// <summary>Plain relative paths into the noVNC client; nothing that could climb out of it.</summary>
    internal static bool IsViewerFile(string? path) =>
        !string.IsNullOrEmpty(path) && path.Length <= 200 && ViewerPath().IsMatch(path) && !path.Contains("..");

    private static bool IsLive(BrowserSessionRecord? session) =>
        session is { Kind: BrowserSessionKinds.Computer, Status: "active" };

    private static async Task<ComputerViewGrant?> LiveGrantAsync(HttpContext http, ComputerViewAccess access,
        IBrowserSessionStore sessions)
    {
        var grant = access.ReadCookie(http.Request.Cookies[ComputerViewAccess.CookieName]);
        if (grant is null) return null;
        return IsLive(await sessions.GetAsync(grant.OwnerId, grant.SessionId, http.RequestAborted)) ? grant : null;
    }

    private static async Task ProxyWebSocketAsync(HttpContext http, ComputerViewGrant grant,
        IBrowserSessionStore sessions, Uri viewUrl, ILogger logger)
    {
        using var upstream = new ClientWebSocket();
        upstream.Options.AddSubProtocol("binary");
        var target = new UriBuilder(viewUrl)
        {
            Scheme = viewUrl.Scheme == "https" ? "wss" : "ws",
            Path = "/websockify"
        }.Uri;
        try
        {
            await upstream.ConnectAsync(target, http.RequestAborted);
        }
        catch (WebSocketException exception)
        {
            logger.LogWarning(exception, "The computer sandbox live view is not reachable.");
            http.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        var subProtocol = http.WebSockets.WebSocketRequestedProtocols.Contains("binary") ? "binary" : null;
        using var client = await http.WebSockets.AcceptWebSocketAsync(subProtocol);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        lifetime.CancelAfter(ComputerViewAccess.CookieLifetime);
        var pumps = new[]
        {
            PumpAsync(client, upstream, lifetime.Token),
            PumpAsync(upstream, client, lifetime.Token),
            WatchSessionAsync(grant, sessions, lifetime.Token)
        };
        await Task.WhenAny(pumps);
        await lifetime.CancelAsync();
        await CloseQuietlyAsync(client);
        await CloseQuietlyAsync(upstream);
        try { await Task.WhenAll(pumps); }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException) { }
    }

    /// <summary>Ends the live view as soon as the session finishes.</summary>
    private static async Task WatchSessionAsync(ComputerViewGrant grant, IBrowserSessionStore sessions,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (!IsLive(await sessions.GetAsync(grant.OwnerId, grant.SessionId, cancellationToken))) return;
        }
    }

    private static async Task PumpAsync(WebSocket from, WebSocket to, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await from.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return;
            await to.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage,
                cancellationToken);
        }
    }

    private static async Task CloseQuietlyAsync(WebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Live view ended.", timeout.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException) { }
    }

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_./-]*$")]
    private static partial Regex ViewerPath();
}
