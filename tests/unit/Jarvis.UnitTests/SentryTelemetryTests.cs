using System.Diagnostics;
using Jarvis.Agents.Telemetry;
using Jarvis.Api.Errors;
using Jarvis.Application.Usage;
using Jarvis.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Jarvis.UnitTests;

public class SentryTelemetryTests
{
    [Fact]
    public void EmptyDsnLeavesSentryDisabled()
    {
        Assert.False(JarvisSentry.IsEnabled(null));
        Assert.False(JarvisSentry.IsEnabled("  "));
        Assert.True(JarvisSentry.IsEnabled("https://public@example.ingest.sentry.io/1"));
    }

    [Fact]
    public void HealthChecksAreNotSampled()
    {
        Assert.Equal(0, JarvisSentry.SampleTransaction("GET /health"));
        Assert.Equal(0, JarvisSentry.SampleTransaction("/alive"));
        Assert.Null(JarvisSentry.SampleTransaction("POST /api/v1/conversations"));
    }

    [Fact]
    public void ProductionTraceSampleRateDefaultsBelowDevelopment()
    {
        var production = new ConfigurationBuilder().Build();
        Assert.Equal(JarvisSentry.DefaultProductionTracesSampleRate,
            JarvisSentry.ResolveTracesSampleRate(production, isDevelopment: false));
        Assert.Equal(1, JarvisSentry.ResolveTracesSampleRate(production, isDevelopment: true));
    }

    [Fact]
    public void AuthorizationHeaderIsScrubbed()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer secret-token",
            ["Cookie"] = "session=abc",
            ["Accept"] = "application/json"
        };

        JarvisSentry.ScrubHeaders(headers);

        Assert.Equal("[redacted]", headers["Authorization"]);
        Assert.Equal("[redacted]", headers["Cookie"]);
        Assert.Equal("application/json", headers["Accept"]);
        Assert.True(JarvisSentry.LooksSensitive("Host=db;Password=secret"));
        Assert.False(JarvisSentry.LooksSensitive("validation failed"));
    }

    [Fact]
    public void ClientErrorsAreNotIssuesAndServerErrorsAre()
    {
        Assert.False(JarvisSentryExceptions.ShouldCapture(new ArgumentException("bad request")));
        Assert.False(JarvisSentryExceptions.ShouldCapture(new UnauthorizedAccessException()));
        Assert.True(JarvisSentryExceptions.ShouldCapture(new InvalidOperationException("boom")));
        Assert.True(JarvisSentryExceptions.ShouldCapture(new TimeoutException("timed out")));
    }

    [Fact]
    public void AiContentStaysOffUnlessConfigured()
    {
        Assert.False(JarvisSentry.RecordAiContent(new ConfigurationBuilder().Build()));
        var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sentry:RecordAiContent"] = "true"
        }).Build();
        Assert.True(JarvisSentry.RecordAiContent(enabled));
    }

    [Fact]
    public void ModelClientSpansAreNotExportedTwice()
    {
        Assert.False(JarvisSentry.ShouldExportActivity("Jarvis.OpenRouterChatClient", "chat gpt"));
        Assert.False(JarvisSentry.ShouldExportActivity("Jarvis.CodexChatClient", "chat"));
        Assert.True(JarvisSentry.ShouldExportActivity("Jarvis.CodexChatClient", "jarvis.model.completion"));
        Assert.True(JarvisSentry.ShouldExportActivity("Jarvis.Api", "jarvis.agent.run"));
        Assert.False(JarvisSentry.ShouldExportActivity("Microsoft.AspNetCore", "GET /health"));
    }

    [Fact]
    public void CodexSpanCarriesGenAiAttributesWithoutPromptText()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Jarvis.CodexChatClient",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("Jarvis.CodexChatClient");
        using var activity = source.StartActivity("jarvis.model.completion");
        GenAiTelemetry.TagChat(activity, "openai", "gpt-test");
        GenAiTelemetry.TagUsage(activity, 3, 5, 1, 2);

        Assert.Equal("chat", activity?.GetTagItem(GenAiTelemetry.OperationName));
        Assert.Equal("openai", activity?.GetTagItem(GenAiTelemetry.ProviderName));
        Assert.Equal("gpt-test", activity?.GetTagItem(GenAiTelemetry.RequestModel));
        Assert.Equal(3L, activity?.GetTagItem(GenAiTelemetry.InputTokens));
        Assert.DoesNotContain("gen_ai.input", activity?.Tags.Select(tag => tag.Key) ?? []);
        Assert.DoesNotContain("prompt", activity?.Tags.Select(tag => tag.Key) ?? []);
    }

    [Fact]
    public void PricedCallSetsSeparateInputAndOutputCost()
    {
        var split = UsageCost.Split(1_000_000, 2_000_000, new TokenPrice(3m, 6m));
        Assert.Equal((3m, 12m, 15m), split);
        Assert.Null(UsageCost.Split(1, 1, null));
    }
}
