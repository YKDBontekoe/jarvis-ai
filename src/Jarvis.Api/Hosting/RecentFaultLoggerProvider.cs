using System.Text.RegularExpressions;
using Jarvis.Application.Diagnostics;

namespace Jarvis.Api.Hosting;

/// <summary>Feeds warnings and errors from Jarvis's own code into <see cref="IRecentFaultLog"/>.</summary>
internal sealed partial class RecentFaultLoggerProvider(IRecentFaultLog faults) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FaultLogger(faults, categoryName);

    public void Dispose()
    {
    }

    private sealed class FaultLogger(IRecentFaultLog faults, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Warning && category.StartsWith("Jarvis.", StringComparison.Ordinal);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                // The structured template is a constant in the code; the rendered message may hold user data.
                var template = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.FirstOrDefault(pair => pair.Key == "{OriginalFormat}").Value as string
                    : null;
                template ??= "(no message template)";
                faults.Record(logLevel.ToString(), category, template.Length > 300 ? template[..300] : template,
                    exception?.GetType().FullName, exception is null ? [] : Frames(exception));
            }
            catch
            {
                // Diagnostics must never break the code that is logging.
            }
        }
    }

    private static IReadOnlyList<string> Frames(Exception exception) =>
        (exception.StackTrace ?? string.Empty)
        .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(line => line.Contains(" Jarvis.", StringComparison.Ordinal))
        .Select(line => Location().Replace(line, m => $" in {Path.GetFileName(m.Groups[1].Value)}:line {m.Groups[2].Value}"))
        .Take(6)
        .ToArray();

    [GeneratedRegex(@" in (\S.*?):line (\d+)")]
    private static partial Regex Location();
}
