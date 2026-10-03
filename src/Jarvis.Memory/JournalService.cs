using Jarvis.Application.Automations;
using Jarvis.Application.Journal;
using Jarvis.Application.Memory;
using Jarvis.Domain.Journal;
using Microsoft.Extensions.Logging;

namespace Jarvis.Memory;

/// <summary>
/// Owns journal entries and mirrors each one into the memory store (kind <c>journal</c>) so that memory
/// search, agent context, and dreaming can reference what the user wrote or said about their day.
/// </summary>
public sealed class JournalService(IJournalRepository repository, IMemoryService memories,
    ILogger<JournalService> logger, TimeProvider? timeProvider = null,
    Jarvis.Application.Automations.IAutomationEventBus? events = null) : IJournalService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<JournalEntry> CreateAsync(Guid ownerId, JournalDraft draft, CancellationToken cancellationToken)
    {
        EnsureValid(draft);
        var now = clock.GetUtcNow();
        var entry = await repository.AddAsync(Build(Guid.CreateVersion7(), ownerId, draft, now, now, null), cancellationToken);
        var synced = await SyncMemoryAsync(entry, cancellationToken);
        await PublishAsync(synced, cancellationToken);
        return synced;
    }

    /// <summary>Tells automations a journal entry was saved; the detail lists the ratings, e.g. "mood 2/5".</summary>
    private Task PublishAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        var ratings = new List<string>();
        if (entry.Rating is { } rating) ratings.Add($"day {rating}/10");
        if (entry.Mood is { } mood) ratings.Add($"mood {mood}/5");
        if (entry.Energy is { } energy) ratings.Add($"energy {energy}/5");
        if (entry.Stress is { } stress) ratings.Add($"stress {stress}/5");
        return events.TryPublishAsync(entry.OwnerId, new Jarvis.Application.Automations.AutomationEvent(
            Jarvis.Application.Automations.AutomationEventKinds.JournalSaved, "Journal entry saved",
            string.Join(", ", ratings), entry.Source, entry.Id, entry.UpdatedAt), cancellationToken);
    }

    public async Task<(JournalEntry Entry, bool Merged)> MergeAsync(Guid ownerId, JournalDraft draft,
        CancellationToken cancellationToken)
    {
        EnsureValid(draft);
        var existing = (await repository.ListAsync(ownerId, draft.EntryDate, draft.EntryDate, 1, cancellationToken))
            .FirstOrDefault();
        if (existing is null) return (await CreateAsync(ownerId, draft, cancellationToken), false);

        var merged = new JournalDraft(
            existing.EntryDate,
            Join(existing.Content, draft.Content, JournalRules.MaxContentLength),
            Join(existing.Highlights, draft.Highlights, JournalRules.MaxSectionLength),
            Join(existing.Gratitude, draft.Gratitude, JournalRules.MaxSectionLength),
            draft.Rating ?? existing.Rating,
            draft.Mood ?? existing.Mood,
            draft.Energy ?? existing.Energy,
            draft.Stress ?? existing.Stress,
            JournalRules.NormalizeTags(existing.Tags.Concat(draft.Tags ?? [])).Take(JournalRules.MaxTags).ToArray(),
            existing.Source);
        var updated = await UpdateAsync(existing.Id, ownerId, merged, cancellationToken);
        return updated is null
            ? (await CreateAsync(ownerId, draft, cancellationToken), false)
            : (updated, true);
    }

    public async Task<JournalEntry?> UpdateAsync(Guid id, Guid ownerId, JournalDraft draft,
        CancellationToken cancellationToken)
    {
        EnsureValid(draft);
        var existing = await repository.GetAsync(id, ownerId, cancellationToken);
        if (existing is null) return null;
        var entry = Build(id, ownerId, draft, existing.CreatedAt, clock.GetUtcNow(), existing.MemoryId);
        var saved = await repository.UpdateAsync(entry, cancellationToken);
        if (saved is null) return null;
        var synced = await SyncMemoryAsync(saved, cancellationToken);
        await PublishAsync(synced, cancellationToken);
        return synced;
    }

    public Task<JournalEntry?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        repository.GetAsync(id, ownerId, cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(id, ownerId, cancellationToken);
        if (existing is null) return false;
        if (!await repository.DeleteAsync(id, ownerId, cancellationToken)) return false;
        if (existing.MemoryId is { } memoryId)
        {
            try
            {
                await memories.DeleteAsync(memoryId, ownerId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not delete the memory for journal entry {EntryId}.", id);
            }
        }
        return true;
    }

    public Task<IReadOnlyList<JournalEntry>> ListAsync(Guid ownerId, DateOnly? from, DateOnly? to, int limit,
        CancellationToken cancellationToken) =>
        repository.ListAsync(ownerId, from, to, Math.Clamp(limit, 1, JournalRules.MaxListLimit), cancellationToken);

    public async Task<JournalSummary> SummarizeAsync(Guid ownerId, int days, DateOnly today,
        CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 365);
        var from = today.AddDays(-(days - 1));
        var entries = await repository.ListAsync(ownerId, from, today, JournalRules.MaxListLimit * 5, cancellationToken);
        var series = entries.GroupBy(entry => entry.EntryDate).OrderBy(group => group.Key)
            .Select(group => new JournalDaySummary(group.Key, Average(group.Select(x => x.Rating)),
                Average(group.Select(x => x.Mood)), Average(group.Select(x => x.Energy)),
                Average(group.Select(x => x.Stress)), group.Count()))
            .ToArray();
        return new JournalSummary(days, entries.Count, Streak(series.Select(day => day.Date).ToHashSet(), today),
            Average(entries.Select(x => x.Rating)), Average(entries.Select(x => x.Mood)),
            Average(entries.Select(x => x.Energy)), Average(entries.Select(x => x.Stress)), series);
    }

    /// <summary>Consecutive days with an entry, ending today (or yesterday when today has none yet).</summary>
    public static int Streak(IReadOnlySet<DateOnly> dates, DateOnly today)
    {
        var day = dates.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (dates.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }

    private async Task<JournalEntry> SyncMemoryAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var content = JournalMemoryFormatter.Format(entry);
            var importance = JournalMemoryFormatter.Importance(entry);
            if (entry.MemoryId is { } memoryId &&
                await memories.GetAsync(memoryId, entry.OwnerId, cancellationToken) is { } existing)
            {
                await memories.UpdateAsync(existing.Id, entry.OwnerId, JournalMemoryFormatter.MemoryKind, content,
                    importance, 1f, existing.ValidUntil, existing.IsPinned, cancellationToken);
                return entry;
            }

            var created = await memories.CreateAsync(entry.OwnerId, JournalMemoryFormatter.MemoryKind, content,
                importance, 1f, null, false, cancellationToken, JournalMemoryFormatter.MemorySourceType, entry.Id);
            await repository.SetMemoryIdAsync(entry.Id, entry.OwnerId, created.Id, cancellationToken);
            return entry with { MemoryId = created.Id };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The entry is safe in the journal; the next edit retries the memory mirror.
            logger.LogWarning(exception, "Could not mirror journal entry {EntryId} into memory.", entry.Id);
            return entry;
        }
    }

    private void EnsureValid(JournalDraft draft)
    {
        var errors = JournalRules.Validate(draft, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors.Values.SelectMany(messages => messages)));
    }

    private static JournalEntry Build(Guid id, Guid ownerId, JournalDraft draft, DateTimeOffset createdAt,
        DateTimeOffset updatedAt, Guid? memoryId) =>
        new(id, ownerId, draft.EntryDate, draft.Source, JournalRules.Clean(draft.Content) ?? string.Empty,
            JournalRules.Clean(draft.Highlights), JournalRules.Clean(draft.Gratitude), draft.Rating, draft.Mood,
            draft.Energy, draft.Stress, JournalRules.NormalizeTags(draft.Tags), memoryId, createdAt, updatedAt);

    private static string? Join(string? first, string? second, int max)
    {
        var a = JournalRules.Clean(first);
        var b = JournalRules.Clean(second);
        if (a is null || b is null) return a ?? b;
        var joined = a + "\n\n" + b;
        return joined.Length <= max ? joined : joined[..max];
    }

    private static double? Average(IEnumerable<int?> values)
    {
        var present = values.Where(value => value is not null).Select(value => (double)value!.Value).ToArray();
        return present.Length == 0 ? null : Math.Round(present.Average(), 2);
    }
}
