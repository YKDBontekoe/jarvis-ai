using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Jarvis.Application.Search;

public static class SearchDiagnostics
{
    public const string ActivitySourceName = "Jarvis.Search";
    public const string MeterName = "Jarvis.Search";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> ProviderLatency = Meter.CreateHistogram<double>(
        "jarvis.search.provider.duration", "ms", "Latency of a federated search provider.");

    public static readonly Histogram<long> ProviderResultCount = Meter.CreateHistogram<long>(
        "jarvis.search.provider.results", "{result}", "Result count returned by a federated search provider.");

    public static readonly Counter<long> ProviderFailures = Meter.CreateCounter<long>(
        "jarvis.search.provider.failures", "{failure}", "Federated search provider failures.");

    public static readonly Counter<long> ProviderTimeouts = Meter.CreateCounter<long>(
        "jarvis.search.provider.timeouts", "{timeout}", "Federated search provider timeouts.");
}
