using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Telemetry;

/// <summary>
/// Wraps chat clients with Sentry agent tracing. Prompt and response text stay off unless the host opts in.
/// </summary>
internal static class SentryChatInstrumentation
{
#pragma warning disable SENTRY0001 // Sentry agent tracing is experimental.
    public static IChatClient Instrument(IChatClient client, bool recordContent) =>
        client.AddSentry(options =>
        {
            options.Experimental.RecordInputs = recordContent;
            options.Experimental.RecordOutputs = recordContent;
            options.Experimental.AgentName = "Jarvis";
        });

    public static ChatOptions InstrumentTools(ChatOptions options)
    {
        options.AddSentryToolInstrumentation();
        return options;
    }
#pragma warning restore SENTRY0001
}
