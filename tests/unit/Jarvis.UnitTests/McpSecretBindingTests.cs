using Jarvis.Application.Integrations;
using Jarvis.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class McpSecretBindingTests
{
    [Theory]
    [InlineData("NOTION_TOKEN", true)]
    [InlineData("BRAVE_API_KEY", true)]
    [InlineData("PATH", false)]
    [InlineData("NODE_OPTIONS", false)]
    [InlineData("LD_PRELOAD", false)]
    [InlineData("PYTHONPATH", false)]
    [InlineData("HTTPS_PROXY", false)]
    [InlineData("BAD-NAME", false)]
    public void Environment_names_block_variables_that_change_how_code_loads(string name, bool allowed) =>
        Assert.Equal(allowed, McpSecretBindings.IsAllowedEnvironmentName(name));

    [Theory]
    [InlineData("Authorization", true)]
    [InlineData("X-API-Key", true)]
    [InlineData("Host", false)]
    [InlineData("Cookie", false)]
    [InlineData("Mcp-Session-Id", false)]
    [InlineData("X-Forwarded-For", false)]
    public void Header_names_block_transport_headers(string name, bool allowed) =>
        Assert.Equal(allowed, McpSecretBindings.IsAllowedHeaderName(name));

    [Theory]
    [InlineData("NOTION_TOKEN", "notion_token")]
    [InlineData("X-API-Key", "x_api_key")]
    [InlineData("2FA_CODE", "secret_2fa_code")]
    public void Secret_names_are_derived_from_the_variable_or_header(string key, string expected) =>
        Assert.Equal(expected, McpSecretBindings.SecretNameFor(key));

    [Fact]
    public void Stdio_servers_take_environment_secrets_only()
    {
        var header = new McpSecretBinding("api_key", McpSecretBindings.HeaderTarget, "X-API-Key", null, "Key", null, true);

        Assert.Throws<ArgumentException>(() => McpSecretBindings.Normalize([header], "stdio"));
    }

    [Fact]
    public void Duplicate_and_reserved_secret_names_are_rejected()
    {
        var first = new McpSecretBinding("token_a", "env", "A_TOKEN", null, "A", null, true);
        var duplicate = first with { Key = "B_TOKEN" };
        var reserved = first with { SecretName = IntegrationCredentialProviders.UserMcpConfigSecret };

        Assert.Throws<ArgumentException>(() => McpSecretBindings.Normalize([first, duplicate], "stdio"));
        Assert.Throws<ArgumentException>(() => McpSecretBindings.Normalize([reserved], "stdio"));
    }

    [Fact]
    public async Task Registry_stores_declared_secrets_and_reports_which_are_set()
    {
        var store = new InMemoryCredentialStore();
        var registry = new UserMcpServerRegistry(store, NullLogger<UserMcpServerRegistry>.Instance);
        var owner = Guid.NewGuid();
        var added = await registry.AddDefinitionAsync(owner, new McpServerDefinition("Notion", "stdio", null, "npx",
            ["-y", "@notionhq/notion-mcp-server@1.9.0"], ["*"],
            [
                new McpSecretBinding("notion_token", "env", "NOTION_TOKEN", null, "Notion token", "From notion.so", true),
                new McpSecretBinding("notion_version", "env", "NOTION_VERSION", null, "API version", null, false),
            ], "io.github.makenotion/notion-mcp-server"), CancellationToken.None);

        await store.SaveSecretAsync(owner, added.Id, "notion_token", "secret-value", CancellationToken.None);
        var listed = Assert.Single(await registry.ListAsync(owner, CancellationToken.None));

        Assert.Equal("io.github.makenotion/notion-mcp-server", listed.CatalogName);
        Assert.Collection(listed.Secrets!,
            token => Assert.True(token is { Name: "notion_token", Required: true, IsSet: true }),
            version => Assert.True(version is { Name: "notion_version", Required: false, IsSet: false }));
        Assert.Equal("NOTION_TOKEN", listed.SecretBindings![0].Key);
    }

    [Theory]
    [InlineData("@notionhq/notion-mcp-server@1.9.0")]
    [InlineData("mcp-server-fetch@2025.4.7")]
    public void Pinned_package_specs_are_accepted(string package)
    {
        var (_, arguments) = McpStdioCommandValidator.Normalize(package.StartsWith('@') ? "npx" : "uvx",
            package.StartsWith('@') ? ["-y", package] : [package]);

        Assert.Contains(package, arguments);
    }

    [Fact]
    public void Npx_needs_a_real_package_not_only_flags()
    {
        Assert.Throws<ArgumentException>(() => McpStdioCommandValidator.Normalize("npx", ["-y"]));
    }
}
