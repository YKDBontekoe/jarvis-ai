using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Jarvis.Application.Integrations;

namespace Jarvis.Workflows;

/// <summary>Reads one numeric value from an unauthenticated public HTTPS JSON endpoint.</summary>
public sealed class PublicJsonMetricReader
{
    private const int MaxResponseBytes = 1_048_576;

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
        var publicAddresses = addresses.Where(McpServerEndpointValidator.IsPublic).ToArray();
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
