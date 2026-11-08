using Jarvis.Application.Settings;

namespace Jarvis.Application.Lists;

public sealed record ListItem(Guid Id, string Text, bool Done, DateTimeOffset AddedAt);

public sealed record ListRecord(Guid Id, string Name, IReadOnlyList<ListItem> Items, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Owner settings section <see cref="Section"/>: every plain list the owner keeps (shopping, packing, ...).</summary>
public sealed record ListsState(IReadOnlyList<ListRecord> Lists)
{
    public const string Section = "lists";
}

public static class ListRules
{
    public const int MaxLists = 50;
    public const int MaxItemsPerList = 200;
    public const int MaxNameLength = 60;
    public const int MaxItemLength = 200;

    public static string? Clean(string? text, int max)
    {
        var clean = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) return null;
        return clean.Length <= max ? clean : clean[..max].TrimEnd();
    }
}

public enum ListFailure
{
    None,
    NotFound,
    Invalid,
    Ambiguous,
}

public sealed record ListOperation<T>(T? Value, ListFailure Failure = ListFailure.None, string? Message = null)
{
    public bool Succeeded => Failure == ListFailure.None;
    public static ListOperation<T> Ok(T value) => new(value);
    public static ListOperation<T> NotFound(string message) => new(default, ListFailure.NotFound, message);
    public static ListOperation<T> Invalid(string message) => new(default, ListFailure.Invalid, message);
    public static ListOperation<T> Ambiguous(string message) => new(default, ListFailure.Ambiguous, message);
}

public interface IListService
{
    Task<IReadOnlyList<ListRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Adds items to the list with this name, creating the list when there is none. Duplicates are skipped.</summary>
    Task<ListOperation<ListRecord>> AddItemsAsync(Guid ownerId, string listName, IReadOnlyList<string> items,
        CancellationToken cancellationToken);

    /// <summary>Ticks or unticks items by text (exact, else a single partial match per text).</summary>
    Task<ListOperation<ListRecord>> SetDoneAsync(Guid ownerId, string listName, IReadOnlyList<string> items, bool done,
        CancellationToken cancellationToken);

    /// <summary>Takes items off a list whether or not they are ticked (exact text, else a single partial match).</summary>
    Task<ListOperation<ListRecord>> RemoveItemsAsync(Guid ownerId, string listName, IReadOnlyList<string> items,
        CancellationToken cancellationToken);

    /// <summary>Gives a list a new name. Refused when another list already has that name.</summary>
    Task<ListOperation<ListRecord>> RenameAsync(Guid ownerId, string listName, string newName,
        CancellationToken cancellationToken);

    /// <summary>Removes the ticked items from a list.</summary>
    Task<ListOperation<ListRecord>> ClearDoneAsync(Guid ownerId, string listName, CancellationToken cancellationToken);

    Task<bool> DeleteListAsync(Guid ownerId, Guid listId, CancellationToken cancellationToken);
}

public sealed class ListService(IOwnerSettingsStore settings, TimeProvider? timeProvider = null) : IListService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    // The lists are one stored document, changed by reading and writing it back. The app and the assistant can edit
    // at the same moment, so changes for one owner take turns instead of one overwriting the other.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    private static async Task<IDisposable> LockAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    public async Task<IReadOnlyList<ListRecord>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        (await LoadAsync(ownerId, cancellationToken)).OrderByDescending(l => l.UpdatedAt).ToList();

    public async Task<ListOperation<ListRecord>> AddItemsAsync(Guid ownerId, string listName,
        IReadOnlyList<string> items, CancellationToken cancellationToken)
    {
        var name = ListRules.Clean(listName, ListRules.MaxNameLength);
        if (name is null) return ListOperation<ListRecord>.Invalid("A list needs a name.");
        var texts = (items ?? []).Select(t => ListRules.Clean(t, ListRules.MaxItemLength)).OfType<string>().ToList();
        if (texts.Count == 0) return ListOperation<ListRecord>.Invalid("There is nothing to add.");
        using var turn = await LockAsync(ownerId, cancellationToken);

        var all = (await LoadAsync(ownerId, cancellationToken)).ToList();
        var now = clock.GetUtcNow();
        var index = all.FindIndex(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            if (all.Count >= ListRules.MaxLists)
                return ListOperation<ListRecord>.Invalid($"At most {ListRules.MaxLists} lists. Delete one first.");
            all.Add(new ListRecord(Guid.CreateVersion7(), name, [], now, now));
            index = all.Count - 1;
        }
        var list = all[index];
        var current = list.Items.ToList();
        foreach (var text in texts)
        {
            if (current.Any(i => i.Text.Equals(text, StringComparison.OrdinalIgnoreCase) && !i.Done)) continue;
            if (current.Count >= ListRules.MaxItemsPerList)
                return ListOperation<ListRecord>.Invalid($"A list holds at most {ListRules.MaxItemsPerList} items.");
            current.Add(new ListItem(Guid.CreateVersion7(), text, false, now));
        }
        all[index] = list with { Items = current, UpdatedAt = now };
        await SaveAsync(ownerId, all, cancellationToken);
        return ListOperation<ListRecord>.Ok(all[index]);
    }

    public async Task<ListOperation<ListRecord>> SetDoneAsync(Guid ownerId, string listName,
        IReadOnlyList<string> items, bool done, CancellationToken cancellationToken)
    {
        using var turn = await LockAsync(ownerId, cancellationToken);
        var all = (await LoadAsync(ownerId, cancellationToken)).ToList();
        var found = Find(all, listName);
        if (found.Failure != ListFailure.None) return new ListOperation<ListRecord>(null, found.Failure, found.Message);
        var index = found.Index;
        var current = all[index].Items.ToList();
        var misses = new List<string>();
        foreach (var raw in items ?? [])
        {
            var text = ListRules.Clean(raw, ListRules.MaxItemLength);
            if (text is null) continue;
            var matches = current.Where(i => i.Done != done && i.Text.Equals(text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
                matches = current.Where(i => i.Done != done && i.Text.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1) { misses.Add(text); continue; }
            current[current.FindIndex(i => i.Id == matches[0].Id)] = matches[0] with { Done = done };
        }
        if (misses.Count > 0 && misses.Count == (items ?? []).Count)
            return ListOperation<ListRecord>.NotFound("No single item matches: " + string.Join(", ", misses));
        all[index] = all[index] with { Items = current, UpdatedAt = clock.GetUtcNow() };
        await SaveAsync(ownerId, all, cancellationToken);
        return ListOperation<ListRecord>.Ok(all[index]);
    }

    public async Task<ListOperation<ListRecord>> RemoveItemsAsync(Guid ownerId, string listName,
        IReadOnlyList<string> items, CancellationToken cancellationToken)
    {
        using var turn = await LockAsync(ownerId, cancellationToken);
        var all = (await LoadAsync(ownerId, cancellationToken)).ToList();
        var found = Find(all, listName);
        if (found.Failure != ListFailure.None) return new ListOperation<ListRecord>(null, found.Failure, found.Message);
        var index = found.Index;
        var current = all[index].Items.ToList();
        var misses = new List<string>();
        foreach (var raw in items ?? [])
        {
            var text = ListRules.Clean(raw, ListRules.MaxItemLength);
            if (text is null) continue;
            var matches = current.Where(i => i.Text.Equals(text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
                matches = current.Where(i => i.Text.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1) { misses.Add(text); continue; }
            current.RemoveAll(i => i.Id == matches[0].Id);
        }
        if (misses.Count > 0 && misses.Count == (items ?? []).Count)
            return ListOperation<ListRecord>.NotFound("No single item matches: " + string.Join(", ", misses));
        all[index] = all[index] with { Items = current, UpdatedAt = clock.GetUtcNow() };
        await SaveAsync(ownerId, all, cancellationToken);
        return ListOperation<ListRecord>.Ok(all[index]);
    }

    public async Task<ListOperation<ListRecord>> RenameAsync(Guid ownerId, string listName, string newName,
        CancellationToken cancellationToken)
    {
        var name = ListRules.Clean(newName, ListRules.MaxNameLength);
        if (name is null) return ListOperation<ListRecord>.Invalid("A list needs a name.");
        using var turn = await LockAsync(ownerId, cancellationToken);
        var all = (await LoadAsync(ownerId, cancellationToken)).ToList();
        var found = Find(all, listName);
        if (found.Failure != ListFailure.None) return new ListOperation<ListRecord>(null, found.Failure, found.Message);
        if (all.Where((l, i) => i != found.Index).Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return ListOperation<ListRecord>.Invalid("Another list already has that name.");
        all[found.Index] = all[found.Index] with { Name = name, UpdatedAt = clock.GetUtcNow() };
        await SaveAsync(ownerId, all, cancellationToken);
        return ListOperation<ListRecord>.Ok(all[found.Index]);
    }

    public async Task<ListOperation<ListRecord>> ClearDoneAsync(Guid ownerId, string listName,
        CancellationToken cancellationToken)
    {
        using var turn = await LockAsync(ownerId, cancellationToken);
        var all = (await LoadAsync(ownerId, cancellationToken)).ToList();
        var found = Find(all, listName);
        if (found.Failure != ListFailure.None) return new ListOperation<ListRecord>(null, found.Failure, found.Message);
        all[found.Index] = all[found.Index] with
        {
            Items = all[found.Index].Items.Where(i => !i.Done).ToList(), UpdatedAt = clock.GetUtcNow()
        };
        await SaveAsync(ownerId, all, cancellationToken);
        return ListOperation<ListRecord>.Ok(all[found.Index]);
    }

    public async Task<bool> DeleteListAsync(Guid ownerId, Guid listId, CancellationToken cancellationToken)
    {
        using var turn = await LockAsync(ownerId, cancellationToken);
        var all = (await LoadAsync(ownerId, cancellationToken)).ToList();
        if (all.RemoveAll(l => l.Id == listId) == 0) return false;
        await SaveAsync(ownerId, all, cancellationToken);
        return true;
    }

    private static (ListFailure Failure, string? Message, int Index) Find(List<ListRecord> all, string? name)
    {
        var wanted = ListRules.Clean(name, ListRules.MaxNameLength);
        if (wanted is null) return (ListFailure.Invalid, "Which list?", -1);
        var exact = all.FindIndex(l => l.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0) return (ListFailure.None, null, exact);
        var partial = all.Select((l, i) => (l, i)).Where(x => x.l.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        return partial.Count switch
        {
            1 => (ListFailure.None, null, partial[0].i),
            0 => (ListFailure.NotFound, "There is no list with that name.", -1),
            _ => (ListFailure.Ambiguous, "More than one list matches: " + string.Join(", ", partial.Select(x => x.l.Name)), -1),
        };
    }

    private async Task<IReadOnlyList<ListRecord>> LoadAsync(Guid ownerId, CancellationToken ct) =>
        (await settings.GetAsync<ListsState>(ownerId, ListsState.Section, ct))?.Lists ?? [];

    private Task SaveAsync(Guid ownerId, List<ListRecord> all, CancellationToken ct) =>
        settings.SaveAsync(ownerId, ListsState.Section, new ListsState(all), ct);
}
