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

    [Theory]
    [InlineData("Codex CLI app-server exited unexpectedly: segfault", true)]
    [InlineData("Codex CLI app-server returned no thread identifier.", true)]
    [InlineData("Codex CLI streamed a non-prefix assistant response.", true)]
    [InlineData("Codex CLI app-server rejected the model turn: invalid model", false)]
    [InlineData("secret path /home/x", false)]
    public void Only_process_and_connection_failures_are_worth_a_second_attempt(string message, bool retryable) =>
        Assert.Equal(retryable, AgentFailureMessage.IsRetryable(new InvalidOperationException(message)));

    [Fact]
    public void Timeouts_and_broken_connections_are_retryable_even_when_nested()
    {
        Assert.True(AgentFailureMessage.IsRetryable(new TimeoutException()));
        Assert.True(AgentFailureMessage.IsRetryable(new IOException("pipe closed")));
        Assert.True(AgentFailureMessage.IsRetryable(new InvalidOperationException("run", new HttpRequestException("reset"))));
    }

    [Theory]
    [InlineData("401 Unauthorized")]
    [InlineData("429 Too Many Requests")]
    [InlineData("Could not start the Codex CLI process: No such file or directory")]
    public void Signed_out_rate_limited_and_missing_cli_failures_are_never_retried(string message) =>
        Assert.False(AgentFailureMessage.IsRetryable(new TimeoutException(message)));

    [Fact]
    public async Task A_failure_before_any_output_is_tried_once_more()
    {
        var calls = 0;
        var retried = new List<Exception>();

        var result = await ConversationTurnService.RetryWhenNothingHappenedAsync(_ =>
        {
            calls++;
            return calls == 1 ? throw new TimeoutException() : Task.FromResult("ok");
        }, retried.Add, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(2, calls);
        Assert.Single(retried);
    }

    [Fact]
    public async Task A_failure_after_output_is_never_repeated_so_nothing_is_said_or_done_twice()
    {
        var calls = 0;

        await Assert.ThrowsAsync<TimeoutException>(() => ConversationTurnService.RetryWhenNothingHappenedAsync<string>(
            produced =>
            {
                calls++;
                produced();
                throw new TimeoutException();
            }, null, CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_second_failure_and_non_retryable_failures_propagate()
    {
        var timeouts = 0;
        await Assert.ThrowsAsync<TimeoutException>(() => ConversationTurnService.RetryWhenNothingHappenedAsync<string>(
            _ =>
            {
                timeouts++;
                throw new TimeoutException();
            }, null, CancellationToken.None));
        Assert.Equal(2, timeouts);

        var permanent = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConversationTurnService.RetryWhenNothingHappenedAsync<string>(_ =>
            {
                permanent++;
                throw new InvalidOperationException("Codex CLI app-server rejected the model turn");
            }, null, CancellationToken.None));
        Assert.Equal(1, permanent);
    }

    [Fact]
    public async Task A_cancelled_run_is_not_retried()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;

        await Assert.ThrowsAsync<TimeoutException>(() => ConversationTurnService.RetryWhenNothingHappenedAsync<string>(
            _ =>
            {
                calls++;
                cts.Cancel();
                throw new TimeoutException();
            }, null, cts.Token));

        Assert.Equal(1, calls);
    }
}
