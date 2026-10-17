using Jarvis.Api.Hosting;
using Xunit;

namespace Jarvis.UnitTests;

public class WebClientHostingTests
{
    [Theory]
    [InlineData("/assets/NOTICES", "text/plain; charset=utf-8")]
    [InlineData("/canvaskit/skwasm.wasm", "application/wasm")]
    [InlineData("/flutter_bootstrap.mjs", "text/javascript")]
    [InlineData("/main.dart.js", "text/javascript")]
    public void FlutterFilesHaveAContentType(string path, string expected)
    {
        Assert.True(new FlutterContentTypeProvider().TryGetContentType(path, out var contentType));
        Assert.Equal(expected, contentType);
    }

    [Theory]
    [InlineData("/NOTICES")]
    [InlineData("/assets/OTHER")]
    public void OtherExtensionlessFilesStayUnserved(string path) =>
        Assert.False(new FlutterContentTypeProvider().TryGetContentType(path, out _));
}
