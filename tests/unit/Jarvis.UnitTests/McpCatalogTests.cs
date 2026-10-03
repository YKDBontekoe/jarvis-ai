using System.Text.Json;
using Jarvis.Api.Endpoints;
using Jarvis.Application.Integrations;
using Jarvis.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class McpCatalogTests
{
    private const string BraveNpm = """
        {"name":"io.github.brave/brave-search-mcp-server","description":"Brave Search MCP Server.","version":"2.1.3",
         "repository":{"url":"https://github.com/brave/brave-search-mcp-server","source":"github"},
         "packages":[{"registryType":"npm","identifier":"@brave/brave-search-mcp-server","version":"2.1.3",
           "transport":{"type":"stdio"},
           "environmentVariables":[{"name":"BRAVE_API_KEY","description":"Your API key","isRequired":true,"isSecret":true}]}]}
        """;

    private const string SmitheryRemote = """
        {"name":"ai.smithery/brave","description":"Search the web.","version":"2.0.58",
         "remotes":[{"type":"streamable-http","url":"https://server.smithery.ai/brave/mcp",
           "headers":[{"name":"Authorization","value":"Bearer {smithery_api_key}","isRequired":true,"isSecret":true,
             "description":"Bearer token for Smithery"}]}]}
        """;

    [Fact]
    public void Npm_package_becomes_a_pinned_npx_server_with_its_key()
    {
        var entry = Map(BraveNpm)!;

        var option = Assert.Single(entry.Options);
        Assert.Equal("Brave Search", entry.Title);
        Assert.Equal("npm", option.Kind);
        Assert.Equal("@brave/brave-search-mcp-server@2.1.3", option.Summary);
        Assert.Equal("npx", option.Definition.Command);
        Assert.Equal(["-y", "@brave/brave-search-mcp-server@2.1.3"], option.Definition.Arguments);
        Assert.Equal("io.github.brave/brave-search-mcp-server", option.Definition.CatalogName);
        var secret = Assert.Single(option.Secrets);
        Assert.Equal("brave_api_key", secret.Name);
        Assert.True(secret.Required);
        Assert.Equal(new McpSecretBinding("brave_api_key", "env", "BRAVE_API_KEY", null, "BRAVE_API_KEY",
            "Your API key", true), Assert.Single(option.Definition.Secrets));
    }

    [Fact]
    public void Remote_header_template_becomes_a_prefixed_header_secret()
    {
        var option = Assert.Single(Map(SmitheryRemote)!.Options);

        Assert.Equal("remote", option.Kind);
        Assert.Equal("streamableHttp", option.Definition.Transport);
        Assert.Equal("https://server.smithery.ai/brave/mcp", option.Definition.Endpoint);
        var binding = Assert.Single(option.Definition.Secrets);
        Assert.Equal(("header", "Authorization", "Bearer", "authorization"),
            (binding.Target, binding.Key, binding.Prefix, binding.SecretName));
    }

    [Fact]
    public void Pypi_package_runs_with_uvx_at_its_version()
    {
        var option = Assert.Single(Map("""
            {"name":"com.example/fetch","version":"1.0.2","packages":[
              {"registryType":"pypi","identifier":"mcp-server-fetch","version":"1.0.2","transport":{"type":"stdio"}},
              {"registryType":"oci","identifier":"ghcr.io/example/fetch:1.0.2","transport":{"type":"stdio"}}]}
            """)!.Options);

        Assert.Equal("pypi", option.Kind);
        Assert.Equal("uvx", option.Definition.Command);
        Assert.Equal(["mcp-server-fetch@1.0.2"], option.Definition.Arguments);
    }

    [Theory]
    [InlineData("""{"name":"a/sse","version":"1","remotes":[{"type":"sse","url":"https://example.com/sse"}]}""")]
    [InlineData("""{"name":"a/http","version":"1","remotes":[{"type":"streamable-http","url":"http://example.com/mcp"}]}""")]
    [InlineData("""{"name":"a/fixed","version":"1","remotes":[{"type":"streamable-http","url":"https://example.com/mcp","headers":[{"name":"X-Tenant","value":"acme"}]}]}""")]
    [InlineData("""{"name":"a/env","version":"1","packages":[{"registryType":"npm","identifier":"x","version":"1.0.0","environmentVariables":[{"name":"NODE_OPTIONS","isRequired":true}]}]}""")]
    [InlineData("""{"name":"a/args","version":"1","packages":[{"registryType":"npm","identifier":"x","version":"1.0.0","packageArguments":[{"type":"positional","valueHint":"path","isRequired":true}]}]}""")]
    [InlineData("""{"name":"a/unpinned","version":"1","packages":[{"registryType":"npm","identifier":"x"}]}""")]
    public void Entries_Jarvis_cannot_run_as_listed_are_left_out(string json) => Assert.Null(Map(json));

    [Theory]
    [InlineData(null, "io.github.brave/brave-search-mcp-server", "Brave Search")]
    [InlineData(null, "com.example/github_mcp", "Github")]
    [InlineData(null, "com.example/aws-cost-api", "AWS Cost API")]
    [InlineData("Linear", "app.linear/linear", "Linear")]
    [InlineData(null, "com.notion/mcp", "Notion")]
    public void Display_names_read_like_app_names(string? title, string name, string expected) =>
        Assert.Equal(expected, McpRegistryMapper.DisplayName(title, name));

    [Fact]
    public void Search_ranks_the_publishers_own_server_first()
    {
        McpCatalogEntry Entry(string name, string title) => new(name, title, null, "1", null, null, []);
        var ranked = McpRegistryMapper.Rank(
        [
            Entry("ai.smithery/smithery-notion", "Smithery Notion"),
            Entry("io.github.someone/notion-tools", "Notion Tools"),
            Entry("com.notion/mcp", "Notion"),
        ], "notion");

        Assert.Equal(["com.notion/mcp", "io.github.someone/notion-tools", "ai.smithery/smithery-notion"],
            ranked.Select(entry => entry.Name));
    }

    [Theory]
    [InlineData("https://mcp.notion.com/mcp", "Notion")]
    [InlineData("https://server.smithery.ai/brave/mcp", "Smithery")]
    [InlineData("https://mcp.example.co.uk/mcp", "Example")]
    public void Pasted_addresses_are_named_after_their_host(string endpoint, string expected) =>
        Assert.Equal(expected, McpCatalogEndpoints.NameFromEndpoint(endpoint));

    [Fact]
    public async Task Install_resolves_the_entry_from_the_catalog_and_reports_missing_keys()
    {
        var catalog = new FakeCatalog(Map(BraveNpm)!);
        var registry = new UserMcpServerRegistry(new InMemoryCredentialStore(),
            NullLogger<UserMcpServerRegistry>.Instance);
        var owner = Guid.NewGuid();

        var server = await McpCatalogInstaller.InstallAsync(owner,
            new McpCatalogInstallRequest("io.github.brave/brave-search-mcp-server", null), catalog, registry,
            CancellationToken.None);

        Assert.Equal("Brave Search", server.Name);
        Assert.Equal(["*"], server.AllowedTools);
        Assert.Equal("secrets", McpCatalogInstaller.NextStep(server, usesOAuth: false));
        await Assert.ThrowsAsync<ArgumentException>(() => McpCatalogInstaller.InstallAsync(owner,
            new McpCatalogInstallRequest("com.example/unknown", null), catalog, registry, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => McpCatalogInstaller.InstallAsync(owner,
            new McpCatalogInstallRequest("io.github.brave/brave-search-mcp-server", "remote-9"), catalog, registry,
            CancellationToken.None));
    }

    [Fact]
    public void Next_step_prefers_keys_then_sign_in()
    {
        var server = new UserMcpServer("id", "App", "https://example.com/mcp", ["*"], DateTimeOffset.UtcNow, false);

        Assert.Equal("ready", McpCatalogInstaller.NextStep(server, usesOAuth: false));
        Assert.Equal("sign_in", McpCatalogInstaller.NextStep(server, usesOAuth: true));
        Assert.Equal("secrets", McpCatalogInstaller.NextStep(server with
        {
            Secrets = [new McpServerSecret("key", "Key", null, Required: true, IsSet: false)]
        }, usesOAuth: true));
    }

    private static McpCatalogEntry? Map(string json)
    {
        using var document = JsonDocument.Parse(json);
        return McpRegistryMapper.Map(document.RootElement);
    }

    private sealed class FakeCatalog(params McpCatalogEntry[] entries) : IMcpCatalog
    {
        public Task<McpCatalogPage> SearchAsync(string? query, string? cursor, int limit,
            CancellationToken cancellationToken) => Task.FromResult(new McpCatalogPage(entries, null));

        public Task<McpCatalogEntry?> GetAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(entries.FirstOrDefault(entry => entry.Name == name));
    }
}
