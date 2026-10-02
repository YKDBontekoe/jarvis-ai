using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace Jarvis.Api.Security;

/// <summary>
/// Per-client-IP limits for the endpoints that are reachable without a signed-in session: account sign-in,
/// OAuth callbacks, the Agent2Agent JSON-RPC endpoint, and channel webhooks. Authenticated owner traffic is
/// not limited here.
/// </summary>
public static class ApiRateLimiting
{
    public const string AuthPolicy = "auth";
    public const string PublicPolicy = "public";

    public static IServiceCollection AddJarvisRateLimiting(this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("RateLimiting");
        var authPerMinute = Math.Max(1, section.GetValue("AuthPermitsPerMinute", 10));
        var publicPerMinute = Math.Max(1, section.GetValue("PublicPermitsPerMinute", 120));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
            options.AddPolicy(AuthPolicy, context => PerClient(context, authPerMinute));
            options.AddPolicy(PublicPolicy, context => PerClient(context, publicPerMinute));
        });

        // Behind the production reverse proxy every request arrives from the proxy's address. Only the last
        // X-Forwarded-For hop (the one the proxy appended) is honored, so a client cannot spoof its own address.
        if (configuration.GetValue("ReverseProxy:TrustForwardedHeaders", false))
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        return services;
    }

    public static WebApplication UseJarvisRateLimiting(this WebApplication app)
    {
        if (app.Configuration.GetValue("ReverseProxy:TrustForwardedHeaders", false))
            app.UseForwardedHeaders();
        app.UseRateLimiter();
        return app;
    }

    internal static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> PerClient(HttpContext context, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
}
