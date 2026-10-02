using System.Net;
using Jarvis.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ApiRateLimitingTests
{
    [Fact]
    public async Task Auth_policy_rejects_requests_over_the_limit_with_retry_after()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPermitsPerMinute"] = "2",
        });
        using var client = new HttpClient { BaseAddress = BaseAddress(app) };

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/auth", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/auth", null)).StatusCode);
        var rejected = await client.PostAsync("/auth", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter is not null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/unlimited")).StatusCode);
    }

    [Fact]
    public async Task Trusted_proxy_partitions_by_the_forwarded_client_address()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPermitsPerMinute"] = "1",
            ["ReverseProxy:TrustForwardedHeaders"] = "true",
        });
        using var client = new HttpClient { BaseAddress = BaseAddress(app) };

        Assert.Equal(HttpStatusCode.OK, (await PostAs(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAs(client, "203.0.113.1")).StatusCode);
        // A spoofed earlier hop is ignored: only the address the proxy appended counts.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAs(client, "198.51.100.9, 203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAs(client, "203.0.113.2")).StatusCode);
    }

    private static Task<HttpResponseMessage> PostAs(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddJarvisRateLimiting(builder.Configuration);
        var app = builder.Build();
        app.UseJarvisRateLimiting();
        app.MapPost("/auth", () => Results.Ok()).RequireRateLimiting(ApiRateLimiting.AuthPolicy);
        app.MapGet("/unlimited", () => Results.Ok());
        await app.StartAsync();
        return app;
    }

    private static Uri BaseAddress(WebApplication app) =>
        new(app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.First());
}
