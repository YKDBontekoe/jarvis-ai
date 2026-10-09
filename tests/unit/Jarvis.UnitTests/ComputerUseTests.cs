using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Agents.Computer;
using Jarvis.Api.Computer;
using Jarvis.Api.Endpoints;
using Jarvis.Application.Approvals;
using Jarvis.Application.Browser;
using Jarvis.Application.Computer;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Jarvis.Application.Realtime;
using Jarvis.Application.Settings;
using Jarvis.Infrastructure.Computer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ComputerUseTests
{
    private static readonly Guid OwnerId = Guid.Parse("01996b8c-6000-7000-8000-000000000001");
    private static readonly Guid ConversationId = Guid.Parse("01996b8c-6000-7000-8000-0000000000c1");
    private static readonly Guid SessionId = Guid.Parse("01996b8c-6000-7000-8000-0000000000a1");
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    [Fact]
    public void Only_the_newest_tool_screenshot_reaches_the_model_as_an_image()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "Open the page"),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", Screenshot("Clicked at 1,2.", 1))]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-2", Screenshot("Typed 3 characters.", 2))])
        };

        var payload = CodexCliChatClient.BuildPrompt(messages, null, [], enableWebSearch: false);

        var image = Assert.Single(payload.Images);
        Assert.Equal(new DataContent(Jpeg.Append((byte)2).ToArray(), "image/jpeg").Uri, image);
        Assert.Contains("[Earlier screenshot omitted]", payload.Text);
        Assert.Contains("[Screenshot attached as image 1]", payload.Text);
        Assert.Contains("Typed 3 characters.", payload.Text);
        Assert.DoesNotContain("base64", payload.Text);
    }

    [Fact]
    public void Screenshots_survive_a_stored_and_reloaded_conversation()
    {
        var stored = JsonSerializer.SerializeToElement(Screenshot("Screenshot of the sandbox desktop.", 7),
            AIJsonUtilities.DefaultOptions);
        var messages = new[] { new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", stored)]) };

        var payload = CodexCliChatClient.BuildPrompt(messages, null, [], enableWebSearch: false);

        Assert.Single(payload.Images);
        Assert.Contains("Screenshot of the sandbox desktop.", payload.Text);
        Assert.DoesNotContain("base64", payload.Text);
    }

    [Fact]
    public void Mcp_call_results_are_read_with_their_image_and_error_flag()
    {
        using var document = JsonDocument.Parse($$"""
            {"content":[{"type":"text","text":"Clicked left at 5,6."},
                        {"type":"image","data":"{{Convert.ToBase64String(Jpeg)}}","mimeType":"image/jpeg"}],
             "isError":true}
            """);

        var observation = ComputerToolResults.Read(document.RootElement.Clone());

        Assert.Equal("Clicked left at 5,6.", observation.Text);
        Assert.Equal(Jpeg, observation.Image);
        Assert.Equal("image/jpeg", observation.ImageMediaType);
        Assert.True(observation.IsError);
        Assert.Null(ComputerToolResults.Read("plain").Image);
    }

    [Fact]
    public void Redacted_mcp_results_keep_their_screenshot()
    {
        // The MCP client returns AI contents; a server with credential headers gets them re-serialized as JSON nodes
        // by the secret-redacting wrapper.
        var redacted = JsonSerializer.SerializeToNode(Screenshot("Pressed Return.", 9),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var observation = ComputerToolResults.Read(redacted);

        Assert.Equal("Pressed Return.", observation.Text);
        Assert.Equal(Jpeg.Append((byte)9).ToArray(), observation.Image);
        Assert.Equal("image/jpeg", observation.ImageMediaType);
        Assert.Equal(observation.Image, ComputerToolResults.Read(Screenshot("Pressed Return.", 9)).Image);
    }

    [Fact]
    public void Timeline_summaries_never_carry_typed_text_or_shell_output()
    {
        Assert.Equal("Typed 11 characters.",
            ComputerStepFunction.Summarize("computer_type", new("Typed 11 characters.", null, null, false)));
        Assert.Equal("Command finished (exit code 0).", ComputerStepFunction.Summarize("computer_shell",
            new("Command finished (exit code 0).\nsecret output", null, null, false)));
        Assert.Equal("browser_type completed.",
            ComputerStepFunction.Summarize("browser_type", new("typed hunter2 into #password", null, null, false)));
    }

    [Fact]
    public async Task Computer_tools_refuse_without_an_open_session()
    {
        var invoked = false;
        var function = Step(Inner(() => invoked = true), Sessions(session: null), new MemoryObjects());

        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["x"] = 1, ["y"] = 2
        }));

        Assert.False(invoked);
        Assert.Contains("Call UseComputer first", result?.ToString());
    }

    [Fact]
    public async Task Computer_tools_wait_while_the_user_has_control()
    {
        var invoked = false;
        var session = Session(ComputerControlModes.User);
        var function = Step(Inner(() => invoked = true), Sessions(session), new MemoryObjects());

        var result = await function.InvokeAsync(new AIFunctionArguments());

        Assert.False(invoked);
        Assert.Contains("user has taken control", result?.ToString());
    }

    [Fact]
    public async Task Computer_steps_keep_the_screenshot_and_hand_it_to_the_model()
    {
        var recorded = new List<(string Tool, string Summary, string? Key)>();
        var published = new List<string>();
        var objects = new MemoryObjects();
        var function = Step(Inner(() => { }), Sessions(Session(ComputerControlModes.Agent), recorded), objects,
            published);

        var result = await function.InvokeAsync(new AIFunctionArguments());

        var contents = Assert.IsType<AIContent[]>(result);
        Assert.Equal("Clicked left at 1,2.", Assert.IsType<TextContent>(contents[0]).Text);
        Assert.Equal(Jpeg, Assert.IsType<DataContent>(contents[1]).Data.ToArray());
        var step = Assert.Single(recorded);
        Assert.Equal("computer_click", step.Tool);
        Assert.Equal("Clicked left at 1,2.", step.Summary);
        Assert.True(ComputerScreenshots.BelongsTo(step.Key, OwnerId, SessionId));
        Assert.Equal(Jpeg, objects.Stored[step.Key!]);
        Assert.Equal(["browser.step"], published);
    }

    [Fact]
    public void Computer_actions_have_their_own_approval_categories_and_risk()
    {
        Assert.Equal("computer", ApprovalCategories.Resolve("UseComputer", "{}").Key);
        Assert.Equal("computer", ApprovalCategories.Resolve("computer_click", "{}").Key);
        Assert.Equal("computer.shell", ApprovalCategories.Resolve("computer_shell", "{}").Key);
        Assert.Equal(ToolRisk.Outbound, ToolRiskPolicy.Classify("computer_shell"));
        Assert.Equal(ToolRisk.Outbound, ToolRiskPolicy.Classify("UseComputerAsync"));
        Assert.False(ToolRiskPolicy.CanAutoApprove(ToolRiskPolicy.Classify("computer_shell")));
        Assert.True(AutonomousOutboundCategories.IsEligible("computer"));
        Assert.False(AutonomousOutboundCategories.IsEligible("computer.shell"));
    }

    [Fact]
    public void View_tickets_work_once_and_cookies_round_trip()
    {
        var access = new ComputerViewAccess(new EphemeralDataProtectionProvider(),
            new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
        var grant = new ComputerViewGrant(OwnerId, SessionId);

        var ticket = access.IssueTicket(grant);

        Assert.Equal(grant, access.RedeemTicket(ticket));
        Assert.Null(access.RedeemTicket(ticket));
        Assert.Null(access.RedeemTicket("not-a-ticket"));
        Assert.Equal(grant, access.ReadCookie(access.IssueCookie(grant)));
        // A ticket is not a cookie and the other way round.
        Assert.Null(access.ReadCookie(access.IssueTicket(grant)));
        Assert.Null(access.RedeemTicket(access.IssueCookie(grant)));
    }

    [Theory]
    [InlineData("vnc.html", true)]
    [InlineData("app/ui.js", true)]
    [InlineData("core/rfb.js", true)]
    [InlineData("../etc/passwd", false)]
    [InlineData("app/../../secret", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("vnc.html?x=1", false)]
    [InlineData("", false)]
    public void The_viewer_proxy_only_serves_plain_relative_files(string path, bool allowed) =>
        Assert.Equal(allowed, ComputerEndpoints.IsViewerFile(path));

    [Fact]
    public void Sandbox_options_are_off_until_configured_and_validated()
    {
        Assert.False(ComputerSandboxOptions.From(Configuration()).IsConfigured);

        var options = ComputerSandboxOptions.From(Configuration(
            ("Computer:ControlUrl", "http://computer-sandbox:8932"), ("Computer:ViewUrl", "http://computer-sandbox:6080"),
            ("Computer:Token", " token "), ("Computer:IdleTimeoutMinutes", "15")));
        Assert.True(options.IsConfigured);
        Assert.Equal("token", options.Token);
        Assert.Equal(TimeSpan.FromMinutes(15), options.IdleTimeout);

        Assert.Throws<InvalidOperationException>(() =>
            ComputerSandboxOptions.From(Configuration(("Computer:ControlUrl", "file:///etc"))));
        Assert.Throws<InvalidOperationException>(() =>
            ComputerSandboxOptions.From(Configuration(("Computer:IdleTimeoutMinutes", "1"))));
    }

    private static AIContent[] Screenshot(string text, byte marker) =>
        [new TextContent(text), new DataContent(Jpeg.Append(marker).ToArray(), "image/jpeg")];

    private static AIFunction Inner(Action onInvoke) => AIFunctionFactory.Create(() =>
    {
        onInvoke();
        return JsonSerializer.SerializeToElement(new
        {
            content = new object[]
            {
                new { type = "text", text = "Clicked left at 1,2." },
                new { type = "image", data = Convert.ToBase64String(Jpeg), mimeType = "image/jpeg" }
            }
        });
    }, "computer_click");

    private static ComputerStepFunction Step(AIFunction inner, IBrowserSessionStore sessions, IObjectStorage objects,
        List<string>? published = null) =>
        new(inner, sessions, objects, ConversationId, Fake<IRealtimePublisher>.Create(
                ("PublishToConversationAsync", args =>
                {
                    published?.Add((string)args[1]!);
                    return Task.CompletedTask;
                })),
            Fake<ICurrentUser>.Create(("get_OwnerId", _ => OwnerId)));

    private static BrowserSessionRecord Session(string controlMode) =>
        new(SessionId, OwnerId, ConversationId, "Find a train", null, "active", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, [], BrowserSessionKinds.Computer, controlMode);

    private static IBrowserSessionStore Sessions(BrowserSessionRecord? session,
        List<(string Tool, string Summary, string? Key)>? recorded = null) =>
        Fake<IBrowserSessionStore>.Create(
            ("GetActiveForConversationAsync", _ => session),
            ("RecordStepAsync", args =>
            {
                recorded?.Add(((string)args[1]!, (string)args[2]!, (string?)args[5]));
                return new BrowserStepRecord(Guid.NewGuid(), recorded?.Count ?? 1, (string)args[1]!,
                    (string)args[2]!, (bool)args[3]!, DateTimeOffset.UtcNow, (string?)args[5]);
            }),
            ("TrimScreenshotsAsync", _ => (IReadOnlyList<string>)[]));

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(value =>
            new KeyValuePair<string, string?>(value.Key, value.Value))).Build();

    private sealed class MemoryObjects : IObjectStorage
    {
        public Dictionary<string, byte[]> Stored { get; } = [];

        public async Task PutAsync(string objectKey, Stream content, string contentType,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Stored[objectKey] = buffer.ToArray();
        }

        public Task<Stream?> GetAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(Stored.TryGetValue(objectKey, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            Stored.Remove(objectKey);
            return Task.CompletedTask;
        }
    }
}

internal sealed class StubComputerSandbox(bool configured) : IComputerSandbox
{
    public bool IsConfigured => configured;
    public TimeSpan IdleTimeout => TimeSpan.FromMinutes(30);
    public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
