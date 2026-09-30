using Jarvis.Api.Conversations;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class AgentFailureMessageTests
{
    [Fact]
    public void A_signed_out_codex_tells_the_owner_to_sign_in()
    {
        var error = new InvalidOperationException(
            "Codex CLI model turn failed: unexpected status 401 Unauthorized: Missing bearer or basic authentication in header, url: https://api.openai.com/v1/responses, request id: req_123");
        var message = AgentFailureMessage.For(error);
        Assert.Contains("signed out", message);
        Assert.DoesNotContain("req_123", message);
        Assert.DoesNotContain("openai.com", message);
    }

    [Fact]
    public void Nested_causes_are_classified_too()
    {
        var error = new InvalidOperationException("run failed", new HttpRequestException("429 Too Many Requests"));
        Assert.Contains("usage limit", AgentFailureMessage.For(error));
    }

    [Fact]
    public void A_missing_codex_binary_is_named()
    {
        var error = new InvalidOperationException("Could not start the Codex CLI process: No such file or directory");
        Assert.Contains("isn’t available", AgentFailureMessage.For(error));
    }

    [Fact]
    public void Timeouts_are_reported_as_slow_model()
    {
        Assert.Contains("too long", AgentFailureMessage.For(new TimeoutException()));
    }

    [Fact]
    public void Unknown_failures_keep_the_generic_message_without_leaking_details()
    {
        var message = AgentFailureMessage.For(new InvalidOperationException("secret path /home/x"));
        Assert.Equal(ConversationTurnService.FailureMessage, message);
    }
}
