using System.Net;
using System.Text;
using Jarvis.Api.Channels;
using Jarvis.Application.Audit;
using Jarvis.Application.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ChannelLinkTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public async Task WhatsApp_link_shows_the_qr_then_creates_a_channel_with_the_scanned_number_allowlisted()
    {
        var harness = new Harness(whatsApp: true);
        var started = await harness.Links.StartAsync(Owner, ChannelKinds.WhatsAppLinked, null, default);

        Assert.NotNull(started);
        Assert.Equal(ChannelLinkStates.Waiting, started.State);
        Assert.Equal("data:image/png;base64,QR1", started.QrImage);

        harness.Bridge.Status = """{"state":"open","qr":null,"phone":"+31612345678"}""";
        var linked = await harness.Links.GetAsync(Owner, started.LinkId, default);

        Assert.Equal(ChannelLinkStates.Linked, linked!.State);
        var channel = Assert.Single(harness.Repository.Created);
        Assert.Equal(started.LinkId, channel.Id); // bridge session id == connection id
        Assert.Equal(linked.ChannelId, channel.Id);
        Assert.Equal("+31612345678", channel.Account);
        Assert.Equal(["+31612345678"], channel.AllowedSenders);
        Assert.Equal("+31612345678", channel.NotifyRecipient);
        Assert.True(channel.Enabled && channel.ForwardNotifications);

        // Polling again is idempotent: no second channel.
        await harness.Links.GetAsync(Owner, started.LinkId, default);
        Assert.Single(harness.Repository.Created);
    }

    [Fact]
    public async Task Read_along_links_a_separate_personal_account_without_automatic_replies_or_forwarding()
    {
        var harness = new Harness(whatsApp: true);
        var jarvisAttempt = await harness.Links.StartAsync(Owner, ChannelKinds.WhatsAppLinked, null, default);
        harness.Bridge.Status = """{"state":"open","phone":"+31600000000"}""";
        await harness.Links.GetAsync(Owner, jarvisAttempt!.LinkId, default);
        var jarvis = Assert.Single(harness.Repository.Created);

        harness.Bridge.Status = """{"state":"qr","qr":"data:image/png;base64,QR2"}""";
        var personalAttempt = await harness.Links.StartAsync(Owner, ChannelKinds.WhatsAppLinked, null, default,
            readAlong: true);
        Assert.NotEqual(jarvis.Id, personalAttempt!.LinkId);
        harness.Bridge.Status = """{"state":"open","phone":"+31612345678"}""";
        var linked = await harness.Links.GetAsync(Owner, personalAttempt.LinkId, default);

        Assert.Equal(ChannelLinkStates.Linked, linked!.State);
        var personal = harness.Repository.Created.Single(x => x.Id == linked.ChannelId);
        Assert.Equal(Owner, personal.OwnerId);
        Assert.Equal("My WhatsApp", personal.DisplayName);
        Assert.Equal("+31612345678", personal.Account);
        Assert.True(personal.Enabled);
        Assert.Empty(personal.AllowedSenders);
        Assert.False(personal.ForwardNotifications);
        Assert.Null(personal.NotifyRecipient);
        Assert.False(ChannelAddresses.IsAllowed(personal, personal.Account));
        Assert.Equal([jarvis.Account], jarvis.AllowedSenders);
        Assert.True(jarvis.ForwardNotifications);

        // Settings can still be saved while the personal account has no channel senders.
        var settings = new SaveChannelRequest(personal.Kind, personal.DisplayName, personal.Account,
            true, [], false, null, null);
        ChannelValidation.Normalize(settings, creating: false);
        Assert.Throws<ArgumentException>(() => ChannelValidation.Normalize(
            settings with { ForwardNotifications = true }, creating: false));
        await harness.Links.GetAsync(Owner, personalAttempt.LinkId, default);
        Assert.Equal(2, harness.Repository.Created.Count);
        Assert.Null(await harness.Links.GetAsync(Guid.NewGuid(), personalAttempt.LinkId, default));
    }

    [Fact]
    public async Task Read_along_setup_cannot_repurpose_a_channel_or_link_signal()
    {
        var harness = new Harness(whatsApp: true, signal: true);
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Links.StartAsync(Owner,
            ChannelKinds.Signal, null, default, readAlong: true));
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Links.StartAsync(Owner,
            ChannelKinds.WhatsAppLinked, Guid.NewGuid(), default, readAlong: true));
        Assert.Empty(harness.Repository.Created);
    }

    [Fact]
    public async Task Link_attempts_belong_to_their_owner()
    {
        var harness = new Harness(whatsApp: true);
        var started = await harness.Links.StartAsync(Owner, ChannelKinds.WhatsAppLinked, null, default);

        Assert.Null(await harness.Links.GetAsync(Guid.NewGuid(), started!.LinkId, default));
        Assert.Null(await harness.Links.GetAsync(Owner, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Linking_is_unavailable_until_the_server_has_the_bridge_or_signal_cli()
    {
        var harness = new Harness(whatsApp: false);

        await Assert.ThrowsAsync<ChannelLinkUnavailableException>(() =>
            harness.Links.StartAsync(Owner, ChannelKinds.WhatsAppLinked, null, default));
        await Assert.ThrowsAsync<ChannelLinkUnavailableException>(() =>
            harness.Links.StartAsync(Owner, ChannelKinds.Signal, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Links.StartAsync(Owner, "sms", null, default));
    }

    [Fact]
    public async Task Signal_link_completes_when_a_new_account_appears_in_signal_cli()
    {
        var harness = new Harness(whatsApp: false, signal: true);
        harness.Signal.Accounts = """["+31600000000"]""";
        var started = await harness.Links.StartAsync(Owner, ChannelKinds.Signal, null, default);

        Assert.Equal(ChannelLinkStates.Waiting, started!.State);
        Assert.StartsWith("data:image/png;base64,", started.QrImage);
        Assert.Empty(harness.Repository.Created);

        harness.Signal.Accounts = """["+31600000000","+31612345678"]""";
        var linked = await harness.Links.GetAsync(Owner, started.LinkId, default);

        Assert.Equal(ChannelLinkStates.Linked, linked!.State);
        var channel = Assert.Single(harness.Repository.Created);
        Assert.Equal(ChannelKinds.Signal, channel.Kind);
        Assert.Equal("+31612345678", channel.Account);
        Assert.Equal(["+31612345678"], channel.AllowedSenders);
    }

    private sealed class Harness
    {
        public FakeHandler Bridge { get; } = new();
        public FakeHandler Signal { get; } = new();
        public FakeRepository Repository { get; } = new();
        public ChannelLinkService Links { get; }

        public Harness(bool whatsApp, bool signal = false)
        {
            Bridge.Status = """{"state":"qr","qr":"data:image/png;base64,QR1","phone":null}""";
            var options = new ChannelOptions
            {
                WhatsAppBridgeUrl = whatsApp ? "http://bridge" : null,
                SignalBaseUrl = signal ? "http://signal" : null
            };
            var services = new ServiceCollection()
                .AddSingleton<IChannelRepository>(Repository)
                .AddSingleton<IAuditEventStore, FakeAudit>()
                .BuildServiceProvider();
            Links = new ChannelLinkService(new WhatsAppBridgeClient(new HttpClient(Bridge), options), options,
                new FakeFactory(Signal), services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ChannelLinkService>.Instance);
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public string Status { get; set; } = "{}";
        public string Accounts { get; set; } = "[]";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/qrcodelink"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
            var body = path.EndsWith("/accounts") ? Accounts : path.Contains("/sessions/") ? Status : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeAudit : IAuditEventStore
    {
        public Task<AuditEventRecord> AppendAsync(Guid ownerId, string tool, string action, string riskClass,
            bool success, Guid? approvalId, string? metadataJson, CancellationToken cancellationToken,
            Guid? agentRunId = null) => Task.FromResult<AuditEventRecord>(null!);

        public Task<IReadOnlyList<AuditEventRecord>> ListAsync(Guid ownerId, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeRepository : IChannelRepository
    {
        public List<ChannelConnectionRecord> Created { get; } = [];

        public Task<ChannelConnectionRecord> CreateAsync(Guid ownerId, SaveChannelRequest request,
            CancellationToken cancellationToken, Guid? id = null)
        {
            var now = DateTimeOffset.UtcNow;
            var record = new ChannelConnectionRecord(id ?? Guid.NewGuid(), ownerId, request.Kind!, request.DisplayName!,
                request.Account!, request.Enabled, request.AllowedSenders!, request.ForwardNotifications,
                request.NotifyRecipient, "key", null, null, null, now, now, now);
            Created.Add(record);
            return Task.FromResult(record);
        }

        public Task<ChannelConnectionRecord?> GetAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Created.SingleOrDefault(item => item.Id == id && item.OwnerId == ownerId));

        public Task<IReadOnlyList<ChannelConnectionRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ChannelConnectionRecord>> ListEnabledAsync(string? kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ChannelConnectionRecord?> FindByWebhookKeyAsync(string webhookKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ChannelConnectionRecord?> UpdateAsync(Guid ownerId, Guid id, SaveChannelRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> EnqueueInboundAsync(Guid connectionId, string sender, string text, string externalId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<InboundChannelMessage>> ClaimPendingInboundAsync(int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteInboundAsync(Guid messageId, string? error, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task RecordOutboundAsync(Guid connectionId, string recipient, string text, string? error,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChannelMessageRecord>> ListMessagesAsync(Guid ownerId, Guid connectionId, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChannelThreadRecord>> ListThreadsAsync(Guid ownerId, Guid connectionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChannelMessageRecord>> ListThreadMessagesAsync(Guid ownerId, Guid connectionId,
            string peer, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> GetThreadConversationAsync(Guid connectionId, string sender,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetThreadConversationAsync(Guid connectionId, string sender, Guid conversationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AdvanceNotificationWatermarkAsync(Guid connectionId, DateTimeOffset forwardedUntil,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
