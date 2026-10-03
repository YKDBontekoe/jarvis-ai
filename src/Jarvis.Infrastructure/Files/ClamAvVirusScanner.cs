using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Jarvis.Application.Files;
using Microsoft.Extensions.Configuration;

namespace Jarvis.Infrastructure.Files;

/// <summary>
/// Scans uploads with a ClamAV daemon. The settings are read when a scan starts, not when the scanner is created:
/// the worker builds the whole agent (and so the file service) for every task but never scans an upload, so it
/// has no daemon configured. A missing host still fails closed, because the scan throws.
/// </summary>
public sealed class ClamAvVirusScanner(IConfiguration configuration) : IFileMalwareScanner
{
    public async Task ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        var host = configuration["Antivirus:Host"]
            ?? throw new InvalidOperationException("Antivirus:Host must point to a private ClamAV daemon.");
        var port = ReadPort(configuration["Antivirus:Port"]);
        var timeoutSeconds = ReadTimeout(configuration["Antivirus:TimeoutSeconds"]);
        if (!content.CanSeek)
            throw new ArgumentException("The upload stream must support seeking for malware scanning.", nameof(content));

        var originalPosition = content.Position;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            content.Position = 0;
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);

            var buffer = new byte[64 * 1024];
            var chunkLength = new byte[sizeof(int)];
            while (true)
            {
                var read = await content.ReadAsync(buffer, timeout.Token);
                if (read == 0) break;
                BinaryPrimitives.WriteInt32BigEndian(chunkLength, read);
                await stream.WriteAsync(chunkLength, timeout.Token);
                await stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            BinaryPrimitives.WriteInt32BigEndian(chunkLength, 0);
            await stream.WriteAsync(chunkLength, timeout.Token);
            await stream.FlushAsync(timeout.Token);
            var response = await ReadResponseAsync(stream, timeout.Token);
            if (response.EndsWith(" OK", StringComparison.Ordinal)) return;
            if (response.Contains(" FOUND", StringComparison.Ordinal))
                throw new MalwareDetectedException();
            throw new IOException("The ClamAV daemon did not complete the malware scan.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The ClamAV malware scan exceeded its configured timeout.");
        }
        finally
        {
            content.Position = originalPosition;
        }
    }

    private static async Task<string> ReadResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var response = new MemoryStream();
        var next = new byte[1];
        while (response.Length < 512)
        {
            var read = await stream.ReadAsync(next, cancellationToken);
            if (read == 0 || next[0] == 0) break;
            response.WriteByte(next[0]);
        }
        if (response.Length == 0 || response.Length >= 512)
            throw new IOException("The ClamAV daemon returned an invalid scan response.");
        return Encoding.ASCII.GetString(response.GetBuffer(), 0, checked((int)response.Length));
    }

    private static int ReadPort(string? value)
    {
        if (value is null) return 3310;
        if (int.TryParse(value, out var port) && port is > 0 and <= 65535) return port;
        throw new InvalidOperationException("Antivirus:Port must be a valid TCP port.");
    }

    private static int ReadTimeout(string? value)
    {
        if (value is null) return 60;
        if (int.TryParse(value, out var seconds) && seconds is >= 1 and <= 600) return seconds;
        throw new InvalidOperationException("Antivirus:TimeoutSeconds must be between 1 and 600.");
    }
}
