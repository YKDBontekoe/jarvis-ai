using System.Net;

namespace Jarvis.Application.Integrations;

public sealed record UserMcpServer(string Id, string Name, string Endpoint,
    IReadOnlyList<string> AllowedTools, DateTimeOffset UpdatedAt, bool HasToken, bool Enabled = true,
    string Transport = "streamableHttp", string? Command = null, IReadOnlyList<string>? Arguments = null,
    bool IsValid = true, string? ConfigurationIssue = null, IReadOnlyList<McpServerSecret>? Secrets = null,
    string? CatalogName = null)
{
    /// <summary>Where each secret goes at runtime. Server-side only; clients see <see cref="Secrets"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<McpSecretBinding>? SecretBindings { get; init; }
}

/// <summary>A secret the owner provides for a server, and whether it is stored. Values are never returned.</summary>
public sealed record McpServerSecret(string Name, string Label, string? Description, bool Required, bool IsSet);

/// <summary>
/// Where Jarvis puts one owner secret when it starts a server: an environment variable for stdio servers
/// (<see cref="Target"/> "env") or an HTTP header for remote servers ("header"), optionally after a prefix
/// such as "Bearer".
/// </summary>
public sealed record McpSecretBinding(string SecretName, string Target, string Key, string? Prefix, string Label,
    string? Description, bool Required);

/// <summary>A complete server definition, used by catalog installs.</summary>
public sealed record McpServerDefinition(string Name, string Transport, string? Endpoint, string? Command,
    IReadOnlyList<string>? Arguments, IReadOnlyList<string> AllowedTools, IReadOnlyList<McpSecretBinding> Secrets,
    string? CatalogName = null);

public static partial class McpSecretBindings
{
    public const string EnvironmentTarget = "env";
    public const string HeaderTarget = "header";
    public const int MaxSecrets = 10;

    // Variables that change how the runtime loads code or where it looks, so a registry entry cannot use the
    // owner's secret prompt to inject them.
    private static readonly string[] BlockedEnvironmentPrefixes =
        ["LD_", "DYLD_", "NODE_", "NPM_CONFIG_", "PYTHON", "UV_", "PIP_", "DOTNET_", "COMPlus_"];

    private static readonly HashSet<string> BlockedEnvironmentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "HOME", "SHELL", "USER", "TMPDIR", "TEMP", "TMP", "PWD", "IFS", "BASH_ENV", "ENV",
        "SSL_CERT_FILE", "SSL_CERT_DIR", "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "ALL_PROXY"
    };

    private static readonly HashSet<string> BlockedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Content-Type", "Transfer-Encoding", "Connection", "Cookie", "Accept",
        "Mcp-Session-Id", "Mcp-Protocol-Version", "Upgrade", "TE", "Trailer", "Proxy-Authorization"
    };

    public static IReadOnlyList<McpSecretBinding> Normalize(IReadOnlyList<McpSecretBinding>? bindings, string transport)
    {
        var list = bindings ?? [];
        if (list.Count > MaxSecrets)
            throw new ArgumentException($"A server can ask for at most {MaxSecrets} secrets.");
        var expectedTarget = transport == "stdio" ? EnvironmentTarget : HeaderTarget;
        var result = new List<McpSecretBinding>();
        foreach (var binding in list)
        {
            var secretName = binding.SecretName?.Trim() ?? string.Empty;
            if (!SecretNamePattern().IsMatch(secretName) ||
                secretName == IntegrationCredentialProviders.UserMcpConfigSecret)
                throw new ArgumentException("Secret names must be 1 to 48 lowercase letters, numbers, or underscores.");
            if (binding.Target != expectedTarget)
                throw new ArgumentException(transport == "stdio"
                    ? "Installed connectors receive secrets as environment variables."
                    : "Remote servers receive secrets as HTTP headers.");
            var key = binding.Key?.Trim() ?? string.Empty;
            if (expectedTarget == EnvironmentTarget && !IsAllowedEnvironmentName(key))
                throw new ArgumentException($"Jarvis does not set the environment variable '{key}' for a connector.");
            if (expectedTarget == HeaderTarget && !IsAllowedHeaderName(key))
                throw new ArgumentException($"Jarvis does not send the header '{key}' to a remote server.");
            var prefix = string.IsNullOrWhiteSpace(binding.Prefix) ? null : binding.Prefix.Trim();
            if (prefix is not null && (expectedTarget != HeaderTarget || !PrefixPattern().IsMatch(prefix)))
                throw new ArgumentException("A secret prefix must be one word such as Bearer, and only applies to headers.");
            if (result.Any(existing => existing.SecretName == secretName ||
                                       string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Each secret and each variable or header can appear only once.");
            result.Add(new McpSecretBinding(secretName, expectedTarget, key, prefix,
                Clean(binding.Label, 80) ?? key, Clean(binding.Description, 300), binding.Required));
        }
        return result;
    }

    /// <summary>A stable secret name for an environment variable or header, such as notion_token.</summary>
    public static string SecretNameFor(string key)
    {
        var name = NonWord().Replace(key.Trim().ToLowerInvariant(), "_").Trim('_');
        if (name.Length == 0 || !char.IsAsciiLetterLower(name[0])) name = "secret_" + name;
        name = name.Length > 48 ? name[..48].TrimEnd('_') : name;
        return name == IntegrationCredentialProviders.UserMcpConfigSecret ? "secret_config" : name;
    }

    public static bool IsAllowedEnvironmentName(string name) =>
        EnvironmentNamePattern().IsMatch(name) && !BlockedEnvironmentNames.Contains(name) &&
        !BlockedEnvironmentPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static bool IsAllowedHeaderName(string name) =>
        HeaderNamePattern().IsMatch(name) && !BlockedHeaders.Contains(name) &&
        !name.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase) &&
        !name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z][a-z0-9_]{0,47}$")]
    private static partial System.Text.RegularExpressions.Regex SecretNamePattern();

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")]
    private static partial System.Text.RegularExpressions.Regex EnvironmentNamePattern();

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,63}$")]
    private static partial System.Text.RegularExpressions.Regex HeaderNamePattern();

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z][A-Za-z0-9-]{0,31}$")]
    private static partial System.Text.RegularExpressions.Regex PrefixPattern();

    [System.Text.RegularExpressions.GeneratedRegex("[^a-z0-9]+")]
    private static partial System.Text.RegularExpressions.Regex NonWord();
}

public sealed class InvalidMcpServerConfigurationException : InvalidOperationException
{
    public InvalidMcpServerConfigurationException()
        : base("The MCP server configuration is invalid. Replace or delete the server to recover it.") { }
}

public sealed record AddUserMcpServerRequest(string Name, string Endpoint, IReadOnlyList<string> AllowedTools);

public sealed record AddUserMcpStdioServerRequest(string Name, string Command, IReadOnlyList<string> Arguments,
    IReadOnlyList<string> AllowedTools);

public sealed record McpServerStateRequest(bool? Enabled, string? ToolMode, IReadOnlyList<string>? AllowedTools);

public sealed record HostMcpOverride(bool Enabled, IReadOnlyList<string>? AllowedTools);

public interface IOwnerMcpPolicyStore
{
    Task<IReadOnlyDictionary<string, HostMcpOverride>> GetAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<HostMcpOverride> SaveAsync(Guid ownerId, string serverName, HostMcpOverride value,
        CancellationToken cancellationToken);
}

public interface IUserMcpServerRegistry
{
    Task<IReadOnlyList<UserMcpServer>> ListAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<UserMcpServer> AddAsync(Guid ownerId, AddUserMcpServerRequest request,
        CancellationToken cancellationToken);
    Task<UserMcpServer> AddStdioAsync(Guid ownerId, AddUserMcpStdioServerRequest request,
        CancellationToken cancellationToken);
    Task<UserMcpServer> AddDefinitionAsync(Guid ownerId, McpServerDefinition definition,
        CancellationToken cancellationToken);
    Task<UserMcpServer?> UpdateAsync(Guid ownerId, string id, AddUserMcpServerRequest request,
        CancellationToken cancellationToken);
    Task<UserMcpServer?> SetEnabledAsync(Guid ownerId, string id, bool enabled, CancellationToken cancellationToken);
    Task<UserMcpServer?> SetToolsAsync(Guid ownerId, string id, string mode, IReadOnlyList<string> allowedTools,
        CancellationToken cancellationToken);
    Task<bool> RemoveAsync(Guid ownerId, string id, CancellationToken cancellationToken);
}

public static class McpServerEndpointValidator
{
    public static async Task<string> ValidateAsync(string? value, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
            throw new ArgumentException("MCP servers must use an HTTPS URL without embedded credentials or fragments.");
        var host = endpoint.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out _))
            throw new ArgumentException("Use a public HTTPS hostname for an agent-managed MCP server.");
        _ = await ResolvePublicAddressesAsync(host, cancellationToken);
        return endpoint.AbsoluteUri;
    }

    public static async Task<IPAddress[]> ResolvePublicAddressesAsync(string host, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(host, cancellationToken); }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            if (exception is OperationCanceledException) throw;
            throw new ArgumentException("The MCP endpoint hostname could not be resolved.");
        }
        if (addresses.Length == 0 || addresses.Any(address => !IsPublic(address)))
            throw new ArgumentException("The MCP endpoint hostname must resolve only to public IP addresses.");
        return addresses;
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublic(address.MapToIPv4());
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            var in2001SpecialPurpose = bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] & 0xFE) == 0;
            // IANA's documentation allocation is outside the 2001::/23 special-purpose block.
            var inDocumentation = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8;
            var inNat64WellKnown = bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B &&
                bytes.Skip(4).Take(8).All(value => value == 0);
            var inNat64LocalUse = bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B &&
                bytes[4] == 0x00 && bytes[5] == 0x01;
            var inDiscardOnly = bytes[0] == 0x01 && bytes.Skip(1).Take(7).All(value => value == 0);
            var inDummyPrefix = bytes[0] == 0x01 && bytes[1] == 0x00 && bytes[2] == 0 && bytes[3] == 0 &&
                bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 1;
            return (bytes[0] & 0xE0) == 0x20 && !in2001SpecialPurpose && !inDocumentation &&
                !(bytes[0] == 0x20 && bytes[1] == 0x02) &&
                !(bytes[0] == 0x3F && (bytes[1] & 0xF0) == 0xF0) &&
                !(bytes[0] == 0x5F && bytes[1] == 0x00) &&
                !inNat64WellKnown && !inNat64LocalUse && !inDiscardOnly && !inDummyPrefix;
        }
        var octets = address.GetAddressBytes();
        var first = octets[0];
        var second = octets[1];
        var third = octets[2];
        return first is not (0 or 10 or 127 or >= 224) &&
            !(first == 100 && second is >= 64 and <= 127) &&
            !(first == 169 && second == 254) &&
            !(first == 172 && second is >= 16 and <= 31) &&
            !(first == 192 && (second == 168 || second == 0 && third == 0 || second == 0 && third == 2)) &&
            !(first == 192 && second == 88 && third == 99) &&
            !(first == 198 && (second == 18 || second == 19 || second == 51 && third == 100)) &&
            !(first == 203 && second == 0 && third == 113);
    }
}
