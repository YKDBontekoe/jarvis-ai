using Jarvis.Application.Timeline;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Decisions;

namespace Jarvis.Application.Decisions;

public sealed class DecisionService(
    IDecisionRepository repository,
    IReminderService reminders,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : IDecisionService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<DecisionView> CreateAsync(Guid ownerId, CreateDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var zone = await ZoneAsync(ownerId, cancellationToken);
        var now = clock.GetUtcNow();
        var today = TimelineRules.LocalDate(now, zone);
        var title = Required(request.Title, DecisionRules.MaxTitleLength, "Title");
        var prediction = Required(request.Prediction, DecisionRules.MaxPredictionLength, "Prediction");
        var context = Optional(request.Context, DecisionRules.MaxContextLength, "Context");
        ValidateProbability(request.Probability);
        ValidateReviewDate(request.ReviewOn, today);
        if (await repository.CountUnresolvedAsync(ownerId, cancellationToken) >= DecisionRules.MaxUnresolved)
            throw new ArgumentException(
                $"You already have {DecisionRules.MaxUnresolved} open decisions. Resolve or delete some first.");

        var decision = new Decision(Guid.CreateVersion7(), ownerId, title, context, prediction,
            Math.Round(request.Probability, 2), request.ReviewOn, null, null, null, null, now, now);
        decision = decision with { ReminderId = await ScheduleReminderAsync(decision, zone, now, cancellationToken) };
        await repository.AddAsync(decision, cancellationToken);
        return ToView(decision, today);
    }

    public async Task<DecisionView?> UpdateAsync(Guid id, Guid ownerId, UpdateDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return null;
        if (current.IsResolved) throw new ArgumentException("A resolved decision can no longer be changed.");

        var zone = await ZoneAsync(ownerId, cancellationToken);
        var now = clock.GetUtcNow();
        var today = TimelineRules.LocalDate(now, zone);
        var updated = current with
        {
            Title = request.Title is null ? current.Title
                : Required(request.Title, DecisionRules.MaxTitleLength, "Title"),
            Context = request.Context is null ? current.Context
                : Optional(request.Context, DecisionRules.MaxContextLength, "Context"),
            Prediction = request.Prediction is null ? current.Prediction
                : Required(request.Prediction, DecisionRules.MaxPredictionLength, "Prediction"),
            UpdatedAt = now
        };
        if (request.Probability is { } probability)
        {
            ValidateProbability(probability);
            updated = updated with { Probability = Math.Round(probability, 2) };
        }

        var dateChanged = request.ReviewOn is { } reviewOn && reviewOn != current.ReviewOn;
        if (dateChanged)
        {
            ValidateReviewDate(request.ReviewOn!.Value, today);
            updated = updated with { ReviewOn = request.ReviewOn.Value };
        }

        var titleChanged = updated.Title != current.Title;
        if (dateChanged || titleChanged)
        {
            await CancelReminderAsync(current.ReminderId, ownerId, cancellationToken);
            updated = updated with { ReminderId = await ScheduleReminderAsync(updated, zone, now, cancellationToken) };
        }

        await repository.UpdateAsync(updated, cancellationToken);
        return ToView(updated, today);
    }

    public async Task<DecisionView?> ResolveAsync(Guid id, Guid ownerId, bool outcome, string? note,
        CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return null;
        var cleanNote = Optional(note, DecisionRules.MaxNoteLength, "Note");
        var zone = await ZoneAsync(ownerId, cancellationToken);
        var now = clock.GetUtcNow();

        // Answering early (or late) makes the pending reminder pointless.
        if (!current.IsResolved) await CancelReminderAsync(current.ReminderId, ownerId, cancellationToken);
        var resolved = current with
        {
            Outcome = outcome, OutcomeNote = cleanNote, ResolvedAt = now, ReminderId = null, UpdatedAt = now
        };
        await repository.UpdateAsync(resolved, cancellationToken);
        return ToView(resolved, TimelineRules.LocalDate(now, zone));
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, ownerId, cancellationToken);
        if (current is null) return false;
        await CancelReminderAsync(current.ReminderId, ownerId, cancellationToken);
        return await repository.DeleteAsync(id, ownerId, cancellationToken);
    }

    public async Task<DecisionView?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var decision = await repository.GetAsync(id, ownerId, cancellationToken);
        if (decision is null) return null;
        var zone = await ZoneAsync(ownerId, cancellationToken);
        return ToView(decision, TimelineRules.LocalDate(clock.GetUtcNow(), zone));
    }

    public async Task<IReadOnlyList<DecisionView>> ListAsync(Guid ownerId, string? status, int limit,
        CancellationToken cancellationToken)
    {
        var wanted = status?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(wanted) && !DecisionStatuses.All.Contains(wanted))
            throw new ArgumentException("Status must be open, due or resolved.");

        var zone = await ZoneAsync(ownerId, cancellationToken);
        var today = TimelineRules.LocalDate(clock.GetUtcNow(), zone);
        var views = (await repository.ListAsync(ownerId, cancellationToken)).Select(x => ToView(x, today));
        if (!string.IsNullOrEmpty(wanted)) views = views.Where(x => x.Status == wanted);

        // Due and open decisions read best soonest-first; resolved ones newest-first.
        var ordered = wanted == DecisionStatuses.Resolved
            ? views.OrderByDescending(x => x.ResolvedAt)
            : views.OrderBy(x => x.Status == DecisionStatuses.Resolved).ThenBy(x => x.ReviewOn)
                .ThenBy(x => x.CreatedAt);
        return ordered.Take(Math.Clamp(limit, 1, DecisionRules.MaxLimit)).ToArray();
    }

    public async Task<CalibrationReport> CalibrationAsync(Guid ownerId, CancellationToken cancellationToken) =>
        CalibrationCalculator.Report((await repository.ListAsync(ownerId, cancellationToken))
            .Where(x => x is { Outcome: not null, ResolvedAt: not null })
            .Select(x => new ResolvedPrediction(x.Probability, x.Outcome!.Value, x.ResolvedAt!.Value)).ToArray());

    private async Task<Guid?> ScheduleReminderAsync(Decision decision, TimeZoneInfo zone, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The reminder is the nudge, not the data: a decision is still worth keeping when it cannot be created.
        try
        {
            var due = LocalClock.Resolve(decision.ReviewOn.ToDateTime(DecisionRules.ReminderTime), zone);
            if (due <= now.AddMinutes(1)) due = now.AddMinutes(15);
            var reminder = await reminders.CreateAsync(decision.OwnerId,
                new CreateReminderRequest(Truncate("Check outcome: " + decision.Title, 300), due,
                    TimeZoneId: zone.Id), cancellationToken);
            return reminder.Id;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task CancelReminderAsync(Guid? reminderId, Guid ownerId, CancellationToken cancellationToken)
    {
        if (reminderId is not { } id) return;
        try
        {
            await reminders.CancelAsync(id, ownerId, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Already fired or gone; nothing left to cancel.
        }
    }

    private async Task<TimeZoneInfo> ZoneAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        LocalClock.TryFind(zoneId, out var zone);
        return zone;
    }

    private static DecisionView ToView(Decision d, DateOnly today) => new(d.Id, d.Title, d.Context, d.Prediction,
        d.Probability, d.ReviewOn, DecisionRules.StatusOf(d, today), d.Outcome, d.OutcomeNote, d.ResolvedAt,
        d.CreatedAt);

    private static void ValidateProbability(double probability)
    {
        if (!double.IsFinite(probability) || probability < DecisionRules.MinProbability ||
            probability > DecisionRules.MaxProbability)
            throw new ArgumentException("Probability must be between 1% and 99%.");
    }

    private static void ValidateReviewDate(DateOnly reviewOn, DateOnly today)
    {
        if (reviewOn < today) throw new ArgumentException("The review date cannot be in the past.");
        if (reviewOn > today.AddDays(DecisionRules.MaxHorizonDays))
            throw new ArgumentException("The review date must be within the next two years.");
    }

    private static string Required(string? value, int max, string name)
    {
        var text = TimelineRules.Shorten(value, int.MaxValue);
        if (text.Length == 0) throw new ArgumentException($"{name} is required.");
        if (text.Length > max) throw new ArgumentException($"{name} must be at most {max} characters.");
        return text;
    }

    /// <summary>Longer text keeps its line breaks; only the ends are trimmed. Empty becomes null.</summary>
    private static string? Optional(string? value, int max, string name)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > max) throw new ArgumentException($"{name} must be at most {max} characters.");
        return text;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
