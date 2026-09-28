using System.Net;
using System.Text.Json;
using Jarvis.Agents;
using Jarvis.Api.Realtime;
using Jarvis.Application.Integrations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class McpAuthorizationTests
{
    [Fact]
    public void FormatAsk_can_point_at_an_in_app_connect_link()
    {
        var json = McpAuthorization.FormatAsk("Calendar", "jarvis-pack-calendar",
            startUrl: "https://mcp.example.net/authorize");
        using var document = JsonDocument.Parse(json);
        Assert.Equal("https://mcp.example.net/authorize", document.RootElement.GetProperty("startUrl").GetString());
        Assert.Contains("Jarvis connect link", document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("store a token under provider", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void FormatAsk_requires_the_user_to_authorize_and_never_collects_tokens()
    {
        var json = McpAuthorization.FormatAsk("GitHub", "github", "https://github.com/login/oauth/authorize");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("authorization_required", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("askUser").GetBoolean());
        Assert.True(root.GetProperty("mustAsk").GetBoolean());
        Assert.Equal("github", root.GetProperty("provider").GetString());
        Assert.Equal("https://github.com/login/oauth/authorize", root.GetProperty("authorizationUrl").GetString());
        Assert.Contains("Ask the user to authorize", root.GetProperty("message").GetString());
        Assert.Contains("Settings → Integrations", root.GetProperty("message").GetString());
        Assert.Contains("Never collect the token in chat", root.GetProperty("message").GetString());
        Assert.DoesNotContain("localhost", json);
    }

    [Fact]
    public void WwwAuthenticate_resource_metadata_must_be_public_https()
    {
        Assert.Equal("https://mcp.example.net/.well-known/oauth-protected-resource",
            McpAuthorization.ParseResourceMetadataUrl(
                "Bearer realm=\"mcp\", resource_metadata=\"https://mcp.example.net/.well-known/oauth-protected-resource\""));
        Assert.Null(McpAuthorization.ParseResourceMetadataUrl(
            "Bearer resource_metadata=\"http://mcp.example.net/meta\""));
        Assert.Null(McpAuthorization.ParseResourceMetadataUrl(
            "Bearer resource_metadata=\"https://localhost/.well-known/oauth-protected-resource\""));
        Assert.Null(McpAuthorization.ParseResourceMetadataUrl("Bearer realm=\"mcp\""));
    }

    [Fact]
    public void Authorization_endpoint_is_read_from_oauth_metadata()
    {
        Assert.Equal("https://auth.example.net/authorize",
            McpAuthorization.ReadAuthorizationEndpoint("""
                {"authorization_endpoint":"https://auth.example.net/authorize","token_endpoint":"https://auth.example.net/token"}
                """));
        Assert.Equal("https://login.example.net/",
            McpAuthorization.ReadAuthorizationEndpoint("""
                {"authorization_servers":["https://login.example.net"]}
                """));
        Assert.Null(McpAuthorization.ReadAuthorizationEndpoint("""{"authorization_endpoint":"http://insecure.example/"}"""));
    }

    [Fact]
    public void Http_401_and_unauthorized_messages_are_authorization_failures()
    {
        Assert.True(McpAuthorization.IsAuthorizationStatus(HttpStatusCode.Unauthorized));
        Assert.True(McpAuthorization.IsAuthorizationFailure(new HttpRequestException("denied", null, HttpStatusCode.Unauthorized)));
        Assert.True(McpAuthorization.IsAuthorizationFailure(new InvalidOperationException("401 Unauthorized")));
        Assert.False(McpAuthorization.IsAuthorizationFailure(new InvalidOperationException("the server is unavailable")));
    }
}

public sealed class CodexWebSearchTests
{
    [Fact]
    public void Live_search_config_enables_standalone_search_and_live_mode()
    {
        var start = CodexCliChatClient.CreateAppServerStart("codex", Path.GetTempPath(), enableWebSearch: true);
        Assert.Contains("standalone_web_search", start.ArgumentList);
        var enableIndex = start.ArgumentList.IndexOf("standalone_web_search") - 1;
        Assert.Equal("--enable", start.ArgumentList[enableIndex]);
        foreach (var overridePair in CodexCliChatClient.LiveWebSearchConfigOverrides)
            Assert.Contains(overridePair, start.ArgumentList);

        var disabled = CodexCliChatClient.CreateAppServerStart("codex", Path.GetTempPath(), enableWebSearch: false);
        Assert.Equal("--disable", disabled.ArgumentList[disabled.ArgumentList.IndexOf("standalone_web_search") - 1]);
        Assert.DoesNotContain("web_search=\"live\"", disabled.ArgumentList);
    }

    [Fact]
    public void Prompt_requires_live_search_with_today_and_forbids_a_fake_search_tool()
    {
        var prompt = CodexCliChatClient.BuildPrompt(
            [new ChatMessage(ChatRole.User, "What is the latest .NET release?")],
            null, [], enableWebSearch: true).Text;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Assert.Contains("Live Codex web search is enabled", prompt);
        Assert.Contains(today, prompt);
        Assert.Contains("do not return type=tool_call for web_search", prompt);
        Assert.Contains("never invent current facts", prompt);
    }

    [Theory]
    [InlineData("webSearch")]
    [InlineData("web_search")]
    [InlineData("webSearchCall")]
    [InlineData("web_search_call")]
    public void Native_search_item_types_are_recognized(string itemType)
    {
        Assert.True(CodexCliChatClient.IsNativeWebSearchItem(itemType));
        using var document = JsonDocument.Parse("{\"params\":{\"item\":{\"type\":\"" + itemType + "\"}}}");
        Assert.True(CodexCliChatClient.IsNativeWebSearchNotification("item/completed", document.RootElement));
    }

    [Fact]
    public void Unrelated_items_are_not_counted_as_search()
    {
        Assert.False(CodexCliChatClient.IsNativeWebSearchItem("agentMessage"));
        using var document = JsonDocument.Parse("""{"params":{"item":{"type":"agentMessage"}}}""");
        Assert.False(CodexCliChatClient.IsNativeWebSearchNotification("item/completed", document.RootElement));
    }
}

public sealed class VoiceToolsTests
{
    [Fact]
    public void Voice_approval_ids_are_distinct_from_chat_approvals()
    {
        var id = VoiceTools.NewApprovalRequestId();
        Assert.True(VoiceTools.IsVoiceApproval(id));
        Assert.False(VoiceTools.IsVoiceApproval("chat-approval"));
        Assert.Equal(VoiceTools.ApprovalNeeded,
            "I need your approval before I can continue. Check the Jarvis app.");
    }

    [Fact]
    public void Approval_required_functions_are_detected_and_unwrapped()
    {
        var inner = AIFunctionFactory.Create(() => "ran", "fixture_action");
        var gated = new ApprovalRequiredAIFunction(inner);
        Assert.True(VoiceTools.RequiresApproval(gated));
        Assert.False(VoiceTools.RequiresApproval(inner));
        Assert.Same(inner, VoiceTools.UnwrapApprovals(gated));
        Assert.Equal("fixture_action", VoiceTools.Describe(gated).Name);
        Assert.True(VoiceTools.Describe(gated).RequiresApproval);
    }

    [Fact]
    public void Voice_realtime_instructions_keep_tools_on_the_fast_path()
    {
        Assert.Contains("Call them yourself during this voice session", VoiceTools.VoiceRealtimeAppendix);
        Assert.Contains("do not wait for a separate chat conversion", VoiceTools.VoiceRealtimeAppendix);
        Assert.DoesNotContain("Speak only the Jarvis answer", VoiceTools.VoiceRealtimeAppendix);
    }
}
