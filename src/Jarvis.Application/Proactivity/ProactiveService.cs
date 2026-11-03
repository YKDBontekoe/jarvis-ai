using Jarvis.Application.Events;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Proactivity;

public sealed class ProactiveService(IProactiveRepository repository, IProactiveSourceReader sources,
    IOwnerSettingsStore settings, TimeProvider? timeProvider = null,
    IProactivePreparationService? preparation = null,
    Workflows.IDailyBriefingRepository? briefings = null) : IProactiveService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ProactiveCase> CreateAsync(Guid ownerId, ProactiveCaseDraft draft, CancellationToken ct)
    {
        ProactiveRules.Validate(draft);
        if (!await sources.AllowsProfileAsync(ownerId, draft.ProfileId, ct)) throw new ArgumentException("The selected profile is unavailable.");
        var refs = (draft.SourceRefs ?? []).Concat(draft.RootRef is null ? [] : new[] { draft.RootRef })
            .Concat((draft.Steps ?? []).Select(s => s.SourceRef).OfType<string>())
            .Concat((draft.Assumptions ?? []).Select(a => a.SourceRef)).Distinct().ToArray();
        refs = refs.Concat((draft.Assumptions ?? []).Select(a => a.TripRef).OfType<string>()).Distinct().ToArray();
        if (refs.Length > 40 || refs.Any(r => !EntityRef.TryParse(r, out _)))
            throw new ArgumentException("Use at most 40 supported source references, written type:id.");
        refs = refs.Select(r => EntityRef.Parse(r).ToString()).Distinct().ToArray();
        draft = NormalizeReferences(draft);
        var visible = await sources.ReadAsync(ownerId, draft.ProfileId, refs, ct);
        if (visible.Count != refs.Length) throw new ArgumentException("A source is unavailable or outside this profile's access.");
        if (draft.Kind == "research" && visible.Count(s => s.Url is not null) > 5) throw new ArgumentException("Choose at most five public web sources for one research responsibility.");
        ValidateTripBindings(draft, visible);
        var now = clock.GetUtcNow();
        var policy = draft.Policy ?? new(TimeZoneId: briefings is null ? "UTC" :
            await Preferences.OwnerZoneResolver.ZoneIdAsync(settings, briefings, ownerId, ct));
        draft = draft with { RootRef = draft.RootRef is null ? null : EntityRef.Parse(draft.RootRef).ToString() };
        var fingerprint = ProactiveRules.Fingerprint(draft);
        var value = new ProactiveCase(Guid.CreateVersion7(), ownerId, fingerprint, draft.Kind, draft.Title.Trim(),
            draft.Objective.Trim(), draft.Explicit ? "open" : "suggested", draft.Enabled, draft.ProfileId,
            draft.RootRef, refs, draft.StopCondition, policy, draft.Steps is { Count: > 0 } ? draft.Steps : ProactiveRules.DefaultSteps(draft.Kind),
            InitialAssumptions(draft.Assumptions ?? [], visible),
            draft.Deadline, draft.AvailableMinutes, "Choose the next step.", visible, [], [], [], now, now,
            null, draft.Enabled ? now : null, ExpiresAt: draft.ExpiresAt, Notes: draft.Notes);
        var initial = ProactiveRules.Evaluate(value, visible, now);
        value = value with { NextAction = initial.NextAction, Findings = initial.Findings };
        value = value with { Receipts = [new(Guid.CreateVersion7(), "created",
            draft.Explicit ? "Tracked the owner's explicit request." : "Suggested an unfinished intention for owner review.",
            "", Projection(value), now, value.Revision,
            Trigger: draft.Explicit ? "owner_request" : "inferred_intention",
            EvidenceVersions: visible.ToDictionary(s => s.Ref, s => s.Version))] };
        // The unique fingerprint also retains terminal records so they cannot be resurrected by extraction.
        var stored = await repository.CreateAsync(value, ct);
        if (stored.Id != value.Id && !stored.Terminal)
        {
            if (draft.Explicit && (stored.Status == "suggested" || draft.Enabled && !stored.Enabled))
                stored = await SaveAsync(stored, stored with { Status = "open", Enabled = draft.Enabled || stored.Enabled,
                    NextCheckAt = draft.Enabled || stored.Enabled ? now : null }, "accepted", "Owner explicitly chose this responsibility.", ct);
            var merged = stored.SourceRefs.Concat(refs).Distinct().Take(40).ToArray();
            if (merged.Length != stored.SourceRefs.Count)
                stored = await SaveAsync(stored, stored with { SourceRefs = merged }, "sources", "Merged references to the same work.", ct);
        }
        return stored;
    }

    public async Task<IReadOnlyList<ProactiveCase>> ListAsync(Guid ownerId, CancellationToken ct) =>
        (await repository.ListAsync(ownerId, ct)).Select(c => c with { Context = [], Receipts = [],
            PreparedReport = null, TrialObservations = null, Reports = null, ClaimReviews = null }).ToArray();

    public async Task<ProactiveCase?> UpdateAsync(Guid ownerId, Guid id, ProactiveCaseDraft draft, long revision, CancellationToken ct)
    {
        ProactiveRules.Validate(draft);
        var current = await repository.GetAsync(ownerId, id, ct);
        if (current is null) return null;
        RequireRevision(current, revision);
        if (current.Terminal || draft.Kind != current.Kind || draft.ProfileId != current.ProfileId)
            throw new ArgumentException("The kind and profile cannot be changed on existing work.");
        var refs = (draft.SourceRefs ?? []).Concat(draft.RootRef is null ? [] : new[] { draft.RootRef })
            .Concat((draft.Steps ?? []).Select(s => s.SourceRef).OfType<string>())
            .Concat((draft.Assumptions ?? []).Select(a => a.SourceRef)).Distinct().ToArray();
        refs = refs.Concat((draft.Assumptions ?? []).Select(a => a.TripRef).OfType<string>()).Distinct().ToArray();
        if (refs.Length > 40 || refs.Any(r => !EntityRef.TryParse(r, out _))) throw new ArgumentException("Choose valid sources.");
        refs = refs.Select(r => EntityRef.Parse(r).ToString()).Distinct().ToArray();
        draft = NormalizeReferences(draft);
        var visible = await sources.ReadAsync(ownerId, current.ProfileId, refs, ct);
        if (visible.Count != refs.Length) throw new ArgumentException("A source is unavailable or outside this profile's access.");
        if (draft.Kind == "research" && visible.Count(s => s.Url is not null) > 5) throw new ArgumentException("Choose at most five public web sources for one research responsibility.");
        ValidateTripBindings(draft, visible);
        return await SaveAsync(current, current with { Title = draft.Title.Trim(), Objective = draft.Objective.Trim(),
            RootRef = draft.RootRef, SourceRefs = refs, StopCondition = draft.StopCondition, Policy = draft.Policy ?? current.Policy,
            Steps = draft.Steps ?? current.Steps,
            Assumptions = InitialAssumptions((draft.Assumptions ?? current.Assumptions).Select(a => {
                var old = current.Assumptions.FirstOrDefault(previous => previous.Id == a.Id);
                var changed = old is not null && (old.Statement != a.Statement || old.SourceRef != a.SourceRef || old.Comparison != a.Comparison ||
                    old.Threshold != a.Threshold || old.Effect != a.Effect || old.TripRef != a.TripRef || old.TripItemId != a.TripItemId);
                if (!changed || old is null) return a;
                return a with { AcknowledgedVersion = null,
                    ExpectedVersion = old.SourceRef == a.SourceRef ? a.ExpectedVersion : null,
                    BaselineDate = old.TripRef == a.TripRef && old.TripItemId == a.TripItemId ? a.BaselineDate : null,
                    BaselineStart = old.TripRef == a.TripRef && old.TripItemId == a.TripItemId ? a.BaselineStart : null,
                    BaselineEnd = old.TripRef == a.TripRef && old.TripItemId == a.TripItemId ? a.BaselineEnd : null };
            }).ToArray(), visible),
            Deadline = draft.Deadline, AvailableMinutes = draft.AvailableMinutes, Context = visible, ExpiresAt = draft.ExpiresAt, Notes = draft.Notes,
            PreparedReport = null, Reports = current.Reports?.Where(report => report.Sources.Keys.All(refs.Contains)).ToArray(),
            ClaimReviews = current.ClaimReviews?.Where(review => refs.Contains(review.SourceRef) &&
                (draft.Assumptions ?? current.Assumptions).Any(a => a.Id == review.Id && a.SourceRef == review.SourceRef &&
                    ProactiveRules.Hash(a.Statement) == review.ClaimHash)).ToArray(),
            NextCheckAt = current.Enabled ? clock.GetUtcNow() : null, RunId = null, LeaseUntil = null },
            "edited", "Owner updated the responsibility.", ct);
    }

    public async Task<ProactiveCase?> GetAsync(Guid ownerId, Guid id, CancellationToken ct)
    {
        var value = await repository.GetAsync(ownerId, id, ct);
        if (value is null) return null;
        // Context packs are always re-authorized, including after source deletion and profile changes.
        var current = await sources.ReadAsync(ownerId, value.ProfileId, value.SourceRefs, ct);
        var allowed = current.Select(s => s.Ref).ToHashSet();
        var pack = current.Select(source => value.Context.FirstOrDefault(old => old.Ref == source.Ref && old.OriginVersion == source.Version) ?? source).ToArray();
        var changed = allowed.Count != value.SourceRefs.Count || pack.Any(source => value.Context.All(old => old.Ref != source.Ref || old.Version != source.Version));
        var preview = !value.Terminal && value.Status != "needs_input" ? ProactiveRules.Evaluate(value, pack, clock.GetUtcNow()) : null;
        return value with { Context = pack,
            Steps = preview?.Steps ?? value.Steps,
            Reports = allowed.Count == value.SourceRefs.Count ? value.Reports?.Where(report => report.Sources.Keys.All(allowed.Contains)).ToArray() : null,
            ClaimReviews = value.ClaimReviews?.Where(review => pack.Any(s => s.Ref == review.SourceRef && s.Version == review.SourceVersion) &&
                value.Assumptions.Any(a => a.Id == review.Id && a.SourceRef == review.SourceRef && ProactiveRules.Hash(a.Statement) == review.ClaimHash)).ToArray(),
            PreparedReport = changed ? null : value.PreparedReport,
            NextAction = allowed.Count != value.SourceRefs.Count ? "A source is unavailable. Review the sources before continuing." : preview?.NextAction ?? value.NextAction,
            Findings = preview?.Findings ?? value.Findings.Where(f => f.SourceRef is null || allowed.Contains(f.SourceRef)).ToArray() };
    }

    public async Task<ProactiveCase?> CommandAsync(Guid ownerId, Guid id, string command, long revision, CancellationToken ct)
    {
        var current = await repository.GetAsync(ownerId, id, ct);
        if (current is null) return null;
        RequireRevision(current, revision);
        if (current.Terminal) throw new ArgumentException("Finished work cannot be restarted. Create a new responsibility for a new objective.");
        var now = clock.GetUtcNow();
        if (command.StartsWith("reviewed:", StringComparison.Ordinal))
        {
            var assumption = current.Assumptions.FirstOrDefault(a => a.Id == command[9..]) ?? throw new ArgumentException("No such assumption.");
            var evidence = (await sources.ReadAsync(ownerId, current.ProfileId, [assumption.SourceRef], ct)).FirstOrDefault()
                ?? throw new ArgumentException("The assumption's source is unavailable.");
            evidence = current.Context.FirstOrDefault(s => s.Ref == evidence.Ref && s.OriginVersion == evidence.Version) ?? evidence;
            return await SaveAsync(current, current with { Assumptions = current.Assumptions.Select(a => a.Id == assumption.Id
                    ? a with { AcknowledgedVersion = evidence.Version, ExpectedVersion = evidence.Version, ReviewAt = null } : a).ToArray(),
                Findings = current.Findings.Where(f => f.Key != "assumption:" + assumption.Id && f.Key != "assumption:unknown:" + assumption.Id).ToArray(),
                RunId = null, LeaseUntil = null, NextCheckAt = current.Enabled ? now : null },
                "reviewed", "Owner reviewed the changed assumption. The original decision and prediction are unchanged.", ct);
        }
        var next = command switch
        {
            _ when command.StartsWith("done:", StringComparison.Ordinal) && current.Steps.Any(s => s.Id == command[5..]) =>
                current with { Steps = current.Steps.Select(s => s.Id == command[5..] ? s with { Done = true } : s).ToArray(),
                    NextCheckAt = current.Enabled ? now : null, RunId = null, LeaseUntil = null },
            "accept" when current.Status == "suggested" => current with { Status = "open", Enabled = false },
            "pause" => current with { Status = "paused", Enabled = false, NextCheckAt = null, RunId = null, LeaseUntil = null },
            "resume" or "enable" when current.Status != "suggested" => current with { Status = "open", Enabled = true, NextCheckAt = now, RunId = null, LeaseUntil = null },
            "stop" => current with { Status = "stopped", Enabled = false, NextCheckAt = null, RunId = null, LeaseUntil = null },
            "dismiss" => current with { Status = "dismissed", Enabled = false, NextCheckAt = null, RunId = null, LeaseUntil = null },
            "resolve" when current.Status != "suggested" => current with { Status = "resolved", Enabled = false, NextCheckAt = null, RunId = null, LeaseUntil = null },
            _ => throw new ArgumentException("Choose accept, enable, pause, resume, stop, dismiss or resolve.")
        };
        return await SaveAsync(current, next, "lifecycle", "Owner chose " + command + ".", ct);
    }

    public async Task<ProactiveCase?> FeedbackAsync(Guid ownerId, Guid id, string signal, long revision, CancellationToken ct)
    {
        if (signal is not ("useful" or "not_useful" or "wrong" or "too_early" or "too_late" or "too_much_effort"))
            throw new ArgumentException("Choose useful, not_useful, wrong, too_early, too_late or too_much_effort.");
        var current = await repository.GetAsync(ownerId, id, ct);
        if (current is null) return null;
        RequireRevision(current, revision);
        var feedback = current.Feedback.Append(new ProactiveFeedback(Guid.CreateVersion7(), signal, clock.GetUtcNow(), current.ProfileId))
            .TakeLast(100).ToArray();
        // Learn only from three explicit matching answers in this case/profile. Silence is never feedback.
        var next = current with { Feedback = feedback };
        var count = feedback.Count(f => f.Signal == signal && f.At >= clock.GetUtcNow().AddDays(-30));
        var reason = "Recorded explicit feedback: " + signal + ".";
        if (count == 3 && signal is "not_useful" or "too_early")
        {
            next = next with { Policy = current.Policy with { CadenceMinutes = Math.Min(10080, current.Policy.CadenceMinutes * 2) } };
            reason += " Three answers support checking less often; permissions are unchanged.";
        }
        return await SaveAsync(current, next, next.Policy != current.Policy ? "preference" : "feedback", reason, ct);
    }

    public async Task<ProactiveCase?> UndoAsync(Guid ownerId, Guid id, Guid receiptId, long revision, CancellationToken ct)
    {
        var current = await repository.GetAsync(ownerId, id, ct);
        if (current is null) return null;
        RequireRevision(current, revision);
        var receipt = current.Receipts.FirstOrDefault(r => r.Id == receiptId && !r.Undone);
        if (receipt is null) throw new ArgumentException("This change is no longer available to undo.");
        if (receipt.Kind == "internal_plan")
        {
            var restored = current with { Revision = current.Revision + 1, UpdatedAt = clock.GetUtcNow(),
                Receipts = current.Receipts.Select(r => r.Id == receiptId ? r with { Undone = true } : r).ToArray() };
            if (!await repository.ApplyAsync(restored, current.Revision, new("planner.day", receipt.After, receipt.Before), ct))
                throw new ProactiveConflictException();
            return restored;
        }
        if (receipt.Kind != "preference") throw new ArgumentException("This history entry is not a reversible change.");
        // Compare the affected field rather than blindly restoring an entire document.
        if (ProactiveRules.Serialize(current.Policy) != receipt.After) throw new ProactiveConflictException();
        var previous = System.Text.Json.JsonSerializer.Deserialize<ProactivePolicy>(receipt.Before,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var next = current with { Policy = previous, Receipts = current.Receipts.Select(r => r.Id == receiptId ? r with { Undone = true } : r).ToArray() };
        return await SaveAsync(current, next, "undo", "Restored the previous check preference.", ct);
    }

    public async Task<ProactiveCase?> CheckAsync(Guid ownerId, Guid id, CancellationToken ct)
    {
        var current = await repository.GetAsync(ownerId, id, ct);
        if (current is null || current.Terminal || !current.Enabled || current.Status is "paused" or "suggested") return current;
        var now = clock.GetUtcNow();
        if (current.ExpiresAt <= now)
            return await SaveAsync(current, current with { Status = "stopped", Enabled = false, NextCheckAt = null,
                RunId = null, LeaseUntil = null, NextAction = "The responsibility reached its expiry and has stopped." },
                "expired", "Stopped at the owner-chosen expiry.", ct);
        if (current.NextCheckAt > now || current.LeaseUntil > now) return current;
        var autonomy = await settings.GetAsync<AutonomySettings>(ownerId, SettingsSections.Autonomy, ct) ?? AutonomySettings.Default;
        if (!autonomy.Enabled || autonomy.Level == AutonomyLevels.AskEverything || ProactiveRules.Quiet(current.Policy, now))
            return await DeferAsync(current, now.AddMinutes(15), ct);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(current.Policy.TimeZoneId)).DateTime);
        var checks = current.BudgetDay == day ? current.ChecksToday : 0;
        if (checks >= current.Policy.MaxChecksPerDay)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(current.Policy.TimeZoneId);
            return await DeferAsync(current, Workflows.LocalClock.Resolve(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone), ct);
        }
        var runId = Guid.CreateVersion7();
        var claimed = current with { Revision = current.Revision + 1, RunId = runId, LeaseUntil = now.AddMinutes(5), BudgetDay = day, ChecksToday = checks + 1 };
        if (!await repository.CompareExchangeAsync(claimed, current.Revision, ct)) return null;
        try
        {
            var context = await sources.ReadAsync(ownerId, current.ProfileId, current.SourceRefs, ct);
            var prepared = preparation is null ? new ProactivePreparation(context, null) : await preparation.PrepareAsync(current, context, ct);
            context = prepared.Sources;
            var result = ProactiveRules.Evaluate(current with { ClaimReviews = prepared.ClaimReviews ?? current.ClaimReviews }, context, now);
            var confirmed = await sources.ReadAsync(ownerId, current.ProfileId, current.SourceRefs, ct);
            var evidenceChanged = confirmed.Count != context.Count || context.Any(source =>
                confirmed.All(fresh => fresh.Ref != source.Ref || fresh.Version != (source.OriginVersion ?? source.Version)));
            // Pause/stop and changed sources invalidate an in-flight preparation.
            var latest = await repository.GetAsync(ownerId, id, ct);
            if (latest is null || latest.RunId != runId || latest.Revision != claimed.Revision || !latest.Enabled) return latest;
            if (evidenceChanged)
                return await SaveAsync(latest, latest with { RunId = null, LeaseUntil = null, NextCheckAt = now.AddMinutes(1),
                    PreparedReport = null, NextAction = "Supporting information changed during preparation. A fresh check is queued." },
                    "stale", "Discarded preparation because source versions or access changed before it could be saved.", ct);
            var next = latest with { Context = context, Findings = result.Findings, Steps = result.Steps,
                Status = latest.Status == "needs_input" ? "open" : latest.Status,
                NextAction = result.NextAction, LastCheckedAt = now, NextCheckAt = now.AddMinutes(current.Policy.CadenceMinutes),
                Failures = 0, FailureKind = null, RunId = null, LeaseUntil = null,
                PreparedReport = prepared.Report, TrialObservations = prepared.Observations ?? latest.TrialObservations, LastTrigger = null };
            next = next with { ClaimReviews = prepared.ClaimReviews ?? latest.ClaimReviews };
            if (prepared.Report is { } report && current.PreparedReport != report)
                next = next with { Reports = (current.Reports ?? []).Append(new ProactiveReportVersion(now,
                    report.Length <= 20000 ? report : report[..20000] + "\n[Report truncated; open the sources for the full evidence.]",
                    context.ToDictionary(s => s.Ref, s => s.Version))).TakeLast(5).ToArray() };
            if (current.RootRef is { } root && context.Any(s => s.Ref == root && ProactiveRules.CompleteStates.Contains(s.State)) &&
                (current.Kind == "project" && root.StartsWith("project:", StringComparison.Ordinal) ||
                 current.Kind == "follow_through" && root.StartsWith("commitment:", StringComparison.Ordinal)))
                next = next with { Status = "resolved", Enabled = false, NextCheckAt = null,
                    NextAction = "The linked record was explicitly completed. Ongoing checks have stopped." };
            if (current.Kind == "routine_trial" && now >= current.CreatedAt.AddDays(7))
                next = next with { Enabled = false, NextCheckAt = null, Status = "needs_review" };
            var changed = current.Context.Count != context.Count || context.Any(s => current.Context.All(old => old.Ref != s.Ref || old.Version != s.Version)) ||
                          ProactiveRules.Serialize(current.Findings) != ProactiveRules.Serialize(result.Findings) || current.NextAction != result.NextAction ||
                          current.PreparedReport != prepared.Report;
            if (prepared.Mutation is { } mutation)
            {
                if (autonomy.Level == AutonomyLevels.Standard)
                    return await SaveAsync(latest, next with { PreparedReport = "Proposed only: " + next.PreparedReport,
                        NextAction = "Review the proposed replan. Your autonomy setting requires approval for internal changes." },
                        "prepared", "Prepared a plan without changing it under the current autonomy policy.", ct);
                var receipt = new ProactiveReceipt(Guid.CreateVersion7(), "internal_plan", "Replanned unlocked internal focus blocks; calendar and pinned blocks are unchanged.",
                    mutation.ExpectedJson ?? "null", mutation.AfterJson, now, latest.Revision + 1,
                    Trigger: latest.LastTrigger ?? "scheduled_check", EvidenceVersions: context.ToDictionary(s => s.Ref, s => s.Version));
                next = next with { Revision = latest.Revision + 1, UpdatedAt = now,
                    Receipts = latest.Receipts.Append(receipt).TakeLast(200).ToArray() };
                if (!await repository.ApplyAsync(next, latest.Revision, mutation, ct)) throw new ProactiveConflictException();
                return next;
            }
            return await SaveAsync(latest, next, changed ? "prepared" : null,
                "Checked permitted sources and refreshed the next step. No external action was taken.", ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is not ProactiveConflictException)
        {
            var latest = await repository.GetAsync(ownerId, id, ct);
            if (latest is null || latest.RunId != runId) return latest;
            var kind = ProactiveRules.Failure(error);
            var attempts = latest.Failures + 1;
            var retry = kind == "transient" && attempts < 3;
            var request = error is ArgumentException argument
                ? "Needed: " + argument.Message[..Math.Min(argument.Message.Length, 600)]
                : kind == "permission" ? "Permission was denied. Review the existing access or approval decision; Jarvis will not retry it automatically."
                : "Review the unavailable capability or missing input before continuing.";
            return await SaveAsync(latest, latest with { RunId = null, LeaseUntil = null, Failures = attempts,
                FailureKind = kind, Status = retry ? "open" : "needs_input", NextCheckAt = retry ? now.AddMinutes(attempts * 5) : null,
                PreparedReport = null, NextAction = retry ? "A temporary failure will be retried." : request },
                "failed", "Check could not complete: " + kind + ". Previous evidence is retained as historical, not current.", ct);
        }
    }

    private async Task<ProactiveCase> SaveAsync(ProactiveCase before, ProactiveCase after, string? kind, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var receipts = after.Receipts;
        if (kind is not null)
        {
            var oldValue = kind == "preference" ? ProactiveRules.Serialize(before.Policy) : Projection(before);
            var newValue = kind == "preference" ? ProactiveRules.Serialize(after.Policy) : Projection(after);
            receipts = receipts.Append(new ProactiveReceipt(Guid.CreateVersion7(), kind, reason, oldValue, newValue, now, before.Revision + 1,
                Trigger: kind is "prepared" or "failed" ? before.LastTrigger ?? "scheduled_check" : kind == "expired" ? "expiry" : "owner",
                EvidenceVersions: (kind == "prepared" ? after : before).Context.ToDictionary(s => s.Ref, s => s.Version))).TakeLast(200).ToArray();
        }
        var next = after with { Revision = before.Revision + 1, UpdatedAt = now, Receipts = receipts };
        if (!await repository.CompareExchangeAsync(next, before.Revision, ct)) throw new ProactiveConflictException();
        return next;
    }

    private static void RequireRevision(ProactiveCase value, long revision)
    {
        if (value.Revision != revision) throw new ProactiveConflictException();
    }

    private static string Projection(ProactiveCase value) => ProactiveRules.Serialize(new {
        value.Status, value.Enabled, value.NextAction,
        steps = value.Steps.Select(s => new { s.Id, s.Done }),
        findings = value.Findings.Select(f => f.Key),
        reportVersion = value.PreparedReport is null ? null : ProactiveRules.Hash(value.PreparedReport)
    });

    private static ProactiveCaseDraft NormalizeReferences(ProactiveCaseDraft draft) => draft with {
        RootRef = draft.RootRef is null ? null : EntityRef.Parse(draft.RootRef).ToString(),
        Steps = draft.Steps?.Select(s => s with { SourceRef = s.SourceRef is null ? null : EntityRef.Parse(s.SourceRef).ToString() }).ToArray(),
        Assumptions = draft.Assumptions?.Select(a => a with { SourceRef = EntityRef.Parse(a.SourceRef).ToString(),
            TripRef = a.TripRef is null ? null : EntityRef.Parse(a.TripRef).ToString() }).ToArray()
    };

    private static void ValidateTripBindings(ProactiveCaseDraft draft, IReadOnlyList<ProactiveSource> visible)
    {
        foreach (var binding in (draft.Assumptions ?? []).Where(a => a.Effect is not null))
            if (visible.FirstOrDefault(s => s.Ref == binding.TripRef)?.TripItems?.Any(i => i.Id == binding.TripItemId && i.Verified) != true)
                throw new ArgumentException("The selected itinerary item is unavailable or unverified. Choose a verified current item from the linked trip.");
    }

    private static IReadOnlyList<ProactiveAssumption> InitialAssumptions(IReadOnlyList<ProactiveAssumption> assumptions, IReadOnlyList<ProactiveSource> visible) =>
        assumptions.Select(a => {
            var item = a.Effect is null ? null : visible.FirstOrDefault(s => s.Ref == a.TripRef)?.TripItems?.FirstOrDefault(i => i.Id == a.TripItemId);
            return a with { ExpectedVersion = a.ExpectedVersion ?? visible.FirstOrDefault(s => s.Ref == a.SourceRef)?.Version,
                BaselineDate = a.BaselineDate ?? item?.Date, BaselineStart = a.BaselineStart ?? item?.Start, BaselineEnd = a.BaselineEnd ?? item?.End };
        }).ToArray();

    private async Task<ProactiveCase> DeferAsync(ProactiveCase current, DateTimeOffset until, CancellationToken ct)
    {
        var next = current with { Revision = current.Revision + 1, NextCheckAt = until };
        return await repository.CompareExchangeAsync(next, current.Revision, ct) ? next : current;
    }
}
