using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jarvis.Application.Proactivity;

public static class ProactiveRules
{
    public static readonly IReadOnlySet<string> CompleteStates = new HashSet<string>(StringComparer.Ordinal)
        { "completed", "done", "fulfilled", "resolved", "achieved" };

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Fingerprint(ProactiveCaseDraft draft) => Hash(string.Join('|', draft.Kind, draft.ProfileId,
        draft.Kind == "open_loop" ? null : draft.RootRef, string.Join(' ', draft.Objective.Trim().ToLowerInvariant().Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries))));

    public static void Validate(ProactiveCaseDraft draft)
    {
        if (!ProactiveKinds.All.Contains(draft.Kind)) throw new ArgumentException("Choose a supported responsibility.");
        if (string.IsNullOrWhiteSpace(draft.Title) || draft.Title.Length > 200 ||
            string.IsNullOrWhiteSpace(draft.Objective) || draft.Objective.Length > 2000)
            throw new ArgumentException("Give this work a title (up to 200 characters) and objective (up to 2,000).");
        if (draft.StopCondition?.Length > 1000 || draft.Notes?.Length > 4000 || draft.SourceRefs?.Count > 40 || draft.Steps?.Count > 40 ||
            draft.Assumptions?.Count > 20 || draft.AvailableMinutes is < 0 or > 10080)
            throw new ArgumentException("This responsibility exceeds its limits.");
        if (!draft.Explicit && draft.Enabled) throw new ArgumentException("Accept a suggested responsibility before enabling it.");
        var policy = draft.Policy ?? new();
        if (policy.UseCalendarCapacity && (draft.Kind != "goal_capacity" || draft.ProfileId is not null))
            throw new ArgumentException("Calendar capacity is an owner-wide goal responsibility. Create it in Ongoing work without a restricted profile.");
        if (policy.CadenceMinutes is < 15 or > 10080 || policy.MaxChecksPerDay is < 1 or > 96 ||
            policy.QuietStart is < 0 or > 23 || policy.QuietEnd is < 0 or > 23 || policy.OptionalItemsPerDay is < 0 or > 20)
            throw new ArgumentException("Choose a valid check cadence, daily limit and quiet hours.");
        if (string.IsNullOrWhiteSpace(policy.TimeZoneId)) throw new ArgumentException("Choose a valid time zone.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new ArgumentException("Choose a valid time zone."); }
        var steps = draft.Steps ?? [];
        if (steps.Any(s => s is null || s.DependsOn is null || string.IsNullOrWhiteSpace(s.Id) || s.Id.Length > 80 ||
                           string.IsNullOrWhiteSpace(s.Title) || s.Title.Length > 300 ||
                           s.Minutes is < 1 or > 10080 || s.CompletionCondition?.Length > 500 || s.DependsOn.Count > 40) ||
            steps.Select(s => s.Id).Distinct().Count() != steps.Count)
            throw new ArgumentException("Give each step a unique id, title and valid effort estimate.");
        var ids = steps.Select(s => s.Id).ToHashSet();
        if (steps.Any(s => s.DependsOn.Any(id => !ids.Contains(id) || id == s.Id)))
            throw new ArgumentException("Dependencies must refer to other steps in this responsibility.");
        var reached = new HashSet<string>();
        for (var i = 0; i < steps.Count; i++)
            foreach (var step in steps.Where(s => s.DependsOn.All(reached.Contains))) reached.Add(step.Id);
        if (reached.Count != steps.Count) throw new ArgumentException("Step dependencies contain a cycle.");
        if ((draft.Assumptions ?? []).Any(a => a is null || string.IsNullOrWhiteSpace(a.Statement) || a.Statement.Length > 500 ||
                string.IsNullOrWhiteSpace(a.Id) || a.Id.Length > 80 || string.IsNullOrWhiteSpace(a.SourceRef)))
            throw new ArgumentException("Give each assumption an id, statement and source.");
        if ((draft.Assumptions ?? []).Select(a => a.Id).Distinct().Count() != (draft.Assumptions?.Count ?? 0))
            throw new ArgumentException("Give each assumption a unique id.");
        foreach (var assumption in (draft.Assumptions ?? []).Where(a => a.Effect is not null))
            if (draft.Kind != "trip" || assumption.Effect != "delay_minutes" ||
                !Events.EntityRef.TryParse(assumption.SourceRef, out var watch) || watch.Type != Events.EntityTypes.Watch ||
                !Events.EntityRef.TryParse(assumption.TripRef, out var trip) || trip.Type != Events.EntityTypes.Trip ||
                string.IsNullOrWhiteSpace(assumption.TripItemId) || assumption.TripItemId.Length > 80)
                throw new ArgumentException("A trip delay must link a selected watch to one itinerary item.");
        var effects = (draft.Assumptions ?? []).Where(a => a.Effect is not null).ToArray();
        if (effects.Select(a => a.TripRef + ":" + a.TripItemId).Distinct().Count() != effects.Length)
            throw new ArgumentException("Choose one delay observation for each itinerary item.");
        if ((draft.Assumptions ?? []).Any(a => a.Comparison is not null &&
            (a.Comparison is not ("gt" or "gte" or "lt" or "lte" or "eq") || a.Threshold is null || !double.IsFinite(a.Threshold.Value))))
            throw new ArgumentException("Choose a supported comparison and finite threshold.");
    }

    public static bool Quiet(ProactivePolicy policy, DateTimeOffset now)
    {
        var hour = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZoneId)).Hour;
        return policy.QuietStart == policy.QuietEnd ? false : policy.QuietStart < policy.QuietEnd
            ? hour >= policy.QuietStart && hour < policy.QuietEnd : hour >= policy.QuietStart || hour < policy.QuietEnd;
    }

    public static ProactiveEvaluation Evaluate(ProactiveCase value, IReadOnlyList<ProactiveSource> sources,
        DateTimeOffset now)
    {
        var findings = new List<ProactiveFinding>();
        var byRef = sources.ToDictionary(s => s.Ref);
        var missing = value.SourceRefs.Where(r => !byRef.ContainsKey(r)).ToArray();
        foreach (var source in missing)
            findings.Add(new("missing:" + source, "A source is unavailable", "It was deleted, unavailable, or is outside this profile's access.", source));
        var steps = value.Steps.Select(step => string.IsNullOrWhiteSpace(step.CompletionCondition) && step.SourceRef is { } reference && byRef.TryGetValue(reference, out var source)
            && CompleteStates.Contains(source.State) ? step with { Done = true } : step).ToArray();
        var done = steps.Where(s => s.Done).Select(s => s.Id).ToHashSet();
        var eligible = steps.Where(s => !s.Done && s.DependsOn.All(done.Contains)).ToArray();
        var next = eligible.FirstOrDefault()?.Title ?? (steps.Length > 0 && steps.All(s => s.Done)
            ? "Review whether the objective is complete." : "Review the current evidence and choose the next step.");
        foreach (var source in sources)
        {
            var previous = value.Context.FirstOrDefault(s => s.Ref == source.Ref);
            if (previous is not null && previous.Version != source.Version)
                findings.Add(new("changed:" + source.Ref, "Supporting information changed", $"{source.Title}: {previous.State} → {source.State}. Review dependent plans and drafts.", source.Ref));
            if (source.State is "failed" or "needs_approval")
                findings.Add(new("blocked:" + source.Ref, source.Title, source.State == "failed"
                    ? "This work failed and needs a recovery decision." : "This work is waiting for approval.", source.Ref, "problem"));
            if (source.State == "unavailable")
                findings.Add(new("unavailable:" + source.Ref, "A source could not be checked", "Its current state is unknown. Review it before relying on this preparation.", source.Ref));
        }
        if (value.Deadline is { } deadline)
        {
            var remaining = steps.Where(s => !s.Done).ToArray();
            if (steps.Length == 0 || remaining.Any(s => s.Minutes is null) || value.AvailableMinutes is null)
                findings.Add(new("deadline:unknown", "Deadline feasibility is unknown", "Supply effort for each unfinished step and the time available before the deadline."));
            else
            {
                var needed = remaining.Sum(s => s.Minutes!.Value);
                var available = deadline <= now ? 0 : value.AvailableMinutes.Value;
                if (needed > available)
                    findings.Add(new("deadline:risk", "The remaining work does not fit", $"{needed} minutes of work − {available} available = {needed - available} minutes short. Reduce scope or move internal work.", Severity: "urgent"));
                if (needed > available)
                {
                    var index = findings.FindIndex(f => f.Key == "deadline:risk");
                    findings[index] = findings[index] with { Reason = findings[index].Reason + " " + Recovery(remaining, available) };
                }
            }
        }
        if (value.Kind == "follow_through")
        {
            var thread = sources.FirstOrDefault(s => s.State == "reply_received" && s.At >= value.CreatedAt);
            next = thread is null ? "Await a reply; review the linked promise before following up."
                : "A reply arrived. Review it and choose your next action; the promise is still open.";
        }
        foreach (var assumption in value.Assumptions)
        {
            if (!byRef.TryGetValue(assumption.SourceRef, out var source)) continue;
            if (assumption.AcknowledgedVersion == source.Version && assumption.ReviewAt is null) continue;
            var changed = assumption.ExpectedVersion is { } expected && expected != source.Version;
            if (assumption.Comparison is { } comparison)
            {
                if (source.NumericValue is not { } number || assumption.Threshold is not { } threshold ||
                    source.State is "failed" or "cancelled" or "unavailable" || source.ValidUntil <= now)
                {
                    findings.Add(new("assumption:unknown:" + assumption.Id, "An assumption could not be verified", "The source has no current numeric observation.", source.Ref));
                    continue;
                }
                changed = comparison switch { "gt" => number > threshold, "gte" => number >= threshold,
                    "lt" => number < threshold, "lte" => number <= threshold, "eq" => number == threshold, _ => false };
            }
            if (changed || assumption.ReviewAt <= now)
                findings.Add(new("assumption:" + assumption.Id, "Revisit an assumption", assumption.Statement, source.Ref));
        }
        foreach (var review in (value.ClaimReviews ?? []).Where(r => r.Status == "contradicted"))
        {
            var claim = value.Assumptions.FirstOrDefault(a => a.Id == review.Id && a.SourceRef == review.SourceRef && Hash(a.Statement) == review.ClaimHash);
            if (claim is not null && claim.AcknowledgedVersion != review.SourceVersion &&
                sources.Any(s => s.Ref == review.SourceRef && s.Version == review.SourceVersion && s.State == "checked"))
                findings.Add(new("claim:" + review.Id, "An important claim needs review", review.Reason, review.SourceRef));
        }
        if (value.Kind == "memory_conflict")
        {
            var memories = sources.Where(s => s.Ref.StartsWith("memory:", StringComparison.Ordinal)).ToArray();
            if (memories.Select(s => s.Detail).Distinct().Count() > 1)
                findings.Add(new("memory:compare", "Review these memories together", "Keep, replace, or retain both with different contexts. No memory has been changed."));
        }
        if (value.Kind is "return" or "refund" or "repair")
            next = eligible.FirstOrDefault()?.Title ?? "Check the evidence, reference number and next response deadline.";
        if (value.Kind == "trip" && value.Assumptions.Any(a => a.Effect == "delay_minutes"))
            next = "Review the trip-change preparation and affected itinerary items.";
        if (value.Kind == "routine_trial")
            next = now < value.CreatedAt.AddDays(7) ? "Observing for seven days. No routine actions are executed."
                : "Review observed matches before explicitly enabling the routine.";
        return new(next, findings, steps);
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static IReadOnlyList<ProactiveStep> DefaultSteps(string kind)
    {
        string[] titles = kind switch
        {
            "return" => ["Find the receipt and return policy", "Confirm the return-by date", "Prepare the return request", "Record the submitted reference and tracking", "Confirm the merchant received it and the outcome"],
            "refund" => ["Find the payment and purchase reference", "Prepare the refund request", "Record the submitted reference and response date", "Review their response", "Confirm the refund actually arrived"],
            "repair" => ["Gather purchase and warranty evidence", "Describe the fault and requested repair", "Record the repair reference", "Track the agreed collection or completion date", "Confirm the repair works"],
            _ => []
        };
        return titles.Select((title, index) => new ProactiveStep("step-" + index, title,
            index == 0 ? [] : ["step-" + (index - 1)], CompletionCondition: "You confirm this step is complete.")).ToArray();
    }

    public static string Failure(Exception error) => error switch
    {
        UnauthorizedAccessException => "permission",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized } => "permission",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.BadRequest } => "missing_input",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } => "unavailable",
        ArgumentException => "missing_input",
        HttpRequestException or TimeoutException => "transient",
        _ => "unavailable"
    };

    private static string Recovery(IReadOnlyList<ProactiveStep> remaining, int available)
    {
        if (available == 0) return "Recovery proposal: choose a new explicit deadline or confirm a smaller outcome. No date or promise has been changed.";
        var keep = remaining.ToList();
        var defer = new List<ProactiveStep>();
        var needed = keep.Sum(s => s.Minutes!.Value);
        while (needed > available && keep.Count > 0)
        {
            // Remove dependent leaves first, retaining a valid prerequisite plan and the owner's earlier priorities.
            var step = keep.Last(s => keep.All(other => !other.DependsOn.Contains(s.Id)));
            defer.Add(step);
            keep.Remove(step);
            needed -= step.Minutes!.Value;
        }
        if (keep.Count == 0)
            return $"Recovery proposal: agree a {available}-minute first pass on “{remaining[0].Title}”, leaving {remaining.Sum(s => s.Minutes!.Value) - available} minutes outside this deadline. Confirm whether that smaller result meets your completion condition before adopting it.";
        return "Recovery proposal: defer " + string.Join(", ", defer.Select(s => $"“{s.Title}” ({s.Minutes} minutes)")) +
            $" from this deadline's scope. The remaining {needed} minutes fit the supplied {available}-minute budget. Confirm the smaller scope before changing the plan.";
    }
}
