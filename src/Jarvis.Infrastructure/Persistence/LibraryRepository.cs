using Jarvis.Application.Library;
using Jarvis.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class LibraryItemEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Kind { get; set; } = LibraryKinds.Note;
    public string? Url { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string[] KeyPoints { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public string Content { get; set; } = string.Empty;
    public string Origin { get; set; } = LibraryOrigins.App;
    public Guid? ProjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public LibraryItem ToRecord(bool withContent = true) => new(Id, OwnerId, Kind, Url, Title, Summary, KeyPoints, Tags,
        withContent ? Content : string.Empty, Origin, ProjectId, CreatedAt, UpdatedAt);

    public void Apply(LibraryItem item)
    {
        Kind = item.Kind;
        Url = item.Url;
        Title = item.Title;
        Summary = item.Summary;
        KeyPoints = item.KeyPoints.ToArray();
        Tags = item.Tags.ToArray();
        Content = item.Content;
        Origin = item.Origin;
        ProjectId = item.ProjectId;
        UpdatedAt = item.UpdatedAt;
    }
}

public sealed class FlashcardEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? ItemId { get; set; }
    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;
    public double Ease { get; set; } = Sm2.StartEase;
    public int IntervalDays { get; set; }
    public int Repetitions { get; set; }
    public int Lapses { get; set; }
    public DateOnly DueOn { get; set; }
    public DateOnly? LastReviewedOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Flashcard ToRecord() => new(Id, OwnerId, ItemId, Front, Back, Ease, IntervalDays, Repetitions, Lapses, DueOn,
        LastReviewedOn, CreatedAt);

    public void Apply(Flashcard card)
    {
        Front = card.Front;
        Back = card.Back;
        Ease = card.Ease;
        IntervalDays = card.IntervalDays;
        Repetitions = card.Repetitions;
        Lapses = card.Lapses;
        DueOn = card.DueOn;
        LastReviewedOn = card.LastReviewedOn;
    }
}

public sealed class LibraryRepository(JarvisDbContext db) : ILibraryRepository
{
    public async Task<IReadOnlyList<LibraryItem>> ListAsync(Guid ownerId, LibraryQuery query,
        CancellationToken cancellationToken)
    {
        var items = db.LibraryItems.AsNoTracking().Where(x => x.OwnerId == ownerId);
        if (query.Kind is not null) items = items.Where(x => x.Kind == query.Kind);
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim().TrimStart('#').ToLowerInvariant();
            items = items.Where(x => x.Tags.Contains(tag));
        }
        // Every word must appear somewhere: title, summary, tags or the text itself.
        foreach (var term in (query.Text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                     .Take(6))
        {
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            items = items.Where(x => EF.Functions.ILike(x.Title, pattern) || EF.Functions.ILike(x.Summary, pattern) ||
                                     EF.Functions.ILike(x.Content, pattern) ||
                                     x.Tags.Contains(term.ToLowerInvariant()));
        }
        var rows = await items.OrderByDescending(x => x.CreatedAt).Take(query.Limit)
            .Select(x => new LibraryItemEntity
            {
                Id = x.Id, OwnerId = x.OwnerId, Kind = x.Kind, Url = x.Url, Title = x.Title, Summary = x.Summary,
                KeyPoints = x.KeyPoints, Tags = x.Tags, Origin = x.Origin, ProjectId = x.ProjectId,
                CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt
            }).ToListAsync(cancellationToken);
        return rows.Select(x => x.ToRecord(withContent: false)).ToArray();
    }

    public async Task<IReadOnlyList<LibraryItem>> ListSinceAsync(Guid ownerId, DateTimeOffset since, int limit,
        CancellationToken cancellationToken)
    {
        var rows = await db.LibraryItems.AsNoTracking().Where(x => x.OwnerId == ownerId && x.CreatedAt >= since)
            .OrderByDescending(x => x.CreatedAt).Take(limit)
            .Select(x => new LibraryItemEntity
            {
                Id = x.Id, OwnerId = x.OwnerId, Kind = x.Kind, Url = x.Url, Title = x.Title, Summary = x.Summary,
                KeyPoints = x.KeyPoints, Tags = x.Tags, Origin = x.Origin, ProjectId = x.ProjectId,
                CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt
            }).ToListAsync(cancellationToken);
        return rows.Select(x => x.ToRecord(withContent: false)).ToArray();
    }

    public async Task<LibraryItem?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.LibraryItems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task<LibraryItem?> FindByUrlAsync(Guid ownerId, string normalizedUrl,
        CancellationToken cancellationToken) =>
        (await db.LibraryItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.Url == normalizedUrl, cancellationToken))?.ToRecord();

    public async Task AddAsync(LibraryItem item, CancellationToken cancellationToken)
    {
        var entity = new LibraryItemEntity { Id = item.Id, OwnerId = item.OwnerId, CreatedAt = item.CreatedAt };
        entity.Apply(item);
        db.LibraryItems.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(LibraryItem item, CancellationToken cancellationToken)
    {
        var entity = await db.LibraryItems
            .SingleOrDefaultAsync(x => x.Id == item.Id && x.OwnerId == item.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(item);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        await db.Flashcards.Where(x => x.ItemId == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
        return await db.LibraryItems.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<Flashcard>> ListCardsAsync(Guid ownerId, Guid? itemId,
        CancellationToken cancellationToken) =>
        (await db.Flashcards.AsNoTracking().Where(x => x.OwnerId == ownerId && (itemId == null || x.ItemId == itemId))
            .OrderBy(x => x.DueOn).Take(5_000).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<IReadOnlyList<Flashcard>> ListDueCardsAsync(Guid ownerId, DateOnly today, int limit,
        CancellationToken cancellationToken) =>
        (await db.Flashcards.AsNoTracking().Where(x => x.OwnerId == ownerId && x.DueOn <= today)
            .OrderBy(x => x.DueOn).ThenBy(x => x.CreatedAt).Take(limit).ToListAsync(cancellationToken))
        .Select(x => x.ToRecord()).ToArray();

    public async Task<Flashcard?> GetCardAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.Flashcards.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddCardsAsync(IReadOnlyList<Flashcard> cards, CancellationToken cancellationToken)
    {
        foreach (var card in cards)
        {
            var entity = new FlashcardEntity
            {
                Id = card.Id, OwnerId = card.OwnerId, ItemId = card.ItemId, CreatedAt = card.CreatedAt
            };
            entity.Apply(card);
            db.Flashcards.Add(entity);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateCardAsync(Flashcard card, CancellationToken cancellationToken)
    {
        var entity = await db.Flashcards
            .SingleOrDefaultAsync(x => x.Id == card.Id && x.OwnerId == card.OwnerId, cancellationToken);
        if (entity is null) return false;
        entity.Apply(card);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteCardAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.Flashcards.Where(x => x.Id == id && x.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken) > 0;

    public Task<int> CountCardsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        db.Flashcards.CountAsync(x => x.OwnerId == ownerId, cancellationToken);
}
