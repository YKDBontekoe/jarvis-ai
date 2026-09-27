using Jarvis.Mcp;
using Microsoft.Extensions.AI;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class SecretRedactingAIFunctionTests
{
    [Fact]
    public async Task Wrapped_cancellation_is_rethrown()
    {
        var inner = AIFunctionFactory.Create(string () =>
            throw new AggregateException(new OperationCanceledException()), "fixture");
        var function = new SecretRedactingAIFunction(inner, ["secret-token"]);

        await Assert.ThrowsAsync<OperationCanceledException>(() => function.InvokeAsync().AsTask());
    }

    [Fact]
    public async Task Exception_messages_are_redacted()
    {
        var inner = AIFunctionFactory.Create(string () =>
            throw new InvalidOperationException("token secret-token leaked"), "fixture");
        var function = new SecretRedactingAIFunction(inner, ["secret-token"]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => function.InvokeAsync().AsTask());
        Assert.Equal("token [REDACTED] leaked", error.Message);
        Assert.DoesNotContain("secret-token", error.Message);
    }

    [Fact]
    public async Task Successful_string_results_are_redacted()
    {
        var inner = AIFunctionFactory.Create(() => "using secret-token", "fixture");
        var function = new SecretRedactingAIFunction(inner, ["secret-token"]);

        var result = await function.InvokeAsync();
        Assert.Equal("using [REDACTED]", result?.ToString());
    }
}
