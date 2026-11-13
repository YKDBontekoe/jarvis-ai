using System.Collections.Concurrent;
using System.Text.Json;
using Jarvis.Application.Events;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Home;

/// <summary>Sizes a Home tile can take on the four-column grid, as the app names them.</summary>
public static class HomeTileSizes
{
    public const string Icon = "icon";
    public const string Strip = "strip";
    public const string Square = "square";
    public const string Wide = "wide";
    public const string Large = "large";

    public static readonly IReadOnlyList<string> All = [Icon, Strip, Square, Wide, Large];

    /// <summary>Grid cells each size covers, used to find the nearest size a tile supports.</summary>
    public static int Cells(string size) => size switch
    {
        Icon => 1,
        Strip => 2,
        Square => 4,
        Wide => 8,
        Large => 16,
        _ => 0,
    };

    public static string? Parse(string? value)
    {
        var clean = value?.Trim().ToLowerInvariant();
        return clean switch
        {
            null or "" => null,
            "small" or "1x1" => Icon,
            "2x1" or "medium" => Strip,
            "2x2" => Square,
            "4x2" => Wide,
            "4x4" or "big" => Large,
            _ => All.Contains(clean) ? clean : null,
        };
    }
}

/// <summary>
/// The cards above the tiles that the owner (or Jarvis, when asked) can put away. Needs you and the Right now card are
/// not here: they carry approvals and what is due, so they always show.
/// </summary>
public static class HomeCards
{
    public const string Ongoing = "ongoing";
    public const string Brief = "brief";
    public const string Noticed = "noticed";
    public const string Evening = "evening";
    public const string Learning = "learning";
    public const string Suggestions = "suggestions";

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [Ongoing] = "Ongoing work: the responsibilities Jarvis keeps moving",
        [Brief] = "Morning brief: today's summary from Jarvis",
        [Noticed] = "Jarvis noticed: routines, drift and birthdays Jarvis spotted",
        [Evening] = "Evening recap: today's recap and tomorrow, from 18:00",
        [Learning] = "Jarvis is learning: what Jarvis changed about itself",
        [Suggestions] = "Ask bar suggestions: the prompt chips under the Ask bar",
    };

    public static string? Parse(string? value)
    {
        var clean = value?.Trim().ToLowerInvariant().Replace(' ', '-');
        if (string.IsNullOrEmpty(clean)) return null;
        if (Descriptions.ContainsKey(clean)) return clean;
        return clean switch
        {
            "ongoing-work" or "proactive" => Ongoing,
            "morning-brief" or "briefing" => Brief,
            "jarvis-noticed" or "insights" => Noticed,
            "evening-recap" or "recap" => Evening,
            "jarvis-is-learning" or "learning-card" => Learning,
            "ask-suggestions" or "ask-bar-suggestions" or "chips" => Suggestions,
            _ => null,
        };
    }
}

/// <summary>One feature the app can show as a Home tile.</summary>
public sealed record HomeTileSpec(string Id, string Name, string Category, string Description, IReadOnlyList<string> Sizes)
{
    /// <summary><paramref name="size"/> if this tile supports it, else the supported size closest in area.</summary>
    public string Nearest(string size)
    {
        if (Sizes.Contains(size)) return size;
        var wanted = HomeTileSizes.Cells(size);
        return Sizes.OrderBy(s => Math.Abs(HomeTileSizes.Cells(s) - wanted)).First();
    }
}

/// <summary>
/// Every Home tile the app offers, from <c>home-tiles.json</c>, which a Flutter test keeps equal to the app's
/// <c>tile_registry.dart</c>. Connected-app tiles (<c>app:&lt;key&gt;~&lt;name&gt;</c>) are made by the app and are
/// not listed.
/// </summary>
public static class HomeTileCatalog
{
    public const string AppTilePrefix = "app:";

    /// <summary>Old tile ids that were folded into another feature, as the app maps them.</summary>
    public static readonly IReadOnlyDictionary<string, string> Merged = new Dictionary<string, string>
    {
        ["expenses"] = "finance",
        ["audit"] = "activity",
    };

    private static readonly Lazy<IReadOnlyList<HomeTileSpec>> Specs = new(Load);

    public static IReadOnlyList<HomeTileSpec> All => Specs.Value;

    public static HomeTileSpec? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var clean = id.Trim().ToLowerInvariant();
        clean = Merged.GetValueOrDefault(clean, clean);
        return All.FirstOrDefault(s => s.Id == clean);
    }

    /// <summary>A tile by id, or by its name as the owner says it ("Ongoing work", "know me").</summary>
    public static HomeTileSpec? Resolve(string? idOrName)
    {
        if (Find(idOrName) is { } byId) return byId;
        var wanted = idOrName?.Trim();
        if (string.IsNullOrEmpty(wanted)) return null;
        return All.FirstOrDefault(s => s.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
               ?? All.FirstOrDefault(s => s.Id.Equals(wanted.Replace(' ', '-'), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A connected-app tile id the app made: <c>app:&lt;key&gt;~&lt;name&gt;</c>.</summary>
    public static bool IsAppTile(string? id) =>
        id is { Length: > 6 and <= 300 } && id.StartsWith(AppTilePrefix, StringComparison.Ordinal) &&
        id.LastIndexOf('~') > AppTilePrefix.Length && !id.Any(char.IsControl);

    /// <summary>The readable name of an app tile, from the part after the last <c>~</c>.</summary>
    public static string AppTileName(string id)
    {
        var raw = id[(id.LastIndexOf('~') + 1)..];
        try { return Uri.UnescapeDataString(raw); }
        catch (UriFormatException) { return raw; }
    }

    private static IReadOnlyList<HomeTileSpec> Load()
    {
        using var stream = typeof(HomeTileCatalog).Assembly.GetManifestResourceStream("Jarvis.Application.Home.home-tiles.json")
                           ?? throw new InvalidOperationException("home-tiles.json is not embedded.");
        return JsonSerializer.Deserialize<List<HomeTileSpec>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? [];
    }
}

/// <summary>A tile on Home at a size, in the order the grid packs them.</summary>
public sealed record HomeLayoutTile(string Id, string Size);

/// <summary>What Home looked like before the latest change, so it can be undone.</summary>
public sealed record HomeLayoutSnapshot(IReadOnlyList<HomeLayoutTile> Tiles, IReadOnlyList<string> HiddenCards);

/// <summary>Who made the latest change: the owner in the app, or Jarvis in chat.</summary>
public static class HomeLayoutAuthors
{
    public const string App = "app";
    public const string Jarvis = "jarvis";
}

/// <summary>Owner settings section <see cref="Section"/>: the owner's Home, shared by every device.</summary>
public sealed record HomeLayoutState(
    IReadOnlyList<HomeLayoutTile> Tiles,
    IReadOnlyList<string> HiddenCards,
    long Revision,
    string ChangedBy,
    DateTimeOffset UpdatedAt,
    string? Note = null,
    HomeLayoutSnapshot? Previous = null)
{
    public const string Section = "home.layout";
}

/// <summary>
/// Home as the app and Jarvis read it. <see cref="Stored"/> is false while the owner never saved one: the app then
/// sends the layout it has, and Jarvis sees the default.
/// </summary>
public sealed record HomeLayoutView(
    IReadOnlyList<HomeLayoutTile> Tiles,
    IReadOnlyList<string> HiddenCards,
    long Revision,
    string ChangedBy,
    DateTimeOffset? UpdatedAt,
    string? Note,
    bool CanUndo,
    bool Stored);

public sealed record HomeLayoutResult(HomeLayoutView? View, string? Error = null, bool Changed = true)
{
    public bool Succeeded => Error is null;
    public static HomeLayoutResult Fail(string error) => new(null, error, false);
}

public interface IHomeLayoutService
{
    Task<HomeLayoutView> GetAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Saves what the owner arranged in the app. Unknown tiles are dropped and sizes fitted, never refused.</summary>
    Task<HomeLayoutResult> SaveFromAppAsync(Guid ownerId, IReadOnlyList<HomeLayoutTile> tiles,
        IReadOnlyList<string>? hiddenCards, CancellationToken cancellationToken);

    /// <summary>Jarvis replaces the whole grid with <paramref name="tiles"/>, in order.</summary>
    Task<HomeLayoutResult> ArrangeAsync(Guid ownerId, IReadOnlyList<HomeLayoutTile> tiles, string? note,
        CancellationToken cancellationToken);

    /// <summary>Jarvis adds a tile, or moves or resizes one already there. <paramref name="position"/> is 1-based.</summary>
    Task<HomeLayoutResult> PinAsync(Guid ownerId, string tile, string? size, int? position, string? note,
        CancellationToken cancellationToken);

    Task<HomeLayoutResult> RemoveAsync(Guid ownerId, string tile, string? note, CancellationToken cancellationToken);

    Task<HomeLayoutResult> SetCardVisibleAsync(Guid ownerId, string card, bool visible, string? note,
        CancellationToken cancellationToken);

    /// <summary>Puts back the layout from before the latest change, by anyone.</summary>
    Task<HomeLayoutResult> UndoAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Back to the layout a new account gets, with every card showing.</summary>
    Task<HomeLayoutResult> ResetAsync(Guid ownerId, string author, CancellationToken cancellationToken);
}

public static class HomeLayoutRules
{
    public const int MaxTiles = 60;
    public const int MaxNoteLength = 160;

    /// <summary>What a new account sees on Home; equal to <c>defaultTileLayout</c> in the app.</summary>
    public static readonly IReadOnlyList<HomeLayoutTile> DefaultTiles =
    [
        new("today", HomeTileSizes.Wide),
        new("tasks", HomeTileSizes.Square),
        new("chats", HomeTileSizes.Square),
        new("habits", HomeTileSizes.Strip),
        new("finance", HomeTileSizes.Strip),
        new("memory", HomeTileSizes.Icon),
        new("journal", HomeTileSizes.Icon),
        new("people", HomeTileSizes.Icon),
        new("voice", HomeTileSizes.Icon),
    ];

    /// <summary>
    /// Drops unknown and repeated tiles and fits sizes to what each tile supports, as the app does when it loads a
    /// layout. Connected-app tiles keep any of the four sizes they offer.
    /// </summary>
    public static List<HomeLayoutTile> Normalize(IEnumerable<HomeLayoutTile>? tiles)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<HomeLayoutTile>();
        foreach (var tile in tiles ?? [])
        {
            if (result.Count >= MaxTiles) break;
            var size = HomeTileSizes.Parse(tile?.Size) ?? HomeTileSizes.Square;
            if (HomeTileCatalog.IsAppTile(tile?.Id))
            {
                if (seen.Add(tile!.Id)) result.Add(new HomeLayoutTile(tile.Id, size == HomeTileSizes.Large ? HomeTileSizes.Wide : size));
                continue;
            }
            var spec = HomeTileCatalog.Find(tile?.Id);
            if (spec is null || !seen.Add(spec.Id)) continue;
            result.Add(new HomeLayoutTile(spec.Id, spec.Nearest(size)));
        }
        return result;
    }

    public static List<string> NormalizeCards(IEnumerable<string>? cards) =>
        (cards ?? []).Select(HomeCards.Parse).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToList();

    public static string? CleanNote(string? note)
    {
        var clean = new string((note ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) return null;
        return clean.Length <= MaxNoteLength ? clean : clean[..(MaxNoteLength - 1)].TrimEnd() + "…";
    }

    /// <summary>The readable name of a tile id, for messages and the activity feed.</summary>
    public static string NameOf(string id) =>
        HomeTileCatalog.IsAppTile(id) ? HomeTileCatalog.AppTileName(id) : HomeTileCatalog.Find(id)?.Name ?? id;
}

public sealed class HomeLayoutService(
    IOwnerSettingsStore settings,
    IJarvisEventBus? events = null,
    TimeProvider? timeProvider = null) : IHomeLayoutService
{
    public const string ChangedEventKind = "home.layout_changed";

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    // Home is one stored document that the app and Jarvis both rewrite, so changes for one owner take turns.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    public async Task<HomeLayoutView> GetAsync(Guid ownerId, CancellationToken cancellationToken) =>
        View(await settings.GetAsync<HomeLayoutState>(ownerId, HomeLayoutState.Section, cancellationToken));

    public Task<HomeLayoutResult> SaveFromAppAsync(Guid ownerId, IReadOnlyList<HomeLayoutTile> tiles,
        IReadOnlyList<string>? hiddenCards, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, HomeLayoutAuthors.App, null, current =>
        {
            var next = HomeLayoutRules.Normalize(tiles);
            return Edit.To(next, hiddenCards is null ? current.HiddenCards : HomeLayoutRules.NormalizeCards(hiddenCards));
        }, cancellationToken);

    public Task<HomeLayoutResult> ArrangeAsync(Guid ownerId, IReadOnlyList<HomeLayoutTile> tiles, string? note,
        CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, HomeLayoutAuthors.Jarvis, note, current =>
        {
            var unknown = new List<string>();
            var resolved = new List<HomeLayoutTile>();
            foreach (var tile in tiles ?? [])
            {
                var id = ResolveId(tile.Id, current.Tiles);
                if (id is null) { unknown.Add(tile.Id); continue; }
                resolved.Add(new HomeLayoutTile(id, HomeTileSizes.Parse(tile.Size) ?? SizeIn(current.Tiles, id) ?? HomeTileSizes.Square));
            }
            if (unknown.Count > 0)
                return Edit.Fail("Unknown tile(s): " + string.Join(", ", unknown) + ". Use the ids GetHomeLayout lists.");
            var next = HomeLayoutRules.Normalize(resolved);
            return next.Count == 0
                ? Edit.Fail("Home needs at least one tile. Use RemoveHomeTile to take single tiles away.")
                : Edit.To(next, current.HiddenCards);
        }, cancellationToken);

    public Task<HomeLayoutResult> PinAsync(Guid ownerId, string tile, string? size, int? position, string? note,
        CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, HomeLayoutAuthors.Jarvis, note, current =>
        {
            var id = ResolveId(tile, current.Tiles);
            if (id is null) return Edit.Fail($"There is no Home tile called \"{tile}\". GetHomeLayout lists them.");
            if (size is not null && HomeTileSizes.Parse(size) is null)
                return Edit.Fail("Size must be icon, strip, square, wide or large.");
            var tiles = current.Tiles.ToList();
            var at = tiles.FindIndex(t => t.Id == id);
            var wanted = HomeTileSizes.Parse(size) ?? (at >= 0 ? tiles[at].Size : HomeTileSizes.Square);
            if (at >= 0) tiles.RemoveAt(at);
            else if (tiles.Count >= HomeLayoutRules.MaxTiles)
                return Edit.Fail($"Home holds at most {HomeLayoutRules.MaxTiles} tiles. Remove one first.");
            var index = position is { } p ? Math.Clamp(p - 1, 0, tiles.Count) : at >= 0 ? Math.Min(at, tiles.Count) : tiles.Count;
            tiles.Insert(index, new HomeLayoutTile(id, wanted));
            return Edit.To(HomeLayoutRules.Normalize(tiles), current.HiddenCards);
        }, cancellationToken);

    public Task<HomeLayoutResult> RemoveAsync(Guid ownerId, string tile, string? note,
        CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, HomeLayoutAuthors.Jarvis, note, current =>
        {
            var id = ResolveId(tile, current.Tiles);
            var tiles = current.Tiles.ToList();
            if (id is null || tiles.RemoveAll(t => t.Id == id) == 0)
                return Edit.Fail($"\"{tile}\" is not on Home.");
            return tiles.Count == 0
                ? Edit.Fail("That is the last tile; Home needs at least one.")
                : Edit.To(tiles, current.HiddenCards);
        }, cancellationToken);

    public Task<HomeLayoutResult> SetCardVisibleAsync(Guid ownerId, string card, bool visible, string? note,
        CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, HomeLayoutAuthors.Jarvis, note, current =>
        {
            var id = HomeCards.Parse(card);
            if (id is null)
                return Edit.Fail("Cards that can be hidden: " + string.Join(", ", HomeCards.Descriptions.Keys) +
                                 ". Needs you and Right now always show.");
            var hidden = current.HiddenCards.Where(c => c != id).ToList();
            if (!visible) hidden.Add(id);
            return Edit.To(current.Tiles, HomeLayoutRules.NormalizeCards(hidden));
        }, cancellationToken);

    public async Task<HomeLayoutResult> UndoAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        using var turn = await LockAsync(ownerId, cancellationToken);
        var state = await settings.GetAsync<HomeLayoutState>(ownerId, HomeLayoutState.Section, cancellationToken);
        if (state?.Previous is not { } previous) return HomeLayoutResult.Fail("There is no change to undo.");
        // Undo is undone by undoing again, so the change that was taken back is kept as the new previous.
        var next = new HomeLayoutState(HomeLayoutRules.Normalize(previous.Tiles),
            HomeLayoutRules.NormalizeCards(previous.HiddenCards), state.Revision + 1, HomeLayoutAuthors.App,
            clock.GetUtcNow(), null, new HomeLayoutSnapshot(state.Tiles, state.HiddenCards));
        await settings.SaveAsync(ownerId, HomeLayoutState.Section, next, cancellationToken);
        return new HomeLayoutResult(View(next));
    }

    public Task<HomeLayoutResult> ResetAsync(Guid ownerId, string author, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, author == HomeLayoutAuthors.Jarvis ? HomeLayoutAuthors.Jarvis : HomeLayoutAuthors.App,
            author == HomeLayoutAuthors.Jarvis ? "Back to the standard Home" : null,
            _ => Edit.To(HomeLayoutRules.DefaultTiles.ToList(), []), cancellationToken);

    private sealed record Edit(List<HomeLayoutTile>? Tiles, IReadOnlyList<string>? HiddenCards, string? Error)
    {
        public static Edit To(IEnumerable<HomeLayoutTile> tiles, IReadOnlyList<string> hidden) => new(tiles.ToList(), hidden, null);
        public static Edit Fail(string error) => new(null, null, error);
    }

    private sealed record Current(IReadOnlyList<HomeLayoutTile> Tiles, IReadOnlyList<string> HiddenCards);

    private async Task<HomeLayoutResult> ChangeAsync(Guid ownerId, string author, string? note,
        Func<Current, Edit> change, CancellationToken cancellationToken)
    {
        HomeLayoutState next;
        HomeLayoutSnapshot before;
        using (await LockAsync(ownerId, cancellationToken))
        {
            var state = await settings.GetAsync<HomeLayoutState>(ownerId, HomeLayoutState.Section, cancellationToken);
            var view = View(state);
            before = new HomeLayoutSnapshot(view.Tiles, view.HiddenCards);
            var edit = change(new Current(view.Tiles, view.HiddenCards));
            if (edit.Error is not null) return HomeLayoutResult.Fail(edit.Error);
            var tiles = edit.Tiles!;
            var hidden = edit.HiddenCards!;
            if (state is not null && Same(tiles, hidden, before)) return new HomeLayoutResult(view, Changed: false);
            next = new HomeLayoutState(tiles, hidden, (state?.Revision ?? 0) + 1, author, clock.GetUtcNow(),
                author == HomeLayoutAuthors.Jarvis ? HomeLayoutRules.CleanNote(note) : null,
                // The first save from the app is the starting point, not a change worth undoing.
                state is null && author == HomeLayoutAuthors.App ? null : before);
            await settings.SaveAsync(ownerId, HomeLayoutState.Section, next, cancellationToken);
        }
        if (author == HomeLayoutAuthors.Jarvis)
        {
            await events.TryPublishAsync(new JarvisEvent(ownerId, ChangedEventKind,
                "Jarvis changed Home" + (next.Note is { } why ? ": " + why : ""),
                Data: new Dictionary<string, string>
                {
                    ["revision"] = next.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["tiles"] = string.Join(", ", next.Tiles.Select(t => HomeLayoutRules.NameOf(t.Id))),
                },
                Origin: EventOrigin.Agent), cancellationToken);
        }
        return new HomeLayoutResult(View(next));
    }

    private static bool Same(IReadOnlyList<HomeLayoutTile> tiles, IReadOnlyList<string> hidden, HomeLayoutSnapshot before) =>
        tiles.SequenceEqual(before.Tiles) && hidden.SequenceEqual(before.HiddenCards);

    /// <summary>A catalog tile by id or name, or a connected-app tile already on Home by its id or app name.</summary>
    private static string? ResolveId(string? idOrName, IReadOnlyList<HomeLayoutTile> current)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        var wanted = idOrName.Trim();
        if (HomeTileCatalog.IsAppTile(wanted)) return current.Any(t => t.Id == wanted) ? wanted : null;
        if (HomeTileCatalog.Resolve(wanted) is { } spec) return spec.Id;
        return current.FirstOrDefault(t => HomeTileCatalog.IsAppTile(t.Id) &&
                                           HomeTileCatalog.AppTileName(t.Id).Equals(wanted, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static string? SizeIn(IReadOnlyList<HomeLayoutTile> tiles, string id) =>
        tiles.FirstOrDefault(t => t.Id == id)?.Size;

    private static HomeLayoutView View(HomeLayoutState? state) => state is null
        ? new HomeLayoutView(HomeLayoutRules.DefaultTiles, [], 0, HomeLayoutAuthors.App, null, null, false, false)
        : new HomeLayoutView(HomeLayoutRules.Normalize(state.Tiles), HomeLayoutRules.NormalizeCards(state.HiddenCards),
            state.Revision, state.ChangedBy, state.UpdatedAt, state.Note, state.Previous is not null, true);

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
}
