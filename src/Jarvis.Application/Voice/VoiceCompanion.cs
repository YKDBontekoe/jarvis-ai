using System.Text;
using System.Text.Json;
using Jarvis.Application.Comparisons;
using Jarvis.Application.Library;
using Jarvis.Application.Opportunities;
using Jarvis.Application.Settings;

namespace Jarvis.Application.Voice;

public static class CompanionKinds
{
    public const string NeedsYou = "needs_you";
    public const string Comparison = "comparison";
}

public static class CompanionStates
{
    public const string Shown = "shown";
    public const string Saved = "saved";
    public const string Dismissed = "dismissed";
}

public sealed record CompanionItem(string Title, string? Detail, string? Destination);

/// <summary>
/// What the app shows beside a spoken answer. The words and the card come from the same service call, so they name
/// the same real things; the card holds the rows so the voice does not have to read them all.
/// </summary>
public sealed record CompanionCard(
    string Id,
    Guid ConversationId,
    string Kind,
    string Title,
    string Spoken,
    IReadOnlyList<CompanionItem> Items,
    string State,
    Guid? LibraryItemId,
    DateTimeOffset CreatedAt);

/// <summary>Owner settings section <see cref="Section"/>: the latest cards, newest last.</summary>
public sealed record CompanionState(IReadOnlyList<CompanionCard>? Cards = null)
{
    public const string Section = "voice.cards";
    public const int MaxCards = 30;

    public IReadOnlyList<CompanionCard> Items => Cards ?? [];
}

public sealed record CompanionChoice(string Id, string Title);

/// <summary>What "save that" resolved to: one card, a short choice, or nothing to save.</summary>
public sealed record CompanionReference(CompanionCard? Card, IReadOnlyList<CompanionChoice> Choices);

public sealed record VoiceRecapLine(string CardId, string Title, string State, string Text);

/// <summary>An end-of-session recap that keeps saved, dismissed and still-open work apart.</summary>
public sealed record VoiceRecap(
    Guid ConversationId,
    int Shown,
    int Saved,
    int Dismissed,
    int Open,
    IReadOnlyList<VoiceRecapLine> Lines,
    string Statement);

public sealed record CompanionOperation<T>(T? Value, string? Field = null, string? Message = null, bool NotFound = false)
{
    public bool Succeeded => Value is not null;
}

public interface IVoiceCompanionService
{
    /// <summary>Builds and stores the card for a tool call that has one. Null for every other tool.</summary>
    Task<CompanionCard?> CardForToolAsync(Guid ownerId, Guid conversationId, string toolName, string argumentsJson,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CompanionCard>> ListAsync(Guid ownerId, Guid conversationId, CancellationToken cancellationToken);

    /// <summary>The card "save that" means: the only one just shown, or a short choice when it is unclear.</summary>
    Task<CompanionReference> ResolveLastAsync(Guid ownerId, Guid conversationId, string? which,
        CancellationToken cancellationToken);

    Task<CompanionOperation<CompanionCard>> SaveAsync(Guid ownerId, string cardId, CancellationToken cancellationToken);
    Task<CompanionOperation<CompanionCard>> DismissAsync(Guid ownerId, string cardId, CancellationToken cancellationToken);
    Task<VoiceRecap> RecapAsync(Guid ownerId, Guid conversationId, CancellationToken cancellationToken);
}

public static class CompanionRules
{
    public const int MaxItems = 5;

    /// <summary>Cards shown within this window are the ones "that" can mean.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(3);

    public const string Statement =
        "Nothing was sent, accepted or changed outside Jarvis by voice. Only what is marked saved was kept.";

    public static CompanionCard NeedsYou(Guid conversationId, OpportunityList list, DateTimeOffset now)
    {
        var items = list.Items.Take(MaxItems)
            .Select(item => new CompanionItem(item.Title, item.WhyNow, item.Destination)).ToArray();
        var spoken = items.Length == 0
            ? "Nothing needs you right now."
            : $"{items.Length} thing{(items.Length == 1 ? "" : "s")} worth your attention; the first is {items[0].Title}.";
        return Card(conversationId, CompanionKinds.NeedsYou, "What needs you", spoken, items, now);
    }

    public static CompanionCard Compare(Guid conversationId, ComparisonView view, DateTimeOffset now)
    {
        var rows = view.Result.Rows.OrderBy(row => row.Rank).Take(MaxItems).ToArray();
        var items = rows.Select(row => new CompanionItem(row.Name,
            row.Score is { } score
                ? $"{score:0.#} out of 10{(row.Tied ? ", tied" : "")}{(row.Thin ? ", little is known" : "")}"
                : "Not enough is known to score it",
            $"comparison:{view.Record.Id}")).ToArray();
        var leader = rows.FirstOrDefault(row => row.OptionId == view.Result.LeaderId);
        var spoken = leader is null || view.Result.LeaderIsTied
            ? "There is no clear leader yet."
            : $"{leader.Name} leads, but check what is still unknown.";
        return Card(conversationId, CompanionKinds.Comparison, view.Record.Definition.Title, spoken, items, now);
    }

    private static CompanionCard Card(Guid conversationId, string kind, string title, string spoken,
        IReadOnlyList<CompanionItem> items, DateTimeOffset now) =>
        new(Guid.NewGuid().ToString("N")[..12], conversationId, kind, title, spoken, items, CompanionStates.Shown,
            null, now);

    public static string ToMarkdown(CompanionCard card)
    {
        var text = new StringBuilder("# ").AppendLine(card.Title).AppendLine();
        foreach (var item in card.Items)
            text.Append("- ").Append(item.Title).AppendLine(item.Detail is null ? "" : $": {item.Detail}");
        return text.AppendLine().AppendLine("_Saved from a voice session. Nothing was sent or changed._").ToString();
    }

    public static VoiceRecap Recap(Guid conversationId, IReadOnlyList<CompanionCard> cards)
    {
        var lines = cards.Select(card => new VoiceRecapLine(card.Id, card.Title, card.State, card.State switch
        {
            CompanionStates.Saved => "Saved to your Library.",
            CompanionStates.Dismissed => "You put it away.",
            _ => "Still open: not saved."
        })).ToArray();
        return new VoiceRecap(conversationId, cards.Count,
            cards.Count(c => c.State == CompanionStates.Saved), cards.Count(c => c.State == CompanionStates.Dismissed),
            cards.Count(c => c.State == CompanionStates.Shown), lines, Statement);
    }
}

public sealed class VoiceCompanionService(
    IOwnerSettingsStore settings,
    IOpportunityService opportunities,
    IComparisonService comparisons,
    ILibraryService library,
    TimeProvider? timeProvider = null) : IVoiceCompanionService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<CompanionCard?> CardForToolAsync(Guid ownerId, Guid conversationId, string toolName,
        string argumentsJson, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        CompanionCard? card = null;
        if (toolName == "GetOpportunities")
            card = CompanionRules.NeedsYou(conversationId, await opportunities.ListAsync(ownerId, cancellationToken), now);
        else if (toolName == "GetComparison" && ComparisonId(argumentsJson) is { } id &&
                 await comparisons.GetAsync(id, ownerId, cancellationToken) is { } view)
            card = CompanionRules.Compare(conversationId, view, now);
        if (card is null) return null;

        // The same answer asked twice in a row replaces the open card instead of stacking duplicates.
        var state = await LoadAsync(ownerId, cancellationToken);
        var kept = state.Items.Where(c => !(c.ConversationId == conversationId && c.Kind == card.Kind &&
                                            c.Title == card.Title && c.State == CompanionStates.Shown));
        await SaveStateAsync(ownerId, kept.Append(card), cancellationToken);
        return card;
    }

    public async Task<IReadOnlyList<CompanionCard>> ListAsync(Guid ownerId, Guid conversationId,
        CancellationToken cancellationToken) =>
        (await LoadAsync(ownerId, cancellationToken)).Items.Where(c => c.ConversationId == conversationId).ToArray();

    public async Task<CompanionReference> ResolveLastAsync(Guid ownerId, Guid conversationId, string? which,
        CancellationToken cancellationToken)
    {
        var cards = await ListAsync(ownerId, conversationId, cancellationToken);
        var now = _clock.GetUtcNow();
        var open = cards.Where(c => c.State == CompanionStates.Shown && now - c.CreatedAt <= CompanionRules.RecentWindow)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(which))
        {
            var match = open.Where(c => c.Title.Contains(which, StringComparison.OrdinalIgnoreCase) ||
                                        c.Kind.Contains(which, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (match.Length == 1) return new CompanionReference(match[0], []);
            if (match.Length > 1) open = match;
        }
        return open.Length switch
        {
            0 => new CompanionReference(null, []),
            1 => new CompanionReference(open[0], []),
            _ => new CompanionReference(null, open.Select(c => new CompanionChoice(c.Id, c.Title)).ToArray())
        };
    }

    public async Task<CompanionOperation<CompanionCard>> SaveAsync(Guid ownerId, string cardId,
        CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        var card = state.Items.FirstOrDefault(c => c.Id == cardId);
        if (card is null) return new CompanionOperation<CompanionCard>(null, NotFound: true);
        if (card.State == CompanionStates.Saved) return new CompanionOperation<CompanionCard>(card);
        if (card.State == CompanionStates.Dismissed)
            return new CompanionOperation<CompanionCard>(null, "card", "You put that card away, so it cannot be saved.");
        var saved = await library.AddNoteAsync(ownerId, new NoteDraft($"Voice: {card.Title}",
            CompanionRules.ToMarkdown(card), LibraryKinds.Report, null, ["voice", "private"], LibraryOrigins.App, null,
            false), cancellationToken);
        if (!saved.Succeeded)
            return new CompanionOperation<CompanionCard>(null, saved.Field ?? "card", saved.Message ?? "It could not be saved.");
        var updated = card with { State = CompanionStates.Saved, LibraryItemId = saved.Value!.Id };
        await SaveStateAsync(ownerId, state.Items.Select(c => c.Id == cardId ? updated : c), cancellationToken);
        return new CompanionOperation<CompanionCard>(updated);
    }

    public async Task<CompanionOperation<CompanionCard>> DismissAsync(Guid ownerId, string cardId,
        CancellationToken cancellationToken)
    {
        var state = await LoadAsync(ownerId, cancellationToken);
        var card = state.Items.FirstOrDefault(c => c.Id == cardId);
        if (card is null) return new CompanionOperation<CompanionCard>(null, NotFound: true);
        if (card.State != CompanionStates.Shown) return new CompanionOperation<CompanionCard>(card);
        var updated = card with { State = CompanionStates.Dismissed };
        await SaveStateAsync(ownerId, state.Items.Select(c => c.Id == cardId ? updated : c), cancellationToken);
        return new CompanionOperation<CompanionCard>(updated);
    }

    public async Task<VoiceRecap> RecapAsync(Guid ownerId, Guid conversationId, CancellationToken cancellationToken) =>
        CompanionRules.Recap(conversationId, await ListAsync(ownerId, conversationId, cancellationToken));

    private static Guid? ComparisonId(string argumentsJson)
    {
        try
        {
            using var json = JsonDocument.Parse(argumentsJson);
            return json.RootElement.TryGetProperty("comparisonId", out var id) && Guid.TryParse(id.GetString(), out var guid)
                ? guid
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<CompanionState> LoadAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<CompanionState>(ownerId, CompanionState.Section, cancellationToken) ?? new CompanionState();

    private Task SaveStateAsync(Guid ownerId, IEnumerable<CompanionCard> cards, CancellationToken cancellationToken) =>
        settings.SaveAsync(ownerId, CompanionState.Section,
            new CompanionState(cards.TakeLast(CompanionState.MaxCards).ToArray()), cancellationToken);
}
