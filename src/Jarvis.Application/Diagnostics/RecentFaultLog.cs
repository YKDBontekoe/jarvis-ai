namespace Jarvis.Application.Diagnostics;

/// <summary>
/// One repeated warning or error in Jarvis itself. It carries the log <em>template</em> (a constant in the
/// code) and code locations, never rendered values, so it can be shown to the agent without leaking any
/// owner's data.
/// </summary>
public sealed record FaultEntry(DateTimeOffset LastSeen, string Level, string Category, string Template,
    string? ExceptionType, IReadOnlyList<string> Frames, int Count);

public interface IRecentFaultLog
{
    void Record(string level, string category, string template, string? exceptionType, IReadOnlyList<string> frames);

    IReadOnlyList<FaultEntry> Recent(int limit);
}

/// <summary>Bounded in-memory record of the latest distinct faults, newest first.</summary>
public sealed class RecentFaultLog(TimeProvider? clock = null) : IRecentFaultLog
{
    private const int Capacity = 100;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly List<FaultEntry> _entries = [];
    private readonly object _gate = new();

    public void Record(string level, string category, string template, string? exceptionType,
        IReadOnlyList<string> frames)
    {
        lock (_gate)
        {
            var index = _entries.FindIndex(entry => entry.Category == category && entry.Template == template &&
                                                     entry.ExceptionType == exceptionType);
            var count = 1;
            if (index >= 0)
            {
                count = _entries[index].Count + 1;
                _entries.RemoveAt(index);
            }

            _entries.Insert(0, new FaultEntry(_clock.GetUtcNow(), level, category, template, exceptionType, frames, count));
            if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }
    }

    public IReadOnlyList<FaultEntry> Recent(int limit)
    {
        lock (_gate) return _entries.Take(Math.Clamp(limit, 1, Capacity)).ToArray();
    }
}
