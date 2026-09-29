using System.Diagnostics;

namespace Jarvis.Worker.Hosting;

internal static class JarvisWorkerTelemetry
{
    public static readonly ActivitySource Source = new("Jarvis.Worker");
}
