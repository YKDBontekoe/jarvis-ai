using System.ComponentModel;
using System.Text;
using Jarvis.Application.Conversations;
using Jarvis.Application.Home;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents.Home;

internal sealed class HomeLayoutToolContributor(IHomeLayoutService layouts, ICurrentUser currentUser)
    : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        var tools = new HomeLayoutAgentTools(layouts, currentUser);
        yield return AIFunctionFactory.Create(tools.GetHomeLayoutAsync);
        // Home is the owner's own screen: Jarvis changes it when asked in a chat, never from a background task.
        if (context.IsBackgroundTask) yield break;
        yield return AIFunctionFactory.Create(tools.ArrangeHomeTilesAsync);
        yield return AIFunctionFactory.Create(tools.PinHomeTileAsync);
        yield return AIFunctionFactory.Create(tools.RemoveHomeTileAsync);
        yield return AIFunctionFactory.Create(tools.SetHomeCardVisibilityAsync);
        yield return AIFunctionFactory.Create(tools.UndoHomeLayoutChangeAsync);
        yield return AIFunctionFactory.Create(tools.ResetHomeLayoutAsync);
    }
}

/// <summary>One tile in <see cref="HomeLayoutAgentTools.ArrangeHomeTilesAsync"/>, in the order Home shows them.</summary>
public sealed record HomeTilePlacement(
    [property: Description("Tile id from GetHomeLayout, such as 'today' or 'finance'.")] string Tile,
    [property: Description("icon (1x1), strip (2x1), square (2x2), wide (4x2) or large (4x4). Leave empty to keep its current size.")] string? Size = null);

/// <summary>
/// Lets the owner change Home in chat ("put my habits at the top", "I never use finance", "make Home calmer"). Every
/// change can be undone from Home or with <see cref="UndoHomeLayoutChangeAsync"/>, so none needs approval.
/// </summary>
internal sealed class HomeLayoutAgentTools(IHomeLayoutService layouts, ICurrentUser currentUser)
{
    [Description("Show the user's Home screen: the tiles on it in order with their sizes, the cards above the tiles and whether they are hidden, and every tile that can be added with the sizes it supports. Call it before changing Home, and when the user asks what is on Home or what could go there.")]
    public async Task<string> GetHomeLayoutAsync(CancellationToken cancellationToken = default)
    {
        var view = await layouts.GetAsync(currentUser.OwnerId, cancellationToken);
        var text = new StringBuilder();
        text.AppendLine("Home is a 4-column grid; tiles fill it in this order, a small tile filling a gap beside a taller one.");
        text.AppendLine("Sizes: icon 1x1 shortcut, strip 2x1 one line, square 2x2 summary, wide 4x2 list, large 4x4 full view.");
        text.AppendLine(view.Stored ? "Tiles on Home now:" : "Tiles on Home now (the standard layout; the user never changed it):");
        var position = 1;
        foreach (var tile in view.Tiles)
            text.Append("  ").Append(position++).Append(". ").Append(tile.Id).Append(" \"")
                .Append(HomeLayoutRules.NameOf(tile.Id)).Append("\" ").AppendLine(tile.Size);

        text.AppendLine("Cards above the tiles (Needs you and the Right now card always show):");
        foreach (var (card, description) in HomeCards.Descriptions)
            text.Append("  - ").Append(card).Append(": ").Append(description)
                .AppendLine(view.HiddenCards.Contains(card) ? " [hidden]" : " [showing]");

        text.AppendLine("Tiles that can be added (id \"name\" [sizes]: what it shows):");
        var onHome = view.Tiles.Select(t => t.Id).ToHashSet();
        foreach (var group in HomeTileCatalog.All.Where(s => !onHome.Contains(s.Id)).GroupBy(s => s.Category))
        {
            text.Append("  ").Append(group.Key).AppendLine(":");
            foreach (var spec in group)
                text.Append("    ").Append(spec.Id).Append(" \"").Append(spec.Name).Append("\" [")
                    .Append(string.Join(", ", spec.Sizes)).Append("]: ").AppendLine(spec.Description);
        }
        if (view.ChangedBy == HomeLayoutAuthors.Jarvis && view.CanUndo)
            text.Append("Jarvis made the latest change").Append(view.Note is { } note ? $" ({note})" : "")
                .AppendLine("; UndoHomeLayoutChange puts back what was there before.");
        return text.ToString().TrimEnd();
    }

    [Description("Rearrange the whole Home grid at once: pass every tile that should be on Home, in the order to show them, with a size each. Tiles left out are taken off Home. Use it for bigger requests (\"make Home about my health\", \"put work first and keep it minimal\"); for one tile use PinHomeTile or RemoveHomeTile. Call GetHomeLayout first. The user can undo it from Home.")]
    public async Task<string> ArrangeHomeTilesAsync(
        [Description("Every tile for Home, top to bottom.")] HomeTilePlacement[] tiles,
        [Description("One short line for the user, shown on Home, saying what changed and why, in the user's language (\"Health first, money moved down\").")] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var result = await layouts.ArrangeAsync(currentUser.OwnerId,
            (tiles ?? []).Select(t => new HomeLayoutTile(t.Tile, t.Size ?? "")).ToList(), reason, cancellationToken);
        return Describe(result, "Home now shows");
    }

    [Description("Add a tile to Home, or move or resize one that is already there (\"put Habits at the top\", \"make Today bigger\", \"add my reading list\"). Call GetHomeLayout first when unsure of the id or the sizes it supports.")]
    public async Task<string> PinHomeTileAsync(
        [Description("Tile id or name from GetHomeLayout, such as 'habits'.")] string tile,
        [Description("icon, strip, square, wide or large. Leave empty to keep the current size (square for a new tile).")] string? size = null,
        [Description("Place on Home counting from 1 at the top. Leave empty to keep its place, or add it at the end.")] int? position = null,
        [Description("One short line for the user, shown on Home, saying what changed and why, in the user's language.")] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var result = await layouts.PinAsync(currentUser.OwnerId, tile, size, position, reason, cancellationToken);
        return Describe(result, "Home now shows");
    }

    [Description("Take a tile off Home when the user does not want it there (\"I never use the finance tile\"). The feature itself stays available under Everything.")]
    public async Task<string> RemoveHomeTileAsync(
        [Description("Tile id or name on Home, such as 'finance'.")] string tile,
        [Description("One short line for the user, shown on Home, saying what changed and why, in the user's language.")] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var result = await layouts.RemoveAsync(currentUser.OwnerId, tile, reason, cancellationToken);
        return Describe(result, "Removed. Home now shows");
    }

    [Description("Hide or show one of the cards above the tiles: ongoing, brief, noticed, evening, learning or suggestions (\"stop showing the morning brief on Home\", \"bring back the evening recap\"). Needs you and the Right now card always show and cannot be hidden.")]
    public async Task<string> SetHomeCardVisibilityAsync(
        [Description("The card: ongoing, brief, noticed, evening, learning or suggestions.")] string card,
        [Description("True to show the card, false to hide it.")] bool visible,
        [Description("One short line for the user, shown on Home, saying what changed and why, in the user's language.")] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var result = await layouts.SetCardVisibleAsync(currentUser.OwnerId, card, visible, reason, cancellationToken);
        return Describe(result, visible ? "The card shows again. Home tiles" : "The card is hidden. Home tiles");
    }

    [Description("Put Home back the way it was before the latest change, when the user does not like what changed.")]
    public async Task<string> UndoHomeLayoutChangeAsync(CancellationToken cancellationToken = default)
    {
        var result = await layouts.UndoAsync(currentUser.OwnerId, cancellationToken);
        return Describe(result, "Undone. Home shows");
    }

    [Description("Put Home back to the standard layout with every card showing, only when the user asks to start over.")]
    public async Task<string> ResetHomeLayoutAsync(CancellationToken cancellationToken = default)
    {
        var result = await layouts.ResetAsync(currentUser.OwnerId, HomeLayoutAuthors.Jarvis, cancellationToken);
        return Describe(result, "Home is back to the standard layout");
    }

    private static string Describe(HomeLayoutResult result, string lead)
    {
        if (!result.Succeeded) return "I could not change Home: " + result.Error;
        var view = result.View!;
        var tiles = string.Join(", ", view.Tiles.Select(t => $"{HomeLayoutRules.NameOf(t.Id)} ({t.Size})"));
        var hidden = view.HiddenCards.Count == 0 ? "" : $" Hidden cards: {string.Join(", ", view.HiddenCards)}.";
        var unchanged = result.Changed ? "" : " (nothing changed; it was already like that)";
        return $"{lead}{unchanged}: {tiles}.{hidden} The app updates Home within a minute and offers Undo there.";
    }
}
