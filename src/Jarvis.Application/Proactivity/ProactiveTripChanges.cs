using System.Globalization;
using Jarvis.Application.Trips;
using Jarvis.Application.Workflows;

namespace Jarvis.Application.Proactivity;

/// <summary>Revises an internal proposal from an owner-mapped delay observation; never changes a booking.</summary>
public static class ProactiveTripChanges
{
    public static IReadOnlyList<string> Build(ProactiveCase value, IReadOnlyList<ProactiveSource> sources, DateTimeOffset now)
    {
        var lines = new List<string>();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(value.Policy.TimeZoneId);
        string At(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        foreach (var group in value.Assumptions.Where(a => a.Effect == "delay_minutes").GroupBy(a => a.TripRef))
        {
            var trip = sources.FirstOrDefault(s => s.Ref == group.Key);
            if (trip?.TripItems is null) { lines.Add("The linked itinerary is unavailable. A revised schedule is unknown."); continue; }
            var shifts = new Dictionary<string, (int Minutes, string Source, DateTimeOffset Start, DateTimeOffset End)>();
            foreach (var binding in group)
            {
                var observation = sources.FirstOrDefault(s => s.Ref == binding.SourceRef);
                var item = trip.TripItems.FirstOrDefault(i => i.Id == binding.TripItemId);
                if (item is null || !item.Verified || observation is null || observation.State is not ("active" or "triggered") ||
                    observation.At is null || observation.At > now.AddMinutes(5) || observation.ValidUntil <= now ||
                    observation.NumericValue is not { } delay || !double.IsFinite(delay) || delay < 0 || delay > 2880)
                {
                    lines.Add($"{binding.Statement}: the source, original schedule or observation is unknown. Verify it before changing the itinerary. [{binding.SourceRef}]");
                    continue;
                }
                var baselineStart = LocalClock.Resolve((binding.BaselineDate ?? item.Date).ToDateTime(binding.BaselineStart ?? item.Start), zone);
                var baselineEnd = LocalClock.Resolve((binding.BaselineDate ?? item.Date).ToDateTime(binding.BaselineEnd ?? item.End), zone);
                var minutes = (int)Math.Ceiling(delay);
                var currentStart = LocalClock.Resolve(item.Date.ToDateTime(item.Start), zone);
                var currentEnd = LocalClock.Resolve(item.Date.ToDateTime(item.End), zone);
                bool Matches(int offset) => currentStart == baselineStart.AddMinutes(offset) && currentEnd == baselineEnd.AddMinutes(offset);
                var priorDelay = value.Context.FirstOrDefault(s => s.Ref == binding.SourceRef)?.NumericValue;
                var matchesPrior = priorDelay is >= 0 and <= 2880 && Matches((int)Math.Ceiling(priorDelay.Value));
                if (baselineEnd <= baselineStart || !(Matches(0) || Matches(minutes) || matchesPrior))
                { lines.Add($"{item.Title} was edited separately. Review the original schedule and delay mapping before applying this observation."); continue; }
                shifts[item.Id] = (minutes, observation.Ref, baselineStart, baselineEnd);
            }
            if (shifts.Count == 0) continue;
            lines.Add($"Revised proposal for {trip.Title}. Times use {zone.Id}; existing reservations and the saved itinerary remain unchanged.");
            DateTimeOffset? cursor = null;
            var changed = false;
            foreach (var item in trip.TripItems.OrderBy(i => i.Date).ThenBy(i => i.Start))
            {
                var originalStart = LocalClock.Resolve(item.Date.ToDateTime(item.Start), zone);
                var originalEnd = LocalClock.Resolve(item.Date.ToDateTime(item.End), zone);
                if (originalEnd <= originalStart) { lines.Add($"{item.Title}: confirm the end time before replanning it."); continue; }
                var start = originalStart;
                var end = originalEnd;
                if (shifts.TryGetValue(item.Id, out var shift))
                {
                    start = shift.Start.AddMinutes(shift.Minutes);
                    end = shift.End.AddMinutes(shift.Minutes);
                    changed |= shift.Minutes > 0;
                    lines.Add($"Selected source confirms a {shift.Minutes}-minute shift for {item.Title}: {At(shift.Start)}–{At(shift.End)} → {At(start)}–{At(end)}. [{shift.Source}]");
                }
                if (!changed) continue;
                if (cursor is { } previous && start < previous.AddMinutes(item.TravelMinutesBefore))
                {
                    var earliest = previous.AddMinutes(item.TravelMinutesBefore);
                    if (item.Kind is TripKinds.Travel or TripKinds.Stay || shifts.ContainsKey(item.Id))
                        lines.Add($"Affected connection: {item.Title} at {At(start)} cannot be reached before {At(earliest)}. Check the saved alternatives and confirm a replacement; do not assume this reservation moved.");
                    else
                    {
                        end = earliest + (originalEnd - originalStart);
                        start = earliest;
                        lines.Add($"Affected: {item.Title}. Propose {At(start)}–{At(end)}, allowing {item.TravelMinutesBefore} transfer minutes. Verify the venue or reservation before adopting this time.");
                        var localEnd = TimeZoneInfo.ConvertTime(end, zone);
                        if (item.ClosesAt is { } closes && TimeOnly.FromDateTime(localEnd.DateTime) > closes)
                            lines.Add($"{item.Title} would finish after the supplied closing time {closes:HH:mm}; reduce it or choose another activity.");
                    }
                }
                if (shifts.Count > 0 && (cursor is null || end > cursor)) cursor = end;
            }
        }
        return lines;
    }
}
