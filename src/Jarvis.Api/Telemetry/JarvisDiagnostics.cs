using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Jarvis.Api.Telemetry;

public static class JarvisDiagnostics
{
    public const string SourceName = "Jarvis.Api";
    public const string MeterName = "Jarvis.Api";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> AgentRunDuration = Meter.CreateHistogram<double>(
        "jarvis.agent.run.duration", "ms", "Duration of a Jarvis agent run.");
    public static readonly Histogram<double> AgentTimeToFirstToken = Meter.CreateHistogram<double>(
        "jarvis.agent.time_to_first_token", "ms", "Time from agent run start to its first streamed text token.");
    public static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>(
        "jarvis.agent.tool.calls", "{call}", "Jarvis agent tool call outcomes.");
    public static readonly Histogram<double> ToolDuration = Meter.CreateHistogram<double>(
        "jarvis.agent.tool.duration", "ms", "Duration of an agent tool call.");
}
