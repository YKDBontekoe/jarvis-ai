using Jarvis.Application.Integrations;
using Jarvis.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class OfficialMcpRegistryCatalogTests
{
    private sealed class ScriptedHandler(Queue<Func<HttpResponseMessage>> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses.Dequeue()());
    }

    private static OfficialMcpRegistryCatalog Catalog(params Func<HttpResponseMessage>[] responses) =>
        new(new HttpClient(new ScriptedHandler(new Queue<Func<HttpResponseMessage>>(responses))),
            new ConfigurationBuilder().Build(), NullLogger<OfficialMcpRegistryCatalog>.Instance);

    [Fact]
    public async Task ATimeoutSaysTheCatalogWasTooSlow()
    {
        var catalog = Catalog(() => throw new TaskCanceledException("timed out"));

        var failure = await Assert.ThrowsAsync<McpCatalogUnavailableException>(() =>
            catalog.SearchAsync("timeout-" + Guid.NewGuid(), null, 10, CancellationToken.None));

        Assert.Contains("too long", failure.Message);
    }

    [Fact]
    public async Task AServerErrorKeepsTheGenericMessage()
    {
        var catalog = Catalog(() => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway));

        var failure = await Assert.ThrowsAsync<McpCatalogUnavailableException>(() =>
            catalog.SearchAsync("bad-gateway-" + Guid.NewGuid(), null, 10, CancellationToken.None));

        Assert.Contains("not answering", failure.Message);
    }
}
