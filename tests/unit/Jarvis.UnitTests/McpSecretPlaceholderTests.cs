using System.Text.Json;
using System.Text.Json.Nodes;
using Jarvis.Application.Integrations;
using Jarvis.Mcp;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class McpSecretPlaceholderTests
{
    private static Dictionary<string, object?> Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
    }

    private static string Serialize(Dictionary<string, object?> arguments) =>
        JsonSerializer.Serialize(arguments);

    [Fact]
    public void A_whole_string_placeholder_is_replaced_with_the_stored_value()
    {
        var arguments = Arguments("""{"email":"me@example.com","password":"{{secret:password}}"}""");

        var resolved = McpSecretPlaceholders.Resolve(arguments,
            new Dictionary<string, string> { ["password"] = "s3cret \"quoted\"" }, out var missing);

        Assert.Empty(missing);
        var json = JsonNode.Parse(Serialize(resolved))!;
        Assert.Equal("s3cret \"quoted\"", (string?)json["password"]);
        Assert.Equal("me@example.com", (string?)json["email"]);
    }

    [Fact]
    public void Placeholders_inside_nested_objects_and_arrays_are_replaced()
    {
        var arguments = Arguments("""
            {"account":{"auth":{"pass":"{{secret:password}}"},"hosts":["imap.example.com","x-{{secret:api_key}}"]},"port":993,"ssl":true}
            """);

        var resolved = McpSecretPlaceholders.Resolve(arguments,
            new Dictionary<string, string> { ["password"] = "pw", ["api_key"] = "k1" }, out var missing);

        Assert.Empty(missing);
        var json = JsonNode.Parse(Serialize(resolved))!;
        Assert.Equal("pw", (string?)json["account"]!["auth"]!["pass"]);
        Assert.Equal("x-k1", (string?)json["account"]!["hosts"]![1]);
        Assert.Equal(993, (int?)json["port"]);
        Assert.True((bool?)json["ssl"]);
    }

    [Fact]
    public void Names_lists_each_referenced_secret_once()
    {
        var arguments = Arguments("""{"a":"{{secret:password}}","b":["{{secret:password}}","{{secret:api_key}}"]}""");

        Assert.Equal(["password", "api_key"], McpSecretPlaceholders.Names(arguments));
    }

    [Fact]
    public void A_secret_that_is_not_stored_is_reported_and_left_unsubstituted()
    {
        var arguments = Arguments("""{"password":"{{secret:password}}"}""");

        var resolved = McpSecretPlaceholders.Resolve(arguments, new Dictionary<string, string>(), out var missing);

        Assert.Equal(["password"], missing);
        Assert.Contains("{{secret:password}}", Serialize(resolved));
    }

    [Fact]
    public void A_server_with_no_stored_secrets_reports_every_placeholder_missing()
    {
        var arguments = Arguments("""{"password":"{{secret:password}}"}""");

        McpSecretPlaceholders.Resolve(arguments, null, out var missing);

        Assert.Equal(["password"], missing);
    }

    [Theory]
    [InlineData("server_config")]
    [InlineData("oauth_refresh_token")]
    [InlineData("oauth_token_endpoint")]
    public void Internal_secrets_can_never_be_requested(string name)
    {
        var arguments = Arguments("{\"value\":\"{{secret:" + name + "}}\"}");

        var resolved = McpSecretPlaceholders.Resolve(arguments,
            new Dictionary<string, string> { [name] = "do-not-leak" }, out var missing);

        Assert.Equal([name], missing);
        Assert.DoesNotContain("do-not-leak", Serialize(resolved));
    }

    [Fact]
    public void A_stored_value_that_looks_like_a_placeholder_is_not_expanded_again()
    {
        var arguments = Arguments("""{"a":"{{secret:one}}"}""");

        var resolved = McpSecretPlaceholders.Resolve(arguments,
            new Dictionary<string, string> { ["one"] = "{{secret:two}}", ["two"] = "other" }, out var missing);

        Assert.Empty(missing);
        Assert.Equal("{{secret:two}}", (string?)JsonNode.Parse(Serialize(resolved))!["a"]);
    }

    [Theory]
    [InlineData("""{"a":"plain"}""", false)]
    [InlineData("""{"a":"{{secret:password}}"}""", true)]
    [InlineData("""{"a":"{{secret:Bad Name}}"}""", false)]
    [InlineData("""{"a":"{{ secret:password }}"}""", false)]
    public void Contains_only_matches_well_formed_placeholders(string json, bool expected) =>
        Assert.Equal(expected, McpSecretPlaceholders.Contains(json));
}

/// <summary>Drives a real stdio MCP server through <see cref="McpToolHost"/> to prove the secret reaches the tool.</summary>
[Collection(nameof(McpProcessEnvironment))]
public sealed class McpSecretInjectionTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private static readonly string ServerScript = """
        const readline = require('node:readline');
        const tool = { name: 'imap_add_account', description: 'Add a mail account.',
          inputSchema: { type: 'object', properties: { email: { type: 'string' }, password: { type: 'string' } },
            required: ['email', 'password'] } };
        readline.createInterface({ input: process.stdin }).on('line', line => {
          let request; try { request = JSON.parse(line); } catch { return; }
          if (request.id === undefined) return;
          let result;
          if (request.method === 'initialize')
            result = { protocolVersion: request.params.protocolVersion, capabilities: { tools: {} },
              serverInfo: { name: 'imap-fixture', version: '1.0.0' } };
          else if (request.method === 'tools/list') result = { tools: [tool] };
          else if (request.method === 'tools/call') {
            const args = request.params.arguments || {};
            const ok = args.password === 'correct horse battery staple';
            const text = args.password === undefined ? 'error: password is required'
              : ok ? 'added ' + args.email + ' with ' + args.password : 'error: wrong password';
            result = { content: [{ type: 'text', text }], isError: !ok };
          } else if (request.method === 'ping') result = {};
          else {
            process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: request.id,
              error: { code: -32601, message: 'Method not found' } }) + '\n');
            return;
          }
          process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: request.id, result }) + '\n');
        });
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("jarvis-mcp-secret-").FullName;
    private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");

    public McpSecretInjectionTests()
    {
        File.WriteAllText(Path.Combine(_directory, "server.js"), ServerScript);
        var launcher = Path.Combine(_directory, "npx");
        File.WriteAllText(launcher, $"#!/bin/sh\nexec node \"{Path.Combine(_directory, "server.js")}\"\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("PATH", _directory + Path.PathSeparator + _originalPath);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static async Task<(McpToolHost Host, InMemoryCredentialStore Store, Guid Owner, string ServerId)> SetUpAsync()
    {
        var store = new InMemoryCredentialStore();
        var registry = new Jarvis.Infrastructure.Persistence.UserMcpServerRegistry(store,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Jarvis.Infrastructure.Persistence.UserMcpServerRegistry>.Instance);
        var owner = Guid.NewGuid();
        var server = await registry.AddDefinitionAsync(owner, new McpServerDefinition("Mail", "stdio", null, "npx",
            ["-y", "imap-mcp-server@1.0.0"], ["*"], []), CancellationToken.None);
        var host = new McpToolHost(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<McpToolHost>.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, new Owner(owner), store, registry,
            null!, null!);
        return (host, store, owner, server.Id);
    }

    [Fact]
    public async Task Without_a_placeholder_the_tool_is_missing_its_password()
    {
        var (host, _, _, id) = await SetUpAsync();
        await using var _ = host;

        var result = await host.InvokeToolAsync(id, "imap_add_account", """{"email":"me@example.com"}""",
            CancellationToken.None);

        Assert.Contains("password is required", result);
    }

    [Fact]
    public async Task A_saved_password_is_filled_in_for_the_tool_and_redacted_from_the_result()
    {
        var (host, store, owner, id) = await SetUpAsync();
        await using var _ = host;
        await store.SaveSecretAsync(owner, id, "password", Password, CancellationToken.None);

        var result = await host.InvokeToolAsync(id, "imap_add_account",
            """{"email":"me@example.com","password":"{{secret:password}}"}""", CancellationToken.None);

        Assert.Contains("added me@example.com with [REDACTED]", result);
        Assert.DoesNotContain(Password, result);
        Assert.DoesNotContain("isError\":true", result);
    }

    [Fact]
    public async Task A_missing_secret_tells_the_agent_to_ask_the_user_and_never_calls_the_tool()
    {
        var (host, _, _, id) = await SetUpAsync();
        await using var _ = host;

        var result = await host.InvokeToolAsync(id, "imap_add_account",
            """{"email":"me@example.com","password":"{{secret:password}}"}""", CancellationToken.None);

        Assert.Contains("AskForMcpCredential", result);
        Assert.Contains(id, result);
        Assert.Contains("'password'", result);
        Assert.DoesNotContain("added", result);
    }

    [Fact]
    public async Task Another_owners_saved_secret_is_not_used()
    {
        var (host, store, _, id) = await SetUpAsync();
        await using var _ = host;
        await store.SaveSecretAsync(Guid.NewGuid(), id, "password", Password, CancellationToken.None);

        var result = await host.InvokeToolAsync(id, "imap_add_account",
            """{"email":"me@example.com","password":"{{secret:password}}"}""", CancellationToken.None);

        Assert.Contains("AskForMcpCredential", result);
    }

    private sealed class Owner(Guid ownerId) : Jarvis.Application.Conversations.ICurrentUser
    {
        public Guid OwnerId { get; } = ownerId;
    }
}

[CollectionDefinition(nameof(McpProcessEnvironment), DisableParallelization = true)]
public sealed class McpProcessEnvironment;
