using Jarvis.Application.Workflows;
using Jarvis.Domain.Inbox;

namespace Jarvis.Application.Inbox;

public sealed class CommitmentService(IInboxRepository repository, IReminderService reminders,
    IDailyBriefingRepository briefings, TimeProvider? timeProvider = null) : ICommitmentService
{
    public const int DueSoonDays = 7;
    private static readonly TimeOnly ReminderTime = new(9, 0);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zone = await ZoneAsync(ownerId, cancellationToken);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }

    public async Task<IReadOnlyList<Commitment>> ListAsync(Guid ownerId, string? direction, string? status,
        bool includeSuggested, CancellationToken cancellationToken)
    {
        var all = await repository.ListCommitmentsAsync(ownerId, cancellationToken);
        return all
            .Where(x => direction is null || x.Direction == direction)
            .Where(x => status is null || x.Status == status)
            .Where(x => includeSuggested || !x.Suggested)
            // Overdue and soonest first; undated ones last; newest first among equals.
            .OrderBy(x => x.Status == CommitmentStatuses.Open ? 0 : 1)
            .ThenBy(x => x.DueOn ?? DateOnly.MaxValue)
            .ThenByDescending(x => x.CreatedAt)
            .ToArray();
    }

    public Task<Commitment?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.GetCommitmentAsync(id, ownerId, cancellationToken);

    public async Task<InboxOperation<Commitment>> CreateAsync(Guid ownerId, CommitmentDraft draft, DateOnly today,
        CancellationToken cancellationToken)
    {
        if (Validate(draft.Direction, draft.Counterparty, draft.Description, draft.DueOn, today) is { } invalid)
            return InboxOperation<Commitment>.Invalid(invalid.Field, invalid.Message);
        if (!CommitmentSources.IsValid(draft.Source))
            return InboxOperation<Commitment>.Invalid("source", "Unknown commitment source.");

        var counterparty = InboxRules.Limit(draft.Counterparty, InboxRules.MaxCounterpartyLength) ?? "Someone";
        var description = InboxRules.Limit(draft.Description, InboxRules.MaxDescriptionLength)!;
        if (await FindDuplicateAsync(ownerId, draft.Direction!, counterparty, description, cancellationToken) is
            { } existing)
            return InboxOperation<Commitment>.Ok(existing);

        var now = clock.GetUtcNow();
        var commitment = new Commitment(Guid.CreateVersion7(), ownerId, draft.Direction!, counterparty, description,
            draft.DueOn, CommitmentStatuses.Open, false, draft.Source, draft.InboxThreadId, null, null, now, now);
        commitment = commitment with
        {
            ReminderId = await TryRemindAsync(commitment, today, cancellationToken)
        };
        await repository.AddCommitmentAsync(commitment, cancellationToken);
        return InboxOperation<Commitment>.Ok(commitment);
    }

    public async Task<IReadOnlyList<Commitment>> SuggestAsync(Guid ownerId, Guid? threadId, string source,
        IReadOnlyList<CommitmentSuggestion> suggestions, DateOnly today, CancellationToken cancellationToken)
    {
        var added = new List<Commitment>();
        foreach (var suggestion in suggestions.Take(InboxRules.MaxSuggestionsPerTriage))
        {
            if (Validate(suggestion.Direction, suggestion.Counterparty, suggestion.Description, suggestion.DueOn,
                    today) is not null)
                continue;
            var counterparty = InboxRules.Limit(suggestion.Counterparty, InboxRules.MaxCounterpartyLength) ?? "Someone";
            var description = InboxRules.Limit(suggestion.Description, InboxRules.MaxDescriptionLength)!;
            if (await FindDuplicateAsync(ownerId, suggestion.Direction, counterparty, description,
                    cancellationToken) is not null)
                continue;
            var now = clock.GetUtcNow();
            var commitment = new Commitment(Guid.CreateVersion7(), ownerId, suggestion.Direction, counterparty,
                description, suggestion.DueOn, CommitmentStatuses.Open, true,
                CommitmentSources.IsValid(source) ? source : CommitmentSources.Manual, threadId, null, null, now, now);
            await repository.AddCommitmentAsync(commitment, cancellationToken);
            added.Add(commitment);
        }
        return added;
    }

    public async Task<InboxOperation<Commitment>> AcceptAsync(Guid id, Guid ownerId, DateOnly today,
        CancellationToken cancellationToken)
    {
        var commitment = await repository.GetCommitmentAsync(id, ownerId, cancellationToken);
        if (commitment is null) return InboxOperation<Commitment>.NotFound();
        if (!commitment.Suggested) return InboxOperation<Commitment>.Ok(commitment);
        var accepted = commitment with
        {
            Suggested = false,
            UpdatedAt = clock.GetUtcNow()
        };
        accepted = accepted with { ReminderId = await TryRemindAsync(accepted, today, cancellationToken) };
        await repository.UpdateCommitmentAsync(accepted, cancellationToken);
        return InboxOperation<Commitment>.Ok(accepted);
    }

    public async Task<InboxOperation<Commitment>> SetStatusAsync(Guid id, Guid ownerId, string status,
        CancellationToken cancellationToken)
    {
        if (!CommitmentStatuses.IsValid(status))
            return InboxOperation<Commitment>.Invalid("status", "Use open, done, or dropped.");
        var commitment = await repository.GetCommitmentAsync(id, ownerId, cancellationToken);
        if (commitment is null) return InboxOperation<Commitment>.NotFound();
        if (commitment.Status == status) return InboxOperation<Commitment>.Ok(commitment);

        var now = clock.GetUtcNow();
        var updated = commitment with
        {
            Status = status,
            CompletedAt = status == CommitmentStatuses.Open ? null : now,
            UpdatedAt = now
        };
        if (status != CommitmentStatuses.Open)
        {
            await CancelReminderAsync(commitment, ownerId, cancellationToken);
            updated = updated with { ReminderId = null };
        }
        else if (!updated.Suggested)
        {
            // Reopened: bring the morning reminder back if the date is still ahead.
            updated = updated with
            {
                ReminderId = await TryRemindAsync(updated, await TodayAsync(ownerId, cancellationToken),
                    cancellationToken)
            };
        }
        await repository.UpdateCommitmentAsync(updated, cancellationToken);
        return InboxOperation<Commitment>.Ok(updated);
    }

    public async Task<InboxOperation<Commitment>> UpdateAsync(Guid id, Guid ownerId, string? description,
        DateOnly? dueOn, bool clearDue, DateOnly today, CancellationToken cancellationToken)
    {
        var commitment = await repository.GetCommitmentAsync(id, ownerId, cancellationToken);
        if (commitment is null) return InboxOperation<Commitment>.NotFound();
        var newDescription = description is null
            ? commitment.Description
            : InboxRules.Limit(description, InboxRules.MaxDescriptionLength);
        if (newDescription is null)
            return InboxOperation<Commitment>.Invalid("description", "Describe what was promised.");
        var newDue = clearDue ? null : dueOn ?? commitment.DueOn;
        if (Validate(commitment.Direction, commitment.Counterparty, newDescription, newDue, today) is { } invalid)
            return InboxOperation<Commitment>.Invalid(invalid.Field, invalid.Message);

        var updated = commitment with
        {
            Description = newDescription,
            DueOn = newDue,
            UpdatedAt = clock.GetUtcNow()
        };
        if (newDue != commitment.DueOn || newDescription != commitment.Description)
        {
            await CancelReminderAsync(commitment, ownerId, cancellationToken);
            updated = updated with
            {
                ReminderId = commitment.Status == CommitmentStatuses.Open && !commitment.Suggested
                    ? await TryRemindAsync(updated, today, cancellationToken)
                    : null
            };
        }
        await repository.UpdateCommitmentAsync(updated, cancellationToken);
        return InboxOperation<Commitment>.Ok(updated);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var commitment = await repository.GetCommitmentAsync(id, ownerId, cancellationToken);
        if (commitment is null) return false;
        await CancelReminderAsync(commitment, ownerId, cancellationToken);
        return await repository.DeleteCommitmentAsync(id, ownerId, cancellationToken);
    }

    public async Task<CommitmentSummary> SummarizeAsync(Guid ownerId, DateOnly today,
        CancellationToken cancellationToken)
    {
        var all = await repository.ListCommitmentsAsync(ownerId, cancellationToken);
        var open = all.Where(x => x is { Status: CommitmentStatuses.Open, Suggested: false }).ToArray();
        return new CommitmentSummary(
            open.Length,
            open.Count(x => x.DueOn is { } due && due < today),
            open.Count(x => x.DueOn is { } due && due >= today && due <= today.AddDays(DueSoonDays)),
            open.Count(x => x.Direction == CommitmentDirections.IOwe),
            open.Count(x => x.Direction == CommitmentDirections.OwedToMe),
            all.Count(x => x is { Suggested: true, Status: CommitmentStatuses.Open }));
    }

    internal static (string Field, string Message)? Validate(string? direction, string? counterparty,
        string? description, DateOnly? dueOn, DateOnly today)
    {
        if (!CommitmentDirections.IsValid(direction))
            return ("direction", "Use i_owe or owed_to_me.");
        if (InboxRules.Clean(description) is not { } text)
            return ("description", "Describe what was promised.");
        if (text.Length > InboxRules.MaxDescriptionLength)
            return ("description", $"Use at most {InboxRules.MaxDescriptionLength} characters.");
        if (InboxRules.Clean(counterparty) is { Length: > InboxRules.MaxCounterpartyLength })
            return ("counterparty", $"Use at most {InboxRules.MaxCounterpartyLength} characters.");
        if (dueOn is { } due && (due < today.AddYears(-1) || due > today.AddYears(5)))
            return ("dueOn", "Pick a date within the last year and the next five years.");
        return null;
    }

    private async Task<Commitment?> FindDuplicateAsync(Guid ownerId, string direction, string counterparty,
        string description, CancellationToken cancellationToken)
    {
        var key = InboxRules.Key(description);
        var who = InboxRules.Key(counterparty);
        return (await repository.ListCommitmentsAsync(ownerId, cancellationToken)).FirstOrDefault(x =>
            x.Status == CommitmentStatuses.Open && x.Direction == direction &&
            InboxRules.Key(x.Counterparty) == who && InboxRules.Key(x.Description) == key);
    }

    /// <summary>Creates the 09:00 reminder for a dated commitment; null when it has no date or it has passed.</summary>
    private async Task<Guid?> TryRemindAsync(Commitment commitment, DateOnly today,
        CancellationToken cancellationToken)
    {
        if (commitment.DueOn is not { } due || due < today) return null;
        try
        {
            var zone = await ZoneAsync(commitment.OwnerId, cancellationToken);
            var at = LocalClock.Resolve(due.ToDateTime(ReminderTime), zone);
            if (at <= clock.GetUtcNow().AddMinutes(1)) return null;
            var title = commitment.Direction == CommitmentDirections.IOwe
                ? $"Due today: {commitment.Description} (for {commitment.Counterparty})"
                : $"Follow up with {commitment.Counterparty}: {commitment.Description}";
            var reminder = await reminders.CreateAsync(commitment.OwnerId,
                new CreateReminderRequest(InboxRules.Limit(title, 280)!, at, TimeZoneId: zone.Id), cancellationToken);
            return reminder.Id;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // A reminder that cannot be scheduled must not stop the commitment from being saved.
            return null;
        }
    }

    private async Task CancelReminderAsync(Commitment commitment, Guid ownerId, CancellationToken cancellationToken)
    {
        if (commitment.ReminderId is not { } reminderId) return;
        try
        {
            await reminders.CancelAsync(reminderId, ownerId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The reminder may already be gone; the commitment change still goes ahead.
        }
    }

    private async Task<TimeZoneInfo> ZoneAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var zoneId = (await briefings.GetAsync(ownerId, cancellationToken))?.TimeZoneId;
        return LocalClock.TryFind(zoneId, out var zone) ? zone : TimeZoneInfo.Utc;
    }
}
