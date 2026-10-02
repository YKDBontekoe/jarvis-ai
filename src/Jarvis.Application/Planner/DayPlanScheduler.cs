namespace Jarvis.Application.Planner;

public readonly record struct TimeRange(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Length => End - Start;
}

/// <summary>
/// Places to-dos in the free time of one day. Deterministic and model-free, so "plan my day" is instant and the
/// same input always gives the same plan.
/// </summary>
public static class DayPlanScheduler
{
    /// <summary>Breathing room kept after every calendar event and between planned blocks.</summary>
    public static readonly TimeSpan Buffer = TimeSpan.FromMinutes(5);

    /// <summary>Free gaps shorter than this are not offered as slots.</summary>
    public static readonly TimeSpan MinimumSlot = TimeSpan.FromMinutes(15);

    /// <summary>Planned blocks start on this grid so the timeline reads cleanly.</summary>
    public static readonly TimeSpan Grid = TimeSpan.FromMinutes(5);

    /// <summary>Free gaps inside <paramref name="window"/> that no busy range covers.</summary>
    public static IReadOnlyList<TimeRange> FreeSlots(TimeRange window, IEnumerable<TimeRange> busy)
    {
        if (window.End <= window.Start) return [];
        var slots = new List<TimeRange>();
        var cursor = RoundUp(window.Start);
        foreach (var range in Merge(busy))
        {
            if (range.End <= cursor) continue;
            if (range.Start >= window.End) break;
            if (range.Start - cursor >= MinimumSlot)
                slots.Add(new TimeRange(cursor, range.Start));
            cursor = RoundUp(range.End > cursor ? range.End : cursor);
        }
        if (window.End - cursor >= MinimumSlot)
            slots.Add(new TimeRange(cursor, window.End));
        return slots;
    }

    /// <summary>
    /// Puts each item, in the given order, in the earliest free gap that holds it. Items that fit nowhere are
    /// left out of the result, so the caller can say so.
    /// </summary>
    public static IReadOnlyDictionary<Guid, DateTimeOffset> Place(TimeRange window, IEnumerable<TimeRange> busy,
        IEnumerable<(Guid Id, TimeSpan Length)> items)
    {
        var taken = Merge(busy).Select(range => new TimeRange(range.Start, range.End + Buffer)).ToList();
        var placed = new Dictionary<Guid, DateTimeOffset>();
        foreach (var (id, length) in items)
        {
            if (length <= TimeSpan.Zero) continue;
            foreach (var slot in FreeGaps(window, taken))
            {
                if (slot.Length < length) continue;
                placed[id] = slot.Start;
                taken.Add(new TimeRange(slot.Start, slot.Start + length + Buffer));
                taken = Merge(taken).ToList();
                break;
            }
        }
        return placed;
    }

    private static IEnumerable<TimeRange> FreeGaps(TimeRange window, IReadOnlyList<TimeRange> taken)
    {
        var cursor = RoundUp(window.Start);
        foreach (var range in taken)
        {
            if (range.End <= cursor) continue;
            if (range.Start >= window.End) break;
            if (range.Start > cursor) yield return new TimeRange(cursor, range.Start);
            cursor = RoundUp(range.End > cursor ? range.End : cursor);
        }
        if (window.End > cursor) yield return new TimeRange(cursor, window.End);
    }

    private static IReadOnlyList<TimeRange> Merge(IEnumerable<TimeRange> ranges)
    {
        var merged = new List<TimeRange>();
        foreach (var range in ranges.Where(range => range.End > range.Start).OrderBy(range => range.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = new TimeRange(last.Start, range.End > last.End ? range.End : last.End);
            }
            else
            {
                merged.Add(range);
            }
        }
        return merged;
    }

    private static DateTimeOffset RoundUp(DateTimeOffset value)
    {
        var ticks = Grid.Ticks;
        var remainder = value.UtcTicks % ticks;
        return remainder == 0 ? value : value.AddTicks(ticks - remainder);
    }
}
