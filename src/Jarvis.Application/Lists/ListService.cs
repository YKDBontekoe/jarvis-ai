using Jarvis.Domain.Lists;

namespace Jarvis.Application.Lists;

public sealed class ListService(IListRepository lists, TimeProvider? timeProvider = null) : IListService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<PersonalList>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        lists.ListAsync(ownerId, cancellationToken);

    public Task<PersonalList?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        lists.GetAsync(id, ownerId, cancellationToken);

    public async Task<PersonalList?> FindAsync(Guid ownerId, string name, CancellationToken cancellationToken) =>
        Find(await lists.ListAsync(ownerId, cancellationToken), name);

    public async Task<ListOperation<PersonalList>> CreateAsync(Guid ownerId, string? name, string? kind,
        CancellationToken cancellationToken)
    {
        if (ListRules.ValidateName(name) is { } nameError) return ListOperation<PersonalList>.Invalid("name", nameError);
        if (kind is not null && !ListKinds.IsValid(kind))
            return ListOperation<PersonalList>.Invalid("kind", "Unknown list kind.");
        var cleanName = ListRules.Clean(name)!;
        var existing = await lists.ListAsync(ownerId, cancellationToken);
        if (existing.Count >= ListRules.MaxLists)
            return ListOperation<PersonalList>.Invalid("name", $"You can keep at most {ListRules.MaxLists} lists.");
        if (SameName(existing, cleanName, null) is not null)
            return ListOperation<PersonalList>.Conflict("name", "You already have a list with that name.");

        var now = clock.GetUtcNow();
        var list = new PersonalList(Guid.CreateVersion7(), ownerId, cleanName, kind ?? ListKinds.Guess(cleanName), [],
            now, now);
        await lists.AddListAsync(list, cancellationToken);
        return ListOperation<PersonalList>.Ok(list);
    }

    public async Task<ListOperation<PersonalList>> UpdateAsync(Guid id, Guid ownerId, string? name, string? kind,
        CancellationToken cancellationToken)
    {
        if (ListRules.ValidateName(name) is { } nameError) return ListOperation<PersonalList>.Invalid("name", nameError);
        if (kind is not null && !ListKinds.IsValid(kind))
            return ListOperation<PersonalList>.Invalid("kind", "Unknown list kind.");
        var all = await lists.ListAsync(ownerId, cancellationToken);
        var list = all.FirstOrDefault(x => x.Id == id);
        if (list is null) return ListOperation<PersonalList>.NotFound();
        var cleanName = ListRules.Clean(name)!;
        if (SameName(all, cleanName, id) is not null)
            return ListOperation<PersonalList>.Conflict("name", "You already have a list with that name.");

        var now = clock.GetUtcNow();
        var updated = list with { Name = cleanName, Kind = kind ?? list.Kind, UpdatedAt = now };
        return await lists.UpdateListAsync(id, ownerId, updated.Name, updated.Kind, now, cancellationToken)
            ? ListOperation<PersonalList>.Ok(updated)
            : ListOperation<PersonalList>.NotFound();
    }

    public Task<bool> DeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        lists.DeleteListAsync(id, ownerId, cancellationToken);

    public async Task<ListOperation<AddItemsResult>> AddItemsAsync(Guid ownerId, Guid listId,
        IEnumerable<string?> texts, CancellationToken cancellationToken)
    {
        var list = await lists.GetAsync(listId, ownerId, cancellationToken);
        return list is null
            ? ListOperation<AddItemsResult>.NotFound()
            : await AddAsync(list, false, texts, cancellationToken);
    }

    public async Task<ListOperation<AddItemsResult>> AddItemsByNameAsync(Guid ownerId, string listName,
        IEnumerable<string?> texts, CancellationToken cancellationToken)
    {
        var list = await FindAsync(ownerId, listName, cancellationToken);
        var created = false;
        if (list is null)
        {
            var creation = await CreateAsync(ownerId, listName, null, cancellationToken);
            if (!creation.Succeeded)
                return new ListOperation<AddItemsResult>(null, creation.Failure, creation.Field, creation.Message);
            list = creation.Value!;
            created = true;
        }
        return await AddAsync(list, created, texts, cancellationToken);
    }

    public async Task<ListOperation<ListItem>> UpdateItemAsync(Guid ownerId, Guid listId, Guid itemId, string? text,
        bool? done, CancellationToken cancellationToken)
    {
        if (text is not null && ListRules.ValidateItem(text) is { } textError)
            return ListOperation<ListItem>.Invalid("text", textError);
        var list = await lists.GetAsync(listId, ownerId, cancellationToken);
        var item = list?.Items.FirstOrDefault(x => x.Id == itemId);
        if (item is null) return ListOperation<ListItem>.NotFound();

        var now = clock.GetUtcNow();
        var updated = item with { Text = text is null ? item.Text : ListRules.Clean(text)!, UpdatedAt = now };
        if (done is { } isDone && isDone != item.IsDone)
            updated = updated with { IsDone = isDone, DoneAt = isDone ? now : null };
        return await lists.SaveItemsAsync(ownerId, listId, [], [updated], now, cancellationToken)
            ? ListOperation<ListItem>.Ok(updated)
            : ListOperation<ListItem>.NotFound();
    }

    public async Task<bool> DeleteItemAsync(Guid ownerId, Guid listId, Guid itemId,
        CancellationToken cancellationToken) =>
        await lists.DeleteItemsAsync(ownerId, listId, [itemId], clock.GetUtcNow(), cancellationToken) > 0;

    public async Task<int?> ClearDoneAsync(Guid ownerId, Guid listId, CancellationToken cancellationToken)
    {
        var list = await lists.GetAsync(listId, ownerId, cancellationToken);
        if (list is null) return null;
        var done = list.Items.Where(x => x.IsDone).Select(x => x.Id).ToArray();
        return done.Length == 0
            ? 0
            : await lists.DeleteItemsAsync(ownerId, listId, done, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<ListOperation<MatchItemsResult>> SetDoneByTextAsync(Guid ownerId, Guid listId,
        IEnumerable<string?> texts, bool done, CancellationToken cancellationToken)
    {
        var list = await lists.GetAsync(listId, ownerId, cancellationToken);
        if (list is null) return ListOperation<MatchItemsResult>.NotFound();
        var now = clock.GetUtcNow();
        var changed = new Dictionary<Guid, ListItem>();
        var unchanged = new List<string>();
        var notFound = new List<string>();
        var ambiguous = new List<AmbiguousItem>();
        foreach (var text in CleanTexts(texts))
        {
            // Look among items that still need the change first, then among items already in that state.
            var pending = list.Items.Where(x => x.IsDone != done && !changed.ContainsKey(x.Id)).ToArray();
            var match = Match(pending, text);
            if (match.Count == 1)
            {
                changed[match[0].Id] = match[0] with { IsDone = done, DoneAt = done ? now : null, UpdatedAt = now };
                continue;
            }
            if (match.Count > 1)
            {
                ambiguous.Add(new AmbiguousItem(text, match.Select(x => x.Text).ToArray()));
                continue;
            }
            if (Match(list.Items.Where(x => x.IsDone == done).ToArray(), text).Count > 0) unchanged.Add(text);
            else notFound.Add(text);
        }

        if (changed.Count > 0)
            await lists.SaveItemsAsync(ownerId, listId, [], changed.Values.ToArray(), now, cancellationToken);
        var items = list.Items.Select(x => changed.GetValueOrDefault(x.Id, x)).ToArray();
        return ListOperation<MatchItemsResult>.Ok(new MatchItemsResult(
            list with { Items = items, UpdatedAt = changed.Count > 0 ? now : list.UpdatedAt },
            changed.Values.ToArray(), unchanged, notFound, ambiguous));
    }

    public async Task<ListOperation<MatchItemsResult>> RemoveByTextAsync(Guid ownerId, Guid listId,
        IEnumerable<string?> texts, CancellationToken cancellationToken)
    {
        var list = await lists.GetAsync(listId, ownerId, cancellationToken);
        if (list is null) return ListOperation<MatchItemsResult>.NotFound();
        var removed = new Dictionary<Guid, ListItem>();
        var notFound = new List<string>();
        var ambiguous = new List<AmbiguousItem>();
        foreach (var text in CleanTexts(texts))
        {
            var remaining = list.Items.Where(x => !removed.ContainsKey(x.Id)).ToArray();
            // Prefer open items so "remove milk" takes the milk still to buy over last week's checked-off milk.
            var match = Match(remaining.Where(x => !x.IsDone).ToArray(), text);
            if (match.Count == 0) match = Match(remaining, text);
            if (match.Count == 1) removed[match[0].Id] = match[0];
            else if (match.Count > 1) ambiguous.Add(new AmbiguousItem(text, match.Select(x => x.Text).ToArray()));
            else notFound.Add(text);
        }

        var now = clock.GetUtcNow();
        if (removed.Count > 0)
            await lists.DeleteItemsAsync(ownerId, listId, removed.Keys.ToArray(), now, cancellationToken);
        return ListOperation<MatchItemsResult>.Ok(new MatchItemsResult(
            list with
            {
                Items = list.Items.Where(x => !removed.ContainsKey(x.Id)).ToArray(),
                UpdatedAt = removed.Count > 0 ? now : list.UpdatedAt
            },
            removed.Values.ToArray(), [], notFound, ambiguous));
    }

    private async Task<ListOperation<AddItemsResult>> AddAsync(PersonalList list, bool createdList,
        IEnumerable<string?> texts, CancellationToken cancellationToken)
    {
        var cleaned = CleanTexts(texts).ToArray();
        if (cleaned.Length > ListRules.MaxItemsPerRequest)
            return ListOperation<AddItemsResult>.Invalid("items",
                $"Add at most {ListRules.MaxItemsPerRequest} items at a time.");
        foreach (var text in cleaned)
        {
            if (ListRules.ValidateItem(text) is { } error) return ListOperation<AddItemsResult>.Invalid("items", error);
        }

        var now = clock.GetUtcNow();
        var added = new List<ListItem>();
        var reopened = new List<ListItem>();
        var alreadyOnList = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in cleaned)
        {
            var key = ListRules.ItemKey(text);
            if (!seen.Add(key)) continue;
            var existing = list.Items.Where(x => ListRules.ItemKey(x.Text) == key).ToArray();
            if (existing.Any(x => !x.IsDone))
            {
                alreadyOnList.Add(text);
            }
            else if (existing.Length > 0)
            {
                reopened.Add(existing[0] with { IsDone = false, DoneAt = null, UpdatedAt = now });
            }
            else
            {
                // Spread creation times by a tick so items keep the order they were given in.
                added.Add(new ListItem(Guid.CreateVersion7(), list.Id, list.OwnerId, text, false, null,
                    now.AddTicks(added.Count), now.AddTicks(added.Count)));
            }
        }

        if (list.Items.Count + added.Count > ListRules.MaxItemsPerList)
            return ListOperation<AddItemsResult>.Invalid("items",
                $"A list can hold at most {ListRules.MaxItemsPerList} items. Clear checked-off items first.");
        if ((added.Count > 0 || reopened.Count > 0) &&
            !await lists.SaveItemsAsync(list.OwnerId, list.Id, added, reopened, now, cancellationToken))
            return ListOperation<AddItemsResult>.NotFound();

        var reopenedById = reopened.ToDictionary(x => x.Id);
        var items = list.Items.Select(x => reopenedById.GetValueOrDefault(x.Id, x)).Concat(added).ToArray();
        var touched = added.Count > 0 || reopened.Count > 0;
        return ListOperation<AddItemsResult>.Ok(new AddItemsResult(
            list with { Items = items, UpdatedAt = touched ? now : list.UpdatedAt }, createdList, added, reopened,
            alreadyOnList));
    }

    public static PersonalList? Find(IReadOnlyList<PersonalList> all, string name)
    {
        var key = ListRules.NameKey(name);
        if (key.Length == 0) return null;
        var exact = all.FirstOrDefault(x => ListRules.NameKey(x.Name) == key);
        if (exact is not null) return exact;
        var kind = ListKinds.Guess(name);
        if (kind == ListKinds.General) return null;
        var ofKind = all.Where(x => x.Kind == kind).ToArray();
        return ofKind.Length == 1 ? ofKind[0] : null;
    }

    private static PersonalList? SameName(IEnumerable<PersonalList> all, string name, Guid? except)
    {
        var key = ListRules.NameKey(name);
        return all.FirstOrDefault(x => x.Id != except && ListRules.NameKey(x.Name) == key);
    }

    private static IEnumerable<string> CleanTexts(IEnumerable<string?> texts) =>
        texts.Select(ListRules.Clean).Where(x => x is not null).Select(x => x!);

    /// <summary>
    /// Items whose text equals <paramref name="text"/> (ignoring case and punctuation); otherwise items where one
    /// contains the other as whole words, so "milk" finds "2 liters of milk".
    /// </summary>
    internal static IReadOnlyList<ListItem> Match(IReadOnlyList<ListItem> items, string text)
    {
        var key = ListRules.ItemKey(text);
        if (key.Length == 0) return [];
        var exact = items.Where(x => ListRules.ItemKey(x.Text) == key).ToArray();
        if (exact.Length > 0) return [exact[0]];
        var padded = $" {key} ";
        return items.Where(x =>
        {
            var itemKey = ListRules.ItemKey(x.Text);
            return itemKey.Length > 0 && ($" {itemKey} ".Contains(padded, StringComparison.Ordinal) ||
                                          padded.Contains($" {itemKey} ", StringComparison.Ordinal));
        }).ToArray();
    }
}
