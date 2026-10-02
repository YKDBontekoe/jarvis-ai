using Jarvis.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class WebClientCacheTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/main.dart.js")]
    public void Homepage_and_stable_urls_revalidate(string path)
    {
        Assert.Equal("no-cache", WebClientHosting.CacheControlFor(new PathString(path)));
    }

    [Theory]
    [InlineData("/r/2feff8565a92c3ea6cafc58ec647cafd22292b31/main.dart.js")]
    [InlineData("/r/2feff8565a92c3ea6cafc58ec647cafd22292b31/assets/AssetManifest.bin")]
    public void Release_assets_can_be_cached(string path)
    {
        Assert.Equal("public, max-age=86400", WebClientHosting.CacheControlFor(new PathString(path)));
    }
}
