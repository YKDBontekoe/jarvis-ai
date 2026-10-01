using Jarvis.Application.Lists;
using Jarvis.Domain.Lists;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class PersonalListEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary><see cref="ListRules.NameKey"/> of <see cref="Name"/>; unique per owner.</summary>
    public string NameKey { get; set; } = string.Empty;

    public string Kind { get; set; } = ListKinds.General;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ListItemEntity> Items { get; set; } = [];

    public PersonalList ToRecord() => new(Id, OwnerId, Name, Kind,
        Items.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => x.ToRecord()).ToArray(), CreatedAt, UpdatedAt);
}

public sealed class ListItemEntity
{
    public Guid Id { get; set; }
    public Guid ListId { get; set; }
    public Guid OwnerId { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ListItem ToRecord() => new(Id, ListId, OwnerId, Text, IsDone, DoneAt, CreatedAt, UpdatedAt);

    public static ListItemEntity From(ListItem item) => new()
    {
        Id = item.Id, ListId = item.ListId, OwnerId = item.OwnerId, Text = item.Text, IsDone = item.IsDone,
        DoneAt = item.DoneAt, CreatedAt = item.CreatedAt, UpdatedAt = item.UpdatedAt
    };
}

public sealed class ListRepository(JarvisDbContext db) : IListRepository
{
    public async Task<IReadOnlyList<PersonalList>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await db.PersonalLists.AsNoTracking().AsSplitQuery().Include(x => x.Items)
            .Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToArray();

    public async Task<PersonalList?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await db.PersonalLists.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken))?.ToRecord();

    public async Task AddListAsync(PersonalList list, CancellationToken cancellationToken)
    {
        db.PersonalLists.Add(new PersonalListEntity
        {
            Id = list.Id, OwnerId = list.OwnerId, Name = list.Name, NameKey = ListRules.NameKey(list.Name),
            Kind = list.Kind, CreatedAt = list.CreatedAt, UpdatedAt = list.UpdatedAt,
            Items = list.Items.Select(ListItemEntity.From).ToList()
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateListAsync(Guid id, Guid ownerId, string name, string kind,
        DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
        await db.PersonalLists.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Name, name)
                .SetProperty(x => x.NameKey, ListRules.NameKey(name))
                .SetProperty(x => x.Kind, kind)
                .SetProperty(x => x.UpdatedAt, updatedAt), cancellationToken) > 0;

    public async Task<bool> DeleteListAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await db.PersonalLists.Where(x => x.Id == id && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<bool> SaveItemsAsync(Guid ownerId, Guid listId, IReadOnlyList<ListItem> added,
        IReadOnlyList<ListItem> changed, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var list = await db.PersonalLists
            .SingleOrDefaultAsync(x => x.Id == listId && x.OwnerId == ownerId, cancellationToken);
        if (list is null) return false;
        var changedIds = changed.Select(x => x.Id).ToArray();
        var existing = await db.ListItems
            .Where(x => x.ListId == listId && x.OwnerId == ownerId && changedIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (existing.Count != changedIds.Length) return false;
        foreach (var item in changed)
        {
            var entity = existing[item.Id];
            entity.Text = item.Text;
            entity.IsDone = item.IsDone;
            entity.DoneAt = item.DoneAt;
            entity.UpdatedAt = item.UpdatedAt;
        }
        db.ListItems.AddRange(added.Select(item => ListItemEntity.From(item with { ListId = listId, OwnerId = ownerId })));
        list.UpdatedAt = updatedAt;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> DeleteItemsAsync(Guid ownerId, Guid listId, IReadOnlyCollection<Guid> itemIds,
        DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var ids = itemIds.ToArray();
        var deleted = await db.ListItems
            .Where(x => x.ListId == listId && x.OwnerId == ownerId && ids.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
            await db.PersonalLists.Where(x => x.Id == listId && x.OwnerId == ownerId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAt, updatedAt), cancellationToken);
        return deleted;
    }
}
