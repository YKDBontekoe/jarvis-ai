using Jarvis.Application.Integrations;
using Jarvis.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class UserMcpServerRegistryTests
{
    private const string SecretValue = "do-not-disclose-token";

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"name\":\"Broken\",\"endpoint\":\"https://example.com/mcp\",\"allowedTools\":[\"read\"],\"transport\":\"websocket\"}")]
    [InlineData("{\"name\":\"Broken\",\"endpoint\":\"https://example.com/mcp\"}")]
    public async Task List_returns_sanitized_disabled_entry_for_invalid_configuration(string configuration)
    {
        var (registry, store, logger, owner, id) = CreateRegistry();
        await SaveAsync(store, owner, id, configuration);

        var server = Assert.Single(await registry.ListAsync(owner, CancellationToken.None));

        Assert.False(server.IsValid);
        Assert.False(server.Enabled);
        Assert.False(server.HasToken);
        Assert.Equal("invalid_configuration", server.ConfigurationIssue);
        Assert.Empty(server.Endpoint);
        Assert.DoesNotContain(SecretValue, logger.Text, StringComparison.Ordinal);
        Assert.Contains(owner.ToString(), logger.Text, StringComparison.Ordinal);
        Assert.Contains(id, logger.Text, StringComparison.Ordinal);
        Assert.Contains("Exception", logger.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_accepts_legacy_configuration_without_enabled_or_transport()
    {
        var (registry, store, _, owner, id) = CreateRegistry();
        await SaveAsync(store, owner, id,
            """{"name":"Legacy","endpoint":"https://example.com/mcp","allowedTools":["read"]}""");

        var server = Assert.Single(await registry.ListAsync(owner, CancellationToken.None));

        Assert.True(server.IsValid);
        Assert.True(server.Enabled);
        Assert.Equal("streamableHttp", server.Transport);
    }

    [Fact]
    public async Task State_and_tool_mutations_refuse_invalid_configuration()
    {
        var (registry, store, _, owner, id) = CreateRegistry();
        await SaveAsync(store, owner, id, "{bad-json");

        await Assert.ThrowsAsync<InvalidMcpServerConfigurationException>(() =>
            registry.SetEnabledAsync(owner, id, true, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidMcpServerConfigurationException>(() =>
            registry.SetToolsAsync(owner, id, "replace", ["read"], CancellationToken.None));
    }

    [Fact]
    public async Task Explicit_replacement_recovers_invalid_configuration_without_preserving_state()
    {
        var (registry, store, _, owner, id) = CreateRegistry();
        await SaveAsync(store, owner, id, "{bad-json");

        var replaced = await registry.UpdateAsync(owner, id,
            new AddUserMcpServerRequest("Recovered", "https://example.com/mcp", ["read"]),
            CancellationToken.None);

        Assert.NotNull(replaced);
        Assert.True(replaced.Enabled);
        Assert.True(replaced.IsValid);
        Assert.Equal("Recovered", replaced.Name);
    }

    [Fact]
    public async Task Delete_removes_invalid_configuration()
    {
        var (registry, store, _, owner, id) = CreateRegistry();
        await SaveAsync(store, owner, id, "{bad-json");

        Assert.True(await registry.RemoveAsync(owner, id, CancellationToken.None));
        Assert.Empty(await registry.ListAsync(owner, CancellationToken.None));
    }

    private static (UserMcpServerRegistry Registry, InMemoryCredentialStore Store, RecordingLogger Logger,
        Guid Owner, string Id) CreateRegistry()
    {
        var store = new InMemoryCredentialStore();
        var logger = new RecordingLogger();
        return (new UserMcpServerRegistry(store, logger), store, logger, Guid.NewGuid(),
            IntegrationCredentialProviders.UserMcpPrefix + Guid.NewGuid().ToString("N"));
    }

    private static async Task SaveAsync(InMemoryCredentialStore store, Guid owner, string id, string configuration)
    {
        await store.SaveSecretAsync(owner, id, IntegrationCredentialProviders.UserMcpConfigSecret, configuration,
            CancellationToken.None);
        await store.SaveSecretAsync(owner, id, IntegrationCredentialProviders.UserMcpTokenSecret, SecretValue,
            CancellationToken.None);
    }

    private sealed class RecordingLogger : ILogger<UserMcpServerRegistry>
    {
        private readonly List<string> _entries = [];
        public string Text => string.Join('\n', _entries);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _entries.Add($"{formatter(state, exception)} {exception?.GetType().Name}");
    }
}
