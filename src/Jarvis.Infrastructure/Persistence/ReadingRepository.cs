using Jarvis.Application.Reading;
using Jarvis.Domain.Reading;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class ReadingItemEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Url { get; set; } = string.Empty;

    /// <summary><see cref="ReadingUrls.Key"/> of <see cref="Url"/>; unique per owner.</summary>
    public string UrlKey { get; set; } = string.Empty;

    public string Status { get; set; } = ReadingStatuses.Pending;
    public string? Title { get; set; }
    public string? SiteName { get; set; }
    public string? Excerpt { get; set; }
    public string? Summary { get; set; }
    public string[] KeyPoints { get; set; } = [];
    public int? WordCount { get; set; }
    public int? ReadingMinutes { get; set; }
    public string? Note { get; set; }
    public string Source { get; set; } = ReadingSources.App;
    public string? FailureReason { get; set; }
    public int FetchAttempts { get; set; }
    public DateTimeOffset? NextFetchAt { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ReadingItem ToRecord() => new(Id, OwnerId, Url, Status, Title, SiteName, Excerpt, Summary, KeyPoints,
        WordCount, ReadingMinutes, Note, Source, FailureReason, FetchAttempts, NextFetchAt, FetchedAt, ReadAt,
        CreatedAt, UpdatedAt);

    public void Apply(ReadingItem item)
    {
        Status = item.Status;
        Title = item.Title;
        SiteName = item.SiteName;
        Excerpt = item.Excerpt;
        Summary = item.Summary;
        KeyPoints = item.KeyPoints.ToArray();
        WordCount = item.WordCount;
        ReadingMinutes = item.ReadingMinutes;
        Note = item.Note;
        FailureReason = item.FailureReason;
        FetchAttempts = item.FetchAttempts;
        NextFetchAt = item.NextFetchAt;
        FetchedAt = item.FetchedAt;
        ReadAt = item.ReadAt;
        UpdatedAt = item.UpdatedAt;
    }
}

public sealed class ReadingRepository(JarvisDbContext db) : IReadingRepository
{
    public async Task<IReadOnlyList<ReadingItem>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.ReadingItems.AsNoTracking()
            .Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<ReadingItem?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.ReadingItems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<ReadingItem?> FindByKeyAsync(Guid ownerId, string urlKey, CancellationToken cancellationToken) =>
        (await db.ReadingItems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.UrlKey == urlKey, cancellationToken))?.ToRecord();

    public Task<int> CountAsync(Guid ownerId, CancellationToken cancellationToken) =>
        db.ReadingItems.CountAsync(x => x.OwnerId == ownerId, cancellationToken);

    public async Task AddAsync(ReadingItem item, string urlKey, CancellationToken cancellationToken)
    {
        var entity = new ReadingItemEntity
        {
            Id = item.Id, OwnerId = item.OwnerId, Url = item.Url, UrlKey = urlKey, Source = item.Source,
            CreatedAt = item.CreatedAt
        };
        entity.Apply(item);
        db.ReadingItems.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SaveAsync(ReadingItem item, CancellationToken cancellationToken)
    {
        var entity = await db.ReadingItems
            .SingleOrDefaultAsync(x => x.Id == item.Id && x.OwnerId == item.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(item);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.ReadingItems.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<ReadingItem>> ListDueAsync(DateTimeOffset now, int limit,
        CancellationToken cancellationToken) =>
        (await db.ReadingItems.AsNoTracking()
            .Where(x => x.Status == ReadingStatuses.Pending && (x.NextFetchAt == null || x.NextFetchAt <= now))
            .OrderBy(x => x.NextFetchAt)
            .Take(limit)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();
}
