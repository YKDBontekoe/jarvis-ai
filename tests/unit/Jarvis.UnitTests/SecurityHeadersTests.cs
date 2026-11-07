using Jarvis.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class SecurityHeadersTests
{
    private static DefaultHttpContext ContextFor(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    [Theory]
    [InlineData("/api/v1/conversations")]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/hubs/events")]
    [InlineData("/a2a")]
    public void The_owners_data_is_never_stored_by_a_cache(string path)
    {
        var context = ContextFor(path);

        SecurityHeaders.Apply(context);

        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("same-origin", context.Response.Headers["Referrer-Policy"].ToString());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/main.dart.js")]
    [InlineData("/.well-known/agent-card.json")]
    public void Web_files_and_the_public_card_keep_their_own_caching(string path)
    {
        var context = ContextFor(path);

        SecurityHeaders.Apply(context);

        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
    }

    [Fact]
    public void A_handlers_own_headers_are_not_overwritten()
    {
        var context = ContextFor("/api/v1/health-ish");
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        SecurityHeaders.Apply(context);

        Assert.Equal("no-cache", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public void Only_the_microphone_camera_and_location_are_allowed_to_the_app_itself()
    {
        var context = ContextFor("/api/v1/x");

        SecurityHeaders.Apply(context);

        var policy = context.Response.Headers["Permissions-Policy"].ToString();
        Assert.Contains("microphone=(self)", policy, StringComparison.Ordinal);
        Assert.Contains("payment=()", policy, StringComparison.Ordinal);
        Assert.Contains("usb=()", policy, StringComparison.Ordinal);
    }
}
