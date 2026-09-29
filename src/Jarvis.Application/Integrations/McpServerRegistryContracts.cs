using System.Net;

namespace Jarvis.Application.Integrations;

public sealed record UserMcpServer(string Id, string Name, string Endpoint,
    IReadOnlyList<string> AllowedTools, DateTimeOffset UpdatedAt, bool HasToken, bool Enabled = true,
    string Transport = "streamableHttp", string? Command = null, IReadOnlyList<string>? Arguments = null,
    bool IsValid = true, string? ConfigurationIssue = null);

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
