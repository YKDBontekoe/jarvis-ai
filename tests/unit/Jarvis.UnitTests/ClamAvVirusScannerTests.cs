using Jarvis.Infrastructure.Files;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ClamAvVirusScannerTests
{
    [Fact]
    public async Task Scanner_can_be_created_without_a_daemon_but_refuses_to_scan()
    {
        // The worker builds the agent, and with it this scanner, without any Antivirus settings.
        var scanner = new ClamAvVirusScanner(new ConfigurationBuilder().Build());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scanner.ScanAsync(new MemoryStream([1, 2, 3]), CancellationToken.None));
    }
}
