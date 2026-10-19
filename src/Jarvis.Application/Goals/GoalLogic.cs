namespace Jarvis.Application.Goals;

/// <summary>Something that could move a goal forward this week, before it is fitted to the time available.</summary>
public sealed record PlanCandidate(string Key, string Title, int Minutes, string? Ref);

/// <summary>
/// Pure rules for goals, tested with fixed days. Nothing here nags: a missing check-in is only "due", a declined
/// suggestion stays quiet for a month, and a missed week is never counted against the owner.
/// </summary>
public static class GoalLogic
{
    public static DateOnly MondayOf(DateOnly day) =>
        day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static IReadOnlyList<GoalCheckIn> CurrentSeason(GoalRecord goal) =>
        goal.CheckIns.Where(item => item.Season == goal.Season).OrderBy(item => item.At).ToArray();

    /// <summary>
    /// Due when the owner chose a rhythm and the last check-in (or the day the goal began) is at least that old.
    /// Only active goals are ever due; this never escalates.
    /// </summary>
    public static bool CheckInDue(GoalRecord goal, DateOnly today, TimeZoneInfo zone) =>
        DaysSinceCheckIn(goal, today, zone) is { } days && days >= goal.CheckInEveryDays &&
        goal.Status == GoalStatuses.Active;

    public static int? DaysSinceCheckIn(GoalRecord goal, DateOnly today, TimeZoneInfo zone)
    {
        var last = CurrentSeason(goal).LastOrDefault();
        var from = last?.At ?? goal.CreatedAt;
        return Math.Max(0, today.DayNumber - DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime)
            .DayNumber);
    }

    /// <summary>
    /// Keep, change or stop, from the owner's own check-ins. Needs at least two this season; a dismissed
    /// suggestion stays quiet for <see cref="GoalRules.QuietDays"/> days.
    /// </summary>
    public static GoalReviewSuggestion? Review(GoalRecord goal, DateOnly today)
    {
        if (goal.Status != GoalStatuses.Active) return null;
        if (goal.ReviewDismissedOn is { } dismissed && dismissed.AddDays(GoalRules.QuietDays) > today) return null;

        var recent = CurrentSeason(goal).TakeLast(3).ToArray();
        if (goal.Horizon is { } horizon && horizon < today)
            return new GoalReviewSuggestion(GoalVerdicts.Stop,
                $"The horizon ({horizon:d MMM yyyy}) has passed. Mark it achieved, move the date, or let it go.",
                recent.Select(item => item.Id).ToArray());
        if (recent.Length < 2) return null;

        var stuck = recent.Count(item => item.Feeling == GoalFeelings.Stuck);
        var slipping = recent.Count(item => item.Feeling == GoalFeelings.Slipping);
        var basis = recent.Select(item => item.Id).ToArray();
        if (stuck >= 2)
            return new GoalReviewSuggestion(GoalVerdicts.Change,
                $"{stuck} of your last {recent.Length} check-ins said stuck. Try a smaller step or a different approach.",
                basis);
        if (stuck + slipping >= 2)
            return new GoalReviewSuggestion(GoalVerdicts.Change,
                $"Your last {recent.Length} check-ins mention slipping or stuck. What would make it easier?", basis);
        if (recent.All(item => item.Feeling == GoalFeelings.OnTrack))
            return new GoalReviewSuggestion(GoalVerdicts.Keep,
                $"Your last {recent.Length} check-ins say on track. Keep going as you are.", basis);
        return null;
    }

    /// <summary>
    /// Fits candidates, in order, into the weekly minutes the owner allows (at most three, nothing declined within
    /// the quiet period). What does not fit is counted, not squeezed in.
    /// </summary>
    public static GoalPlan Propose(GoalRecord goal, DateOnly today, IEnumerable<PlanCandidate> candidates,
        IEnumerable<GoalPlanItem> kept)
    {
        var week = MondayOf(today);
        var items = kept.ToList();
        var used = items.Where(item => item.Status != GoalPlanStatuses.Declined).Sum(item => item.Minutes);
        var omitted = 0;
        foreach (var candidate in candidates)
        {
            if (items.Count(item => item.Status != GoalPlanStatuses.Declined) >= GoalRules.MaxPlanItems) break;
            if (items.Any(item => item.Key == candidate.Key)) continue;
            if (goal.Declined.TryGetValue(candidate.Key, out var declined) &&
                declined.AddDays(GoalRules.QuietDays) > today) continue;
            if (used + candidate.Minutes > goal.WeeklyMinutes)
            {
                omitted++;
                continue;
            }
            used += candidate.Minutes;
            items.Add(new GoalPlanItem(Guid.NewGuid(), candidate.Title, candidate.Minutes, GoalPlanStatuses.Proposed,
                candidate.Ref, candidate.Key));
        }
        return new GoalPlan(week, items, omitted);
    }

    public static string Clean(string? value, int max)
    {
        var text = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > max ? text[..max] : text;
    }
}
