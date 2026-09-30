using Jarvis.Application.Journal;
using Jarvis.Domain.Journal;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class JournalEntryEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public DateOnly EntryDate { get; set; }
    public string Source { get; set; } = JournalSources.Written;
    public string Content { get; set; } = string.Empty;
    public string? Highlights { get; set; }
    public string? Gratitude { get; set; }
    public int? Rating { get; set; }
    public int? Mood { get; set; }
    public int? Energy { get; set; }
    public int? Stress { get; set; }
    public string[] Tags { get; set; } = [];
    public Guid? MemoryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public JournalEntry ToRecord() => new(Id, OwnerId, EntryDate, Source, Content, Highlights, Gratitude, Rating,
        Mood, Energy, Stress, Tags, MemoryId, CreatedAt, UpdatedAt);

    public static JournalEntryEntity From(JournalEntry entry) => new()
    {
        Id = entry.Id, OwnerId = entry.OwnerId, EntryDate = entry.EntryDate, Source = entry.Source,
        Content = entry.Content, Highlights = entry.Highlights, Gratitude = entry.Gratitude, Rating = entry.Rating,
        Mood = entry.Mood, Energy = entry.Energy, Stress = entry.Stress, Tags = entry.Tags.ToArray(),
        MemoryId = entry.MemoryId, CreatedAt = entry.CreatedAt, UpdatedAt = entry.UpdatedAt
    };
}

public sealed class JournalRepository(JarvisDbContext db) : IJournalRepository
{
    public async Task<JournalEntry> AddAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        db.JournalEntries.Add(JournalEntryEntity.From(entry));
        await db.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<JournalEntry?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.JournalEntries.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<JournalEntry?> UpdateAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        var entity = await db.JournalEntries
            .SingleOrDefaultAsync(x => x.Id == entry.Id && x.OwnerId == entry.OwnerId, cancellationToken);
        if (entity is null) return null;
        entity.EntryDate = entry.EntryDate;
        entity.Content = entry.Content;
        entity.Highlights = entry.Highlights;
        entity.Gratitude = entry.Gratitude;
        entity.Rating = entry.Rating;
        entity.Mood = entry.Mood;
        entity.Energy = entry.Energy;
        entity.Stress = entry.Stress;
        entity.Tags = entry.Tags.ToArray();
        entity.UpdatedAt = entry.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToRecord();
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.JournalEntries.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public Task SetMemoryIdAsync(Guid id, Guid ownerId, Guid? memoryId, CancellationToken cancellationToken) =>
        db.JournalEntries.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.MemoryId, memoryId), cancellationToken);

    public async Task<IReadOnlyList<JournalEntry>> ListAsync(Guid ownerId, DateOnly? from, DateOnly? to, int limit,
        CancellationToken cancellationToken) =>
        (await db.JournalEntries.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && (from == null || x.EntryDate >= from) &&
                        (to == null || x.EntryDate <= to))
            .OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.CreatedAt)
            .Take(limit).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();
}
