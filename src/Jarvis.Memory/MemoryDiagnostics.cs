using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Jarvis.Memory;

public static class MemoryDiagnostics
{
    public const string SourceName = "Jarvis.Memory";
    public const string MeterName = "Jarvis.Memory";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> SearchDuration = Meter.CreateHistogram<double>(
        "jarvis.memory.search.duration", "ms", "Duration of owner-scoped memory retrieval.");
    public static readonly Counter<long> SearchResults = Meter.CreateCounter<long>(
        "jarvis.memory.search.results", "{record}", "Number of memory records returned by search.");
}
