using Jarvis.Infrastructure.Files;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ObjectStorageUrlTests
{
    [Theory]
    [InlineData("http://localhost:8333", "http://localhost:8333")]
    [InlineData("https://s3.example.com/bucket", "https://s3.example.com")]
    [InlineData("tcp://127.0.0.1:8333/", "http://127.0.0.1:8333")]
    [InlineData("tcp://localhost:8333", "http://localhost:8333")]
    public void Normalize_uses_http_for_aspire_tcp_endpoints(string input, string expected) =>
        Assert.Equal(expected, ObjectStorageUrl.Normalize(input));
}
