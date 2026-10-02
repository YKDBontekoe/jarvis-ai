using System.Net;
using System.Net.Sockets;
using System.Text;
using Jarvis.Application.Integrations;
using Jarvis.Application.Workflows;

namespace Jarvis.Workflows;

/// <summary>Reads one numeric value from a public HTTPS JSON endpoint, optionally with a stored Authorization header.</summary>
public sealed class PublicJsonMetricReader
{
    private const int MaxResponseBytes = 1_048_576;

    public Task<double> ReadAsync(string url, string jsonPath, CancellationToken cancellationToken) =>
        ReadAsync(url, jsonPath, null, cancellationToken);

    public async Task<double> ReadAsync(string url, string jsonPath, string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        await using var bounded = await FetchPublicHttpsAsync(url, "application/json", authorizationHeader,
            cancellationToken);
        using var document = await System.Text.Json.JsonDocument.ParseAsync(bounded, cancellationToken: cancellationToken);
        var current = document.RootElement;
        foreach (var segment in jsonPath.Split('.'))
        {
            if (current.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
                throw new InvalidDataException("Condition watch JSON path was not found.");
        }
        if (!current.TryGetDouble(out var value) || !double.IsFinite(value))
            throw new InvalidDataException("Condition watch JSON path did not contain a finite number.");
        return value;
    }

    internal static async Task<MemoryStream> FetchPublicHttpsAsync(string url, string accept,
        string? authorizationHeader, CancellationToken cancellationToken)
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
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.UserAgent.ParseAdd("Jarvis-ConditionWatch/1.0");
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader.Trim());
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new InvalidDataException("Condition watch endpoint redirected; redirects are disabled for safety.");
        response.EnsureSuccessStatusCode();
        if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !(mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
                                       mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Condition watch endpoint did not return JSON.");
        }
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            throw new InvalidDataException("Condition watch response exceeded the 1 MiB limit.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var bounded = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (bounded.Length + count > MaxResponseBytes)
                throw new InvalidDataException("Condition watch response exceeded the 1 MiB limit.");
            await bounded.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        bounded.Position = 0;
        return bounded;
    }

    internal static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context,
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

/// <summary>Parses upcoming VEVENT rows from a public ICS/iCal feed.</summary>
public static class IcsCalendarParser
{
    public static IReadOnlyList<CalendarEventRecord> Parse(string ics, DateTimeOffset from, DateTimeOffset until)
    {
        var events = new List<CalendarEventRecord>();
        string? summary = null;
        DateTimeOffset? start = null;
        DateTimeOffset? end = null;
        foreach (var raw in Unfold(ics))
        {
            if (raw.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                summary = null;
                start = null;
                end = null;
                continue;
            }
            if (raw.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (start is { } at && at >= from && at < until)
                    events.Add(new CalendarEventRecord(string.IsNullOrWhiteSpace(summary) ? "Event" : summary, at, end));
                continue;
            }
            if (raw.StartsWith("SUMMARY", StringComparison.OrdinalIgnoreCase))
                summary = Unescape(ValueOf(raw));
            else if (raw.StartsWith("DTSTART", StringComparison.OrdinalIgnoreCase))
                start = ParseDate(raw);
            else if (raw.StartsWith("DTEND", StringComparison.OrdinalIgnoreCase))
                end = ParseDate(raw);
        }
        return events.OrderBy(item => item.StartAt).Take(40).ToArray();
    }

    private static IEnumerable<string> Unfold(string ics)
    {
        var builder = new StringBuilder();
        using var reader = new StringReader(ics.Replace("\r\n", "\n"));
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                builder.Append(line.TrimStart());
                continue;
            }
            if (builder.Length > 0) yield return builder.ToString();
            builder.Clear();
            builder.Append(line);
        }
        if (builder.Length > 0) yield return builder.ToString();
    }

    private static string ValueOf(string line)
    {
        var index = line.IndexOf(':');
        return index < 0 ? string.Empty : line[(index + 1)..].Trim();
    }

    private static string Unescape(string value) =>
        value.Replace("\\n", " ", StringComparison.OrdinalIgnoreCase).Replace("\\,", ",").Replace("\\;", ";");

    private static DateTimeOffset? ParseDate(string line)
    {
        var value = ValueOf(line);
        if (value.Length == 8 && DateOnly.TryParseExact(value, "yyyyMMdd", out var date))
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (value.EndsWith('Z') && DateTimeOffset.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var utc))
            return utc;
        if (DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var local))
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc));
        return null;
    }
}
