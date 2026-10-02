using System.Globalization;
using System.Text;
using Jarvis.Domain.Reading;

namespace Jarvis.Application.Reading;

public sealed class ReadingListService(IReadingRepository items, TimeProvider? timeProvider = null)
    : IReadingListService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<ReadingItem>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        items.ListAsync(ownerId, cancellationToken);

    public Task<ReadingItem?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        items.GetAsync(id, ownerId, cancellationToken);

    public async Task<ReadingOperation<SaveReadingResult>> SaveAsync(Guid ownerId, string? url, string? note,
        string? source, CancellationToken cancellationToken)
    {
        string normalized;
        try { normalized = ReadingUrls.Normalize(url); }
        catch (ArgumentException exception)
        {
            return ReadingOperation<SaveReadingResult>.Invalid("url", exception.Message);
        }
        if (ReadingRules.ValidateNote(note) is { } noteError)
            return ReadingOperation<SaveReadingResult>.Invalid("note", noteError);

        var now = clock.GetUtcNow();
        var key = ReadingUrls.Key(normalized);
        var existing = await items.FindByKeyAsync(ownerId, key, cancellationToken);
        if (existing is not null)
        {
            var cleanNote = ReadingRules.CleanNote(note);
            if (!existing.IsRead && (cleanNote is null || cleanNote == existing.Note))
                return ReadingOperation<SaveReadingResult>.Ok(new SaveReadingResult(existing, true));
            var reopened = existing with { ReadAt = null, Note = cleanNote ?? existing.Note, UpdatedAt = now };
            return await items.SaveAsync(reopened, cancellationToken)
                ? ReadingOperation<SaveReadingResult>.Ok(new SaveReadingResult(reopened, true))
                : ReadingOperation<SaveReadingResult>.NotFound();
        }

        if (await items.CountAsync(ownerId, cancellationToken) >= ReadingRules.MaxItems)
            return ReadingOperation<SaveReadingResult>.Invalid("url",
                $"Your reading list can hold at most {ReadingRules.MaxItems} links. Remove a few read ones first.");

        var item = new ReadingItem(Guid.CreateVersion7(), ownerId, normalized, ReadingStatuses.Pending, null, null,
            null, null, [], null, null, ReadingRules.CleanNote(note), ReadingSources.Normalize(source), null, 0, now,
            null, null, now, now);
        await items.AddAsync(item, key, cancellationToken);
        return ReadingOperation<SaveReadingResult>.Ok(new SaveReadingResult(item, false));
    }

    public async Task<ReadingOperation<ReadingItem>> UpdateAsync(Guid id, Guid ownerId, bool? read, string? note,
        CancellationToken cancellationToken)
    {
        if (ReadingRules.ValidateNote(note) is { } noteError)
            return ReadingOperation<ReadingItem>.Invalid("note", noteError);
        var item = await items.GetAsync(id, ownerId, cancellationToken);
        if (item is null) return ReadingOperation<ReadingItem>.NotFound();
        var now = clock.GetUtcNow();
        var updated = item with { UpdatedAt = now };
        if (read is { } isRead && isRead != item.IsRead) updated = updated with { ReadAt = isRead ? now : null };
        if (note is not null) updated = updated with { Note = ReadingRules.CleanNote(note) };
        return await items.SaveAsync(updated, cancellationToken)
            ? ReadingOperation<ReadingItem>.Ok(updated)
            : ReadingOperation<ReadingItem>.NotFound();
    }

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        items.DeleteAsync(id, ownerId, cancellationToken);

    public async Task<ReadingOperation<ReadingItem>> RefreshAsync(Guid id, Guid ownerId,
        CancellationToken cancellationToken)
    {
        var item = await items.GetAsync(id, ownerId, cancellationToken);
        if (item is null) return ReadingOperation<ReadingItem>.NotFound();
        var now = clock.GetUtcNow();
        var queued = item with
        {
            Status = ReadingStatuses.Pending, FailureReason = null, FetchAttempts = 0, NextFetchAt = now,
            UpdatedAt = now
        };
        return await items.SaveAsync(queued, cancellationToken)
            ? ReadingOperation<ReadingItem>.Ok(queued)
            : ReadingOperation<ReadingItem>.NotFound();
    }

    public async Task<IReadOnlyList<ReadingItem>> FindAsync(Guid ownerId, string query,
        CancellationToken cancellationToken) =>
        Find(await items.ListAsync(ownerId, cancellationToken), query);

    /// <summary>
    /// Items whose link equals <paramref name="query"/>, otherwise items whose title or site contains every word of
    /// it, ignoring case and accents.
    /// </summary>
    public static IReadOnlyList<ReadingItem> Find(IReadOnlyList<ReadingItem> all, string query)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return [];
        try
        {
            var key = ReadingUrls.Key(ReadingUrls.Normalize(trimmed));
            var byUrl = all.Where(item => ReadingUrls.Key(item.Url) == key).ToArray();
            if (byUrl.Length > 0) return byUrl;
        }
        catch (ArgumentException)
        {
            // Not a link; match on words below.
        }
        var words = Words(trimmed);
        if (words.Count == 0) return [];
        return all.Where(item =>
        {
            var haystack = " " + string.Join(' ', Words($"{item.Title} {item.SiteName} {item.Url}")) + " ";
            return words.All(word => haystack.Contains(word, StringComparison.Ordinal));
        }).ToArray();
    }

    private static List<string> Words(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
