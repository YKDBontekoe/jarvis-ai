using Microsoft.AspNetCore.Http;

namespace Jarvis.Api.Hosting;

/// <summary>
/// Headers every response carries. Static web files already set their own; API and hub responses used to carry
/// none, so a proxy or browser could cache a signed-in owner's JSON and a response could be content-sniffed.
/// </summary>
public static class SecurityHeaders
{
    public static void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        if (!headers.ContainsKey("Referrer-Policy")) headers["Referrer-Policy"] = "same-origin";
        // Voice needs the microphone; nothing else needs a sensor, a payment sheet or USB.
        if (!headers.ContainsKey("Permissions-Policy"))
            headers["Permissions-Policy"] = "microphone=(self), camera=(self), geolocation=(self), payment=(), usb=()";
        if (IsPrivateData(context.Request.Path) && !headers.ContainsKey("Cache-Control"))
            headers.CacheControl = "no-store";
    }

    /// <summary>API calls and the event hub carry the owner's data; nothing there may be stored by a cache.</summary>
    public static bool IsPrivateData(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs") || path.StartsWithSegments("/a2a");

    public static IApplicationBuilder UseJarvisSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            // Set when the response starts, so a handler's own Cache-Control wins and error responses are covered.
            context.Response.OnStarting(() =>
            {
                Apply(context);
                return Task.CompletedTask;
            });
            return next(context);
        });
}
