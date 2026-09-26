using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Jarvis.Workflows;

/// <summary>Reads one numeric value from an unauthenticated public HTTPS JSON endpoint.</summary>
public sealed class PublicJsonMetricReader
{
    private const int MaxResponseBytes = 1_048_576;
    private static readonly (IPAddress Network, int PrefixLength)[] NonPublicIpv4Networks =
    [
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.0.0.0"), 24),
        (IPAddress.Parse("192.0.2.0"), 24),
        (IPAddress.Parse("192.88.99.0"), 24),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("198.18.0.0"), 15),
        (IPAddress.Parse("198.51.100.0"), 24),
        (IPAddress.Parse("203.0.113.0"), 24),
        (IPAddress.Parse("224.0.0.0"), 4)
    ];

    public async Task<double> ReadAsync(string url, string jsonPath, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            IPAddress.TryParse(uri.Host, out _) || IsLocalName(uri.IdnHost))
            throw new InvalidDataException("Condition watch URL is not a credential-free public HTTPS URL.");

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            MaxConnectionsPerServer = 1,
            PooledConnectionLifetime = TimeSpan.Zero,
            ConnectCallback = ConnectToPublicAddressAsync
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("Jarvis-ConditionWatch/1.0");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new InvalidDataException("Condition watch endpoint redirected; redirects are disabled for safety.");
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !(mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
                                   mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Condition watch endpoint did not return JSON.");
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            throw new InvalidDataException("Condition watch JSON exceeded the 1 MiB response limit.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var bounded = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (bounded.Length + count > MaxResponseBytes)
                throw new InvalidDataException("Condition watch JSON exceeded the 1 MiB response limit.");
            await bounded.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        bounded.Position = 0;

        using var document = await JsonDocument.ParseAsync(bounded, cancellationToken: cancellationToken);
        var current = document.RootElement;
        foreach (var segment in jsonPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                throw new InvalidDataException("Condition watch JSON path was not found.");
        }
        if (!current.TryGetDouble(out var value) || !double.IsFinite(value))
            throw new InvalidDataException("Condition watch JSON path did not contain a finite number.");
        return value;
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var publicAddresses = addresses.Where(IsPublicAddress).ToArray();
        if (publicAddresses.Length == 0)
            throw new SocketException((int)SocketError.AccessDenied);

        Exception? lastError = null;
        foreach (var address in publicAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (exception is OperationCanceledException) throw;
                lastError = exception;
            }
        }
        throw new HttpRequestException("Could not connect to the public condition watch host.", lastError);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !NonPublicIpv4Networks.Any(network => IsInNetwork(address, network.Network, network.PrefixLength));
        if (address.AddressFamily != AddressFamily.InterNetworkV6 || IPAddress.IsLoopback(address) ||
            address.IsIPv6LinkLocal || address.IsIPv6Multicast)
            return false;

        var bytes = address.GetAddressBytes();
        // Global unicast space only. Exclude documentation, protocol assignment, Teredo, and 6to4 ranges.
        if (bytes[0] is < 0x20 or > 0x3f ||
            bytes[0] == 0x20 && bytes[1] == 0x01 &&
                (bytes[2] == 0x0d && bytes[3] == 0xb8 ||
                 bytes[2] == 0x00 && (bytes[3] == 0x00 || bytes[3] == 0x02 || bytes[3] == 0x10)) ||
            bytes[0] == 0x20 && bytes[1] == 0x02)
            return false;
        return true;
    }

    private static bool IsInNetwork(IPAddress address, IPAddress network, int prefixLength)
    {
        var candidate = address.GetAddressBytes();
        var prefix = network.GetAddressBytes();
        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        for (var index = 0; index < wholeBytes; index++)
            if (candidate[index] != prefix[index]) return false;
        if (remainingBits == 0) return true;
        var mask = (byte)(0xff << (8 - remainingBits));
        return (candidate[wholeBytes] & mask) == (prefix[wholeBytes] & mask);
    }

    private static bool IsLocalName(string host)
    {
        var normalized = host.TrimEnd('.');
        return normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase) ||
               !normalized.Contains('.', StringComparison.Ordinal);
    }
}
