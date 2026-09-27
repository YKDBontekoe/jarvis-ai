using Jarvis.Application.Integrations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class IntegrationCredentialProviderTests
{
    [Theory]
    [InlineData("jarvis-mcp-0123456789abcdef0123456789abcdef", true)]
    [InlineData("jarvis-mcp-", true)]
    [InlineData("github", false)]
    [InlineData("jarvis-mcp", false)]
    [InlineData("JARVIS-MCP-x", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsUserMcpManaged_only_matches_the_mcp_prefix(string? provider, bool expected)
    {
        Assert.Equal(expected, IntegrationCredentialProviders.IsUserMcpManaged(provider));
    }

    [Theory]
    [InlineData("token", true)]
    [InlineData("server_config", false)]
    [InlineData("TOKEN", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsUserMcpTokenSecret_only_matches_token(string? secretName, bool expected)
    {
        Assert.Equal(expected, IntegrationCredentialProviders.IsUserMcpTokenSecret(secretName));
    }
}
