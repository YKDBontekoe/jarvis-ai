using System.Net;
using Jarvis.Application.Integrations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class McpServerEndpointValidatorTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("198.51.100.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("192.88.99.1", false)]
    [InlineData("192.31.196.1", true)]
    [InlineData("192.175.48.1", true)]
    [InlineData("224.0.0.1", false)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2001:db8:ffff:ffff:ffff:ffff:ffff:ffff", false)]
    [InlineData("2001:db7::1", true)]
    [InlineData("2001:db9::1", true)]
    [InlineData("2001::1", false)]
    [InlineData("2001:2::1", false)]
    [InlineData("2002::1", false)]
    [InlineData("3fff::1", false)]
    [InlineData("5f00::1", false)]
    [InlineData("64:ff9b::808:808", false)]
    [InlineData("64:ff9b:1::1", false)]
    [InlineData("100::1", false)]
    [InlineData("100:0:0:1::1", false)]
    [InlineData("ff02::1", false)]
    public void IsPublic_classifies_public_and_reserved_addresses(string address, bool expected)
    {
        Assert.Equal(expected, McpServerEndpointValidator.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("http://example.com/mcp")]
    [InlineData("file:///tmp/mcp")]
    [InlineData("https://user:password@example.com/mcp")]
    [InlineData("https://example.com/mcp#fragment")]
    [InlineData("https://localhost/mcp")]
    [InlineData("https://node.local/mcp")]
    [InlineData("https://127.0.0.1/mcp")]
    [InlineData("https://[::1]/mcp")]
    public async Task ValidateAsync_rejects_non_public_or_credential_bearing_endpoints(string endpoint)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            McpServerEndpointValidator.ValidateAsync(endpoint, CancellationToken.None));
    }
}
