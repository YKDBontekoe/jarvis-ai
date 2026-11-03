using System.Text.Json;
using Jarvis.Application.Automations;
using Jarvis.Application.Events;
using Jarvis.Application.Library;
using Jarvis.Application.Planner;

namespace Jarvis.Application.Proactivity;

/// <summary>Preparation reads permitted facts. The only automatic cross-feature write is an atomic internal day plan.</summary>
public sealed class ProactivePreparationService(IOwnerSettingDocuments documents, IDayPlannerService planner,
    IOwnerEventRepository events, IAutomationRuleRepository automations, IWebPageFetcher pages,
    TimeProvider? timeProvider = null, IProactiveCapacityService? capacity = null,
    IProactiveClaimReviewer? claimReviewer = null) : IProactivePreparationService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProactivePreparation> PrepareAsync(ProactiveCase value, IReadOnlyList<ProactiveSource> sources, CancellationToken ct)
    {
        if (value.Kind == "day_plan") return await DayAsync(value, sources, ct);
        if (value.Kind == "routine_trial") return await TrialAsync(value, sources, ct);
        if (value.Kind == "research") return await ResearchAsync(value, sources, ct);
        if (value.Kind == "goal_capacity") return new(sources, await CapacityAsync(value, sources, ct));
        var lines = sources.Select(s => $"- {s.Title}: {s.Detail} [{s.Ref}]").ToArray();
        var report = value.Kind switch
        {
            "meeting" => "Next agenda draft from the meetings you selected. Private notes are excluded.\n" +
                string.Join('\n', sources.SelectMany(s => s.Detail.Split('\n')).Where(line => !line.StartsWith("Resolved accepted:", StringComparison.Ordinal)).Distinct()),
            "trip" => Trip(value, sources),
            "consistency" => "Current authoritative sources. Changed facts require dependent drafts to be reviewed; owner-written sections are preserved:\n" + string.Join('\n', lines),
            "project" => "Project update draft\n\nOutcome: " + value.Objective + "\n\nCompleted steps:\n" +
                string.Join('\n', value.Steps.Where(s => s.Done || string.IsNullOrWhiteSpace(s.CompletionCondition) && s.SourceRef is { } reference && sources.Any(source => source.Ref == reference && ProactiveRules.CompleteStates.Contains(source.State))).Select(s => "- " + s.Title)) +
                "\n\nCurrent evidence and blockers:\n" + string.Join('\n', lines),
            "context" => "Current evidence for continuing this work:\n" + string.Join('\n', lines),
            "return" => "Return case: keep the receipt, merchant policy, return-by date, submission reference and tracking evidence together.\n" + string.Join('\n', lines),
            "refund" => "Refund case: keep the original payment, request reference, promised response date and confirmation together. An expected refund is not a received refund.\n" + string.Join('\n', lines),
            "repair" => "Repair case: keep proof of purchase, warranty terms, fault description, repair reference and agreed collection date together.\n" + string.Join('\n', lines),
            "follow_through" => "Follow-through preparation:\n" + string.Join('\n', lines) +
                "\nReview any draft in the linked inbox before sending. A reply does not complete the promise.",
            _ => null
        };
        return new(sources, report);
    }

    private async Task<string> CapacityAsync(ProactiveCase value, IReadOnlyList<ProactiveSource> sources, CancellationToken ct)
    {
        var snapshot = capacity is null ? null : await capacity.CalculateAsync(value, ct);
        if (value.Policy.UseCalendarCapacity && snapshot is null) return "Weekly calendar capacity is unavailable. No goal was changed.";
        var supplied = snapshot is null ? value.AvailableMinutes : snapshot.RemainingMinutes;
        var steps = value.Steps.Count > 0 ? value.Steps : sources.Where(s => s.Ref.StartsWith("goal:", StringComparison.Ordinal) && s.State == "active")
            .Select(s => new ProactiveStep(s.Ref, s.Title, [], Minutes: s.EffortMinutes)).ToArray();
        if (supplied is not { } available || steps.Count == 0 || steps.Any(s => !s.Done && s.Minutes is null))
            return (snapshot?.Explanation ?? "") + "\nWeekly capacity is unknown. Supply a shared time budget and estimates for all selected goal steps.";
        var remaining = available;
        var lines = new List<string> { snapshot?.Explanation ?? $"Shared weekly budget: {available} minutes.", "Steps are allocated in your chosen priority order." };
        foreach (var step in steps.Where(s => !s.Done))
        {
            var allocated = Math.Min(remaining, step.Minutes!.Value);
            remaining -= allocated;
            lines.Add($"{step.Title}: {allocated} of {step.Minutes} minutes fit." + (allocated < step.Minutes ? " Reduce this step or defer it; no goal was changed." : ""));
        }
        return string.Join('\n', lines);
    }

    private string Trip(ProactiveCase value, IReadOnlyList<ProactiveSource> sources)
    {
        var lines = new List<string> { "Trip review. Linked watch changes require verification; no booking or cancellation is performed." };
        lines.AddRange(ProactiveTripChanges.Build(value, sources, clock.GetUtcNow()));
        foreach (var source in sources.Where(s => s.Ref.StartsWith("watch:", StringComparison.Ordinal)))
            lines.Add($"Watch: {source.Title} — {source.Detail} [{source.Ref}]");
        foreach (var source in sources.Where(s => s.TripOptions is not null))
        {
            lines.AddRange(source.Detail.Split('\n').Where(line => line.StartsWith("Checklist open:", StringComparison.Ordinal) ||
                line.StartsWith("Suggested preparation hold:", StringComparison.Ordinal)));
            foreach (var chosen in source.TripOptions!.Where(o => o.Chosen))
            {
                lines.Add($"Chosen: {chosen.Title}; cancellation deadline {chosen.CancelBy?.ToString("yyyy-MM-dd") ?? "unknown"}.");
                foreach (var alternative in source.TripOptions!.Where(o => !o.Chosen && o.Kind == chosen.Kind && o.Availability != "sold_out").Take(3))
                {
                    var difference = alternative.Price is { } other && chosen.Price is { } current && alternative.Currency == chosen.Currency
                        ? (other - current).ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + chosen.Currency : "unknown";
                    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(value.Policy.TimeZoneId)).DateTime);
                    var fresh = alternative.QuotedOn is { } quoted && quoted >= today.AddDays(-7);
                    lines.Add($"Saved alternative to verify: {alternative.Title}; price difference {difference}; availability {alternative.Availability}; " +
                        $"quote {(fresh ? "recent" : "stale or unknown")}; dates {alternative.ForStart}–{alternative.ForEnd}; source {alternative.SourceUrl ?? "unknown"}. " +
                        $"Cancellation: {chosen.Cancellation ?? "unknown"} → {alternative.Cancellation ?? "unknown"}; alternative cancel-by {alternative.CancelBy?.ToString("yyyy-MM-dd") ?? "unknown"}.");
                }
            }
        }
        return string.Join('\n', lines);
    }

    private async Task<ProactivePreparation> DayAsync(ProactiveCase value, IReadOnlyList<ProactiveSource> sources, CancellationToken ct)
    {
        if (value.ProfileId is not null) throw new ArgumentException("Day replanning is an owner-wide responsibility; choose no restricted profile.");
        var raw = await documents.ReadDocumentAsync(value.OwnerId, "planner.day", ct);
        if (raw is null) return new(sources, "Add items to Today before enabling adaptive replanning.");
        var state = JsonSerializer.Deserialize<DayPlanState>(raw, Json)!;
        var today = await planner.GetTodayAsync(value.OwnerId, value.Policy.TimeZoneId, ct);
        if (today.CalendarUnavailable) throw new HttpRequestException("Calendar availability is unknown.");
        if (state.Date != today.Date) return new(sources, "The day rolled over. Review Today before the next automatic replan.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(today.TimeZoneId);
        var start = Workflows.LocalClock.Resolve(today.Date.ToDateTime(today.DayStart), zone);
        var end = Workflows.LocalClock.Resolve(today.Date.ToDateTime(today.DayEnd), zone);
        var now = clock.GetUtcNow();
        if (now > start) start = now;
        var busy = today.Entries.Where(e => e.Kind == DayTimelineKinds.Event && e.EndAt is { } until && until - e.StartAt < TimeSpan.FromDays(1))
            .Select(e => new TimeRange(e.StartAt, e.EndAt!.Value))
            .Concat(state.Items.Where(i => (i.Pinned || i.Done) && i.StartAt is not null)
                .Select(i => new TimeRange(i.StartAt!.Value, i.StartAt.Value.AddMinutes(i.Minutes))));
        var placed = DayPlanScheduler.Place(new(start, end), busy,
            state.Items.Where(i => !i.Done && !i.Pinned).Select(i => (i.Id, TimeSpan.FromMinutes(i.Minutes))));
        var items = state.Items.Select(i => i.Done || i.Pinned ? i : i with { StartAt = placed.TryGetValue(i.Id, out var at) ? at : null }).ToArray();
        var changed = !items.SequenceEqual(state.Items);
        var overflow = items.Where(i => !i.Done && i.StartAt is null).ToArray();
        var report = $"{placed.Count} flexible blocks fit. Pinned and completed blocks keep their time. {overflow.Length} items do not fit." +
            (overflow.Length == 0 ? "" : "\nStill open: " + string.Join(", ", overflow.Select(i => i.Title)));
        if (!today.CalendarConnected) report = "Calendar is not connected; this plan uses your chosen working hours only.\n" + report;
        var mutation = changed ? new ProactiveSettingMutation("planner.day", raw,
            ProactiveRules.Serialize(state with { Items = items, PlannedAt = now })) : null;
        return new(sources, report, Mutation: mutation);
    }

    private async Task<ProactivePreparation> ResearchAsync(ProactiveCase value, IReadOnlyList<ProactiveSource> sources, CancellationToken ct)
    {
        if (!sources.Any(s => s.Url is not null)) throw new ArgumentException("Choose a saved public web source to monitor. No source has been checked.");
        var refreshed = sources.ToList();
        var lines = new List<string>();
        foreach (var source in sources.Where(s => s.Url is not null).Take(5))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Reuses the library's public-address validation, redirect checks and response size limits.
                var page = await pages.FetchAsync(source.Url!, ct);
                var content = page.ContentType.Contains("html", StringComparison.OrdinalIgnoreCase)
                    ? HtmlTextExtractor.Extract(page.Body).Text : page.Body;
                content = LibraryRules.CleanContent(content);
                var version = ProactiveRules.Hash(content);
                var previous = value.Context.FirstOrDefault(s => s.Ref == source.Ref);
                var next = source with { Version = version, Detail = content[..Math.Min(content.Length, 4000)],
                    At = previous?.Version == version ? previous.At : clock.GetUtcNow(), State = "checked", OriginVersion = source.Version, Url = page.FinalUrl };
                refreshed[refreshed.FindIndex(s => s.Ref == source.Ref)] = next;
                if (previous?.Version != version)
                {
                    var oldParagraphs = previous?.State == "checked" ? previous.Detail.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToHashSet() : [];
                    var newParagraphs = next.Detail.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToHashSet();
                    var added = newParagraphs.Except(oldParagraphs).Take(5).ToArray();
                    var removed = oldParagraphs.Except(newParagraphs).Take(5).ToArray();
                    lines.Add($"New or changed evidence — {source.Title} [{page.FinalUrl}]:\n" + string.Join('\n', added));
                    if (removed.Length > 0) lines.Add($"Previous quotations to recheck [{page.FinalUrl}]:\n" + string.Join('\n', removed) +
                        "\nTheir absence does not establish that a claim is false. Review the source before changing a conclusion.");
                }
            }
            catch (Exception error) when (error is HttpRequestException or InvalidDataException or TimeoutException or WebFetchException ||
                                               error is TaskCanceledException && !ct.IsCancellationRequested)
            {
                refreshed[refreshed.FindIndex(s => s.Ref == source.Ref)] = source with { State = "unavailable", OriginVersion = source.Version };
                lines.Add($"{source.Title} [{source.Url}]: could not check this source. Its current claims are unknown.");
            }
        }
        foreach (var skipped in sources.Where(s => s.Url is not null).Skip(5))
        {
            refreshed[refreshed.FindIndex(s => s.Ref == skipped.Ref)] = skipped with { State = "unavailable", OriginVersion = skipped.Version };
            lines.Add($"{skipped.Title} [{skipped.Url}]: not checked because the selected-source limit is five. Its current claims are unknown.");
        }
        var reviews = new List<ProactiveClaimReview>();
        var pending = new List<ProactiveClaimEvidence>();
        foreach (var claim in value.Assumptions)
        {
            var source = refreshed.FirstOrDefault(s => s.Ref == claim.SourceRef);
            if (source is not { State: "checked", Url: not null })
            {
                reviews.Add(new(claim.Id, "unavailable", "The selected source could not be checked. This claim remains unknown.", null,
                    claim.SourceRef, source?.Version ?? "unknown", ProactiveRules.Hash(claim.Statement)));
                continue;
            }
            var cached = value.ClaimReviews?.FirstOrDefault(r => r.Id == claim.Id && r.SourceRef == claim.SourceRef &&
                r.SourceVersion == source.Version && r.ClaimHash == ProactiveRules.Hash(claim.Statement) && r.Status != "unavailable");
            if (cached is not null) reviews.Add(cached);
            else pending.Add(new(claim.Id, claim.Statement, source.Ref, source.Url, source.Version, source.Detail));
        }
        if (pending.Count > 0)
        {
            var checkedClaims = claimReviewer is null ? pending.Select(c => new ProactiveClaimReview(c.Id, "unavailable",
                "Claim review is unavailable; inspect the cited evidence before drawing a conclusion.", null, c.SourceRef, c.SourceVersion, ProactiveRules.Hash(c.Statement))).ToArray()
                : await claimReviewer.ReviewAsync(value.OwnerId, pending, ct);
            reviews.AddRange(checkedClaims);
        }
        reviews = reviews.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        var changed = refreshed.Count != value.Context.Count || refreshed.Any(source => value.Context.All(old =>
            old.Ref != source.Ref || old.Version != source.Version || old.State != source.State || old.Url != source.Url));
        var reviewsChanged = ProactiveRules.Serialize(reviews) != ProactiveRules.Serialize(value.ClaimReviews ?? []);
        if (!changed && !reviewsChanged && value.PreparedReport is not null)
            return new(refreshed, value.PreparedReport, ClaimReviews: reviews);
        var output = new List<string> { "Research question: " + value.Objective };
        if (value.Reports?.LastOrDefault() is { } prior) output.Add($"Compared with preparation from {prior.At:O}; open Previous preparation for that version.");
        output.AddRange(lines.Count == 0 ? ["The checked source text is unchanged."] : lines);
        if (reviews.Count > 0) output.Add("Important claims:");
        foreach (var review in reviews)
        {
            var claim = value.Assumptions.First(a => a.Id == review.Id);
            var url = refreshed.FirstOrDefault(s => s.Ref == review.SourceRef)?.Url ?? review.SourceRef;
            output.Add($"{claim.Statement}\n{review.Status}: {review.Reason}" + (review.Quote is null ? "" : $"\nEvidence: “{review.Quote}”") + $" [{url}]");
        }
        var unanswered = reviews.Where(r => r.Status is "unclear" or "unavailable").ToArray();
        if (unanswered.Length > 0) output.Add("Unanswered questions:\n" + string.Join('\n', unanswered.Select(r =>
            "- Verify: " + value.Assumptions.First(a => a.Id == r.Id).Statement)));
        return new(refreshed, string.Join("\n\n", output), ClaimReviews: reviews);
    }

    private async Task<ProactivePreparation> TrialAsync(ProactiveCase value, IReadOnlyList<ProactiveSource> sources, CancellationToken ct)
    {
        if (value.RootRef is null || !EntityRef.TryParse(value.RootRef, out var reference) || reference.Type != EntityTypes.Automation)
            throw new ArgumentException("Choose the draft automation to observe.");
        var rule = await automations.GetAsync(reference.Id, value.OwnerId, ct);
        if (rule is null) throw new ArgumentException("The automation is unavailable.");
        if (rule.Status == "enabled") throw new ArgumentException("Pause the automation before observing a trial without running actions.");
        var definition = AutomationDefinitionJson.Deserialize(rule.DefinitionJson);
        if (definition.Trigger is not (EventTriggerDefinition or ScheduleTriggerDefinition))
            throw new ArgumentException("Observation supports event-triggered and scheduled routines.");
        if (value.Context.FirstOrDefault(s => s.Ref == reference.ToString()) is { } prior &&
            sources.FirstOrDefault(s => s.Ref == reference.ToString()) is { } current && prior.Version != current.Version)
            throw new ArgumentException("The routine changed. Start a new observation so different definitions are not mixed.");
        var end = value.CreatedAt.AddDays(7);
        var observations = (value.TrialObservations ?? []).ToList();
        var known = observations.Select(o => o.EventId).ToHashSet();
        if (definition.Trigger is ScheduleTriggerDefinition schedule)
        {
            var cursor = value.CreatedAt;
            var partial = false;
            for (var i = 0; i < 200; i++)
            {
                var fire = AutomationScheduleClock.GetNextScheduleFireUtc(cursor, schedule);
                if (fire is null || fire > end || fire > clock.GetUtcNow()) break;
                var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{value.Id}:{fire.Value:O}"));
                var eventId = new Guid(bytes.AsSpan(0, 16));
                if (!known.Contains(eventId))
                {
                    var simulation = AutomationSimulator.Simulate(definition, null, fire.Value);
                    observations.Add(new(eventId, fire.Value, simulation.ActionsThatWouldRun, simulation.ApprovalsNeeded,
                        "Scheduled opportunity observed. Actions were not executed."));
                }
                cursor = fire.Value.AddTicks(1);
                if (i == 199) partial = true;
            }
            var scheduleReport = $"Seven-day observation: {observations.Count} scheduled opportunities, {observations.Sum(o => o.Actions)} actions would run, {observations.Sum(o => o.Approvals)} approvals would be needed. No actions were executed.";
            if (partial) scheduleReport += " The observation reached its limit and is partial.";
            if ((definition.Conditions ?? []).Any(condition => condition is not TimeWindowConditionDefinition))
                scheduleReport += " Live data conditions were not fetched; action and approval counts are upper estimates, not confirmed matches.";
            return new(sources, scheduleReport, observations);
        }
        var recent = await events.ListAsync(value.OwnerId, new OwnerEventQuery(value.CreatedAt, Limit: 200), ct);
        foreach (var ev in recent.Where(e => e.At <= end && !known.Contains(e.Id)).OrderBy(e => e.At))
        {
            var kind = ev.Kind switch { "task.completed" => "task_completed", "file.uploaded" => "file_uploaded",
                "message.received" => "message_received", "journal.saved" => "journal_saved", "expense.logged" => "expense_logged",
                "inbox.needs_reply" => "inbox_needs_reply", "webhook.called" => "webhook", _ => null };
            if (kind is null) continue;
            var simulation = AutomationSimulator.Simulate(definition, new AutomationEvent(kind, ev.Summary,
                ev.Data?.GetValueOrDefault("detail"), ev.Data?.GetValueOrDefault("source"), At: ev.At), ev.At);
            var previousRun = observations.Where(o => o.Actions > 0).Select(o => (DateTimeOffset?)o.At).Max();
            if (previousRun is { } last && ev.At < last.AddMinutes(definition.Limits?.CooldownMinutes ?? 0))
            {
                observations.Add(new(ev.Id, ev.At, 0, 0, "The routine's cooldown would skip this event."));
                continue;
            }
            observations.Add(new(ev.Id, ev.At, simulation.TriggerMatches ? simulation.ActionsThatWouldRun : 0,
                simulation.TriggerMatches ? simulation.ApprovalsNeeded : 0, simulation.TriggerNote ?? "Observed matching conditions; actions were not executed."));
        }
        observations = observations.TakeLast(500).ToList();
        var report = $"Seven-day observation: {observations.Count} events inspected, {observations.Sum(o => o.Actions)} actions would run, {observations.Sum(o => o.Approvals)} approvals would be needed. No actions were executed.";
        if (recent.Count == 200) report += " The event query reached its limit; this is a partial observation.";
        if ((definition.Conditions ?? []).Any(condition => condition is not TimeWindowConditionDefinition))
            report += " Live data conditions were not fetched; action and approval counts are upper estimates, not confirmed matches.";
        if (clock.GetUtcNow() >= end) report += " Observation has ended. Review the draft automation before enabling it yourself.";
        return new(sources, report, observations);
    }
}
