using System.Text.Json;
using Jarvis.Application.Integrations;
using Jarvis.Application.Surfaces;
using Jarvis.Agents;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class McpChatSetupTests
{
    [Fact]
    public void Secret_fields_require_a_chat_managed_provider_and_never_echo_values()
    {
        using var fields = JsonDocument.Parse("""[{"id":"token","label":"Access token","type":"secret"}]""");
        using var actions = JsonDocument.Parse("""[{"id":"save","label":"Save token","style":"primary"}]""");

        var json = UiSurfaceSchema.Normalize("form", "Save a token", "GitHub", null, fields.RootElement,
            actions.RootElement, "github");
        using var schema = JsonDocument.Parse(json);

        Assert.Equal("github", schema.RootElement.GetProperty("credentialProvider").GetString());
        Assert.Equal("secret", schema.RootElement.GetProperty("fields")[0].GetProperty("type").GetString());

        var values = new Dictionary<string, string> { ["token"] = "ghp_super_secret_value" };
        var described = UiSurfaceAnswers.Describe("Save a token", "save", values, schema.RootElement);
        Assert.Contains("saved a credential for 'github'", described);
        Assert.DoesNotContain("ghp_super_secret_value", described);

        var stored = Assert.Single(UiSurfaceAnswers.SecretsToStore(schema.RootElement, values));
        Assert.Equal("github", stored.Provider);
        Assert.Equal("token", stored.SecretName);
        Assert.Equal("ghp_super_secret_value", stored.Value);

        var redacted = UiSurfaceAnswers.Redact(schema.RootElement, values);
        Assert.Equal(UiSurfaceAnswers.StoredMarker, redacted["token"]);
    }

    [Fact]
    public void Secret_fields_without_a_provider_are_rejected()
    {
        using var fields = JsonDocument.Parse("""[{"id":"token","type":"secret"}]""");
        var error = Assert.Throws<ArgumentException>(() =>
            UiSurfaceSchema.Normalize("form", "Token", null, null, fields.RootElement, null));
        Assert.Contains("credentialProvider", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void System_managed_providers_cannot_be_collected_from_chat()
    {
        using var fields = JsonDocument.Parse("""[{"id":"token","type":"secret"}]""");
        var error = Assert.Throws<ArgumentException>(() =>
            UiSurfaceSchema.Normalize("form", "Token", null, null, fields.RootElement, null,
                "jarvis-model-openrouter"));
        Assert.Contains("cannot be collected", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(IntegrationCredentialProviders.AllowsChatSecret("github"));
        Assert.True(IntegrationCredentialProviders.AllowsChatSecret("jarvis-mcp-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        Assert.True(IntegrationCredentialProviders.AllowsChatSecret("jarvis-pack-calendar"));
        Assert.False(IntegrationCredentialProviders.AllowsChatSecret("jarvis-model-openrouter"));
    }

    [Fact]
    public void Authorize_actions_keep_public_https_urls()
    {
        using var actions = JsonDocument.Parse(
            """[{"id":"authorize","label":"Authorize","style":"primary","url":"https://github.com/login/oauth/authorize"}]""");
        var json = UiSurfaceSchema.Normalize("status", "Connect GitHub", null, null, null, actions.RootElement);
        using var schema = JsonDocument.Parse(json);
        Assert.Equal("https://github.com/login/oauth/authorize",
            schema.RootElement.GetProperty("actions")[0].GetProperty("url").GetString());

        using var bad = JsonDocument.Parse(
            """[{"id":"authorize","label":"Authorize","url":"http://evil.example/steal"}]""");
        Assert.Throws<ArgumentException>(() =>
            UiSurfaceSchema.Normalize("status", "Connect", null, null, null, bad.RootElement));
    }

    [Fact]
    public void Setup_catalog_covers_chat_first_paths()
    {
        Assert.Contains(McpSetupCatalog.Options, option => option.Id == "github");
        Assert.Contains(McpSetupCatalog.Options, option => option.Id == "https");
        Assert.Contains(McpSetupCatalog.Options, option => option.Id == "stdio");
        Assert.Contains(McpSetupCatalog.Options, option => option.Id == "manage");
        Assert.Contains("setup card", McpSetupCatalog.Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_instructions_drive_mcp_setup_from_chat()
    {
        var defaults = JarvisAgentFactory.BuildInstructions(null, executingTask: false);
        Assert.Contains("OfferMcpSetup", defaults);
        Assert.Contains("AskForMcpCredential", defaults);
        Assert.Contains("InstallIntegrationPack", defaults);
        Assert.DoesNotContain("Direct the user to Settings → Integrations", defaults);
    }

    [Fact]
    public async Task Unknown_pack_install_is_rejected()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            IntegrationPackInstaller.InstallAsync(Guid.NewGuid(), "nope",
                new InstallIntegrationPackRequest(null, null, null, null, null, null),
                new RejectingCredentials(), new RejectingServers(), CancellationToken.None));
        Assert.Contains("Unknown", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RejectingCredentials : IIntegrationCredentialStore
    {
        public Task<IReadOnlyList<IntegrationCredentialStatus>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IntegrationCredentialStatus>>([]);
        public Task<IntegrationCredentialStatus?> GetStatusAsync(Guid ownerId, string provider, CancellationToken cancellationToken) =>
            Task.FromResult<IntegrationCredentialStatus?>(null);
        public Task SaveAsync(Guid ownerId, string provider, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException());
        public Task SaveSecretAsync(Guid ownerId, string provider, string secretName, string value, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException());
        public Task<bool> DeleteSecretAsync(Guid ownerId, string provider, string secretName, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<IReadOnlyDictionary<string, string>?> GetSecretsAsync(Guid ownerId, string provider, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>?>(null);
        public Task<bool> DeleteAsync(Guid ownerId, string provider, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class RejectingServers : IUserMcpServerRegistry
    {
        public Task<IReadOnlyList<UserMcpServer>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserMcpServer>>([]);
        public Task<UserMcpServer> AddAsync(Guid ownerId, AddUserMcpServerRequest request, CancellationToken cancellationToken) =>
            Task.FromException<UserMcpServer>(new InvalidOperationException());
        public Task<UserMcpServer> AddStdioAsync(Guid ownerId, AddUserMcpStdioServerRequest request, CancellationToken cancellationToken) =>
            Task.FromException<UserMcpServer>(new InvalidOperationException());
        public Task<UserMcpServer?> UpdateAsync(Guid ownerId, string id, AddUserMcpServerRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<UserMcpServer?>(null);
        public Task<UserMcpServer?> SetEnabledAsync(Guid ownerId, string id, bool enabled, CancellationToken cancellationToken) =>
            Task.FromResult<UserMcpServer?>(null);
        public Task<UserMcpServer?> SetToolsAsync(Guid ownerId, string id, string mode, IReadOnlyList<string> allowedTools, CancellationToken cancellationToken) =>
            Task.FromResult<UserMcpServer?>(null);
        public Task<bool> RemoveAsync(Guid ownerId, string id, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
