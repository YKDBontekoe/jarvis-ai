using System.Text.Json;
using Jarvis.Application.Approvals;
using Jarvis.Application.Conversations;
using Jarvis.Application.Memory;
using Jarvis.Application.People;
using Jarvis.Application.Preferences;
using Jarvis.Application.Settings;
using Jarvis.Application.Timeline;
using Jarvis.Application.Workflows;
using Jarvis.Domain.Conversations;
using Jarvis.Domain.Knowing;

namespace Jarvis.Application.Knowing;

/// <summary>Resolves the owner's "today" in their home time zone.</summary>
internal static class OwnerDay
{
    public static async Task<TimeZoneInfo> ZoneAsync(IOwnerSettingsStore settings, IDailyBriefingRepository briefings,
        Guid ownerId, CancellationToken cancellationToken)
    {
        var zoneId = await OwnerZoneResolver.ZoneIdAsync(settings, briefings, ownerId, cancellationToken);
        LocalClock.TryFind(zoneId, out var zone);
        return zone;
    }
}

/// <summary>Train Jarvis: a short deck of things Jarvis believes about the owner, answered right, wrong or outdated.</summary>
public sealed class TrainingService(
    IKnowingRepository repository,
    IMemoryService memories,
    IOwnerSettingsStore settings,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : ITrainingService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<TrainingDeck> DeckAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var reviews = await repository.ListReviewsAsync(ownerId, cancellationToken);
        var candidates = TrainingEngine.Candidates(await memories.ListAsync(ownerId, null, cancellationToken),
            reviews, now);
        var cards = candidates.Take(KnowingRules.DeckSize)
            .Select(m => new TrainingCard(m.Id, m.Kind, Snapshot(m.Content),
                (int)Math.Round(Math.Clamp(m.Confidence, 0, 1) * 100), m.CreatedAt))
            .ToArray();
        return new TrainingDeck(cards, await StatsAsync(ownerId, reviews, candidates.Count, cancellationToken));
    }

    public async Task<TrainingAnswer?> AnswerAsync(Guid ownerId, Guid memoryId, string verdict,
        CancellationToken cancellationToken)
    {
        var clean = verdict?.Trim().ToLowerInvariant();
        if (!MemoryVerdicts.IsValid(clean))
            throw new ArgumentException("The answer must be right, wrong or outdated.", nameof(verdict));
        var memory = await memories.GetAsync(memoryId, ownerId, cancellationToken);
        if (memory is null) return null;

        switch (clean)
        {
            case MemoryVerdicts.Right:
                await memories.UpdateAsync(memory.Id, ownerId, memory.Kind, memory.Content, memory.Importance,
                    Math.Max(memory.Confidence, KnowingRules.ConfirmedConfidence), memory.ValidUntil, memory.IsPinned,
                    cancellationToken);
                break;
            case MemoryVerdicts.Wrong:
                await memories.DeleteAsync(memory.Id, ownerId, cancellationToken);
                break;
            default:
                // A pinned memory cannot be ended; the owner pinned it, so it stays until they unpin it.
                if (!memory.IsPinned) await memories.ExpireAsync(memory.Id, ownerId, cancellationToken);
                break;
        }

        var now = clock.GetUtcNow();
        await repository.AddReviewAsync(new MemoryReview(Guid.CreateVersion7(), ownerId, memory.Id, clean!,
            Snapshot(memory.Content), now), cancellationToken);
        var reviews = await repository.ListReviewsAsync(ownerId, cancellationToken);
        var left = TrainingEngine.Candidates(await memories.ListAsync(ownerId, null, cancellationToken), reviews, now)
            .Count;
        return new TrainingAnswer(clean!, await StatsAsync(ownerId, reviews, left, cancellationToken));
    }

    private async Task<TrainingStats> StatsAsync(Guid ownerId, IReadOnlyList<MemoryReview> reviews, int left,
        CancellationToken cancellationToken)
    {
        var zone = await OwnerDay.ZoneAsync(settings, briefings, ownerId, cancellationToken);
        return TrainingEngine.Stats(reviews, left, TimelineRules.LocalDate(clock.GetUtcNow(), zone),
            at => TimelineRules.LocalDate(at, zone));
    }

    private static string Snapshot(string content)
    {
        var flat = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= KnowingRules.SnapshotLength ? flat : flat[..(KnowingRules.SnapshotLength - 1)] + "…";
    }
}

/// <summary>The map of the unknown: how much of the basics Jarvis knows, and one question a day to clear fog.</summary>
public sealed class KnowledgeMapService(
    IKnowledgeGraphRepository graph,
    IOwnerSettingsStore settings,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : IKnowledgeMapService
{
    public const int MaxAnswerLength = 200;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<KnowledgeMap> MapAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var state = await StateAsync(ownerId, cancellationToken);
        return await BuildAsync(ownerId, state, cancellationToken);
    }

    public async Task<GapAnswerResult?> AnswerAsync(Guid ownerId, Guid entityId, string predicate, string answer,
        CancellationToken cancellationToken)
    {
        var text = string.Join(' ', (answer ?? string.Empty).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0) throw new ArgumentException("Type an answer.", nameof(answer));
        if (text.Length > MaxAnswerLength)
            throw new ArgumentException($"Keep the answer under {MaxAnswerLength} characters.", nameof(answer));

        var details = await graph.GetEntityAsync(ownerId, entityId, cancellationToken);
        if (details is null || !GapEngine.IsKnownPredicate(details.Entity.Type, predicate)) return null;

        var (objectType, isEntity) = GapEngine.AnswerShape(predicate);
        var saved = await graph.MergeAsync(ownerId,
        [
            new GraphFact(details.Entity.Name, details.Entity.Type, predicate, text, objectType, isEntity,
                Exclusive: true, clock.GetUtcNow(), Confidence: 1f)
        ], null, cancellationToken) > 0;

        var state = await StateAsync(ownerId, cancellationToken);
        var today = await TodayAsync(ownerId, cancellationToken);
        state = state.LastAnsweredOn == today
            ? state with { AnsweredOnLastDay = state.AnsweredOnLastDay + 1 }
            : state with { LastAnsweredOn = today, AnsweredOnLastDay = 1 };
        await settings.SaveAsync(ownerId, SettingsSections.Knowing, state, cancellationToken);
        return new GapAnswerResult(saved, await BuildAsync(ownerId, state, cancellationToken));
    }

    public async Task<KnowledgeMap> SkipAsync(Guid ownerId, Guid entityId, string predicate,
        CancellationToken cancellationToken)
    {
        var state = await StateAsync(ownerId, cancellationToken);
        var skipped = (state.SkippedGaps ?? []).ToList();
        var key = GapEngine.Key(entityId, predicate);
        if (!skipped.Contains(key, StringComparer.Ordinal))
        {
            skipped.Add(key);
            // Keep the list bounded; the oldest skips are the least likely to matter.
            if (skipped.Count > 500) skipped.RemoveRange(0, skipped.Count - 500);
            state = state with { SkippedGaps = skipped };
            await settings.SaveAsync(ownerId, SettingsSections.Knowing, state, cancellationToken);
        }

        return await BuildAsync(ownerId, state, cancellationToken);
    }

    private async Task<KnowledgeMap> BuildAsync(Guid ownerId, KnowingState state, CancellationToken cancellationToken)
    {
        var overview = await graph.GetOverviewAsync(ownerId, KnowledgeGraphLimits.MaxOverviewEntities,
            cancellationToken);
        var (gaps, entities) = GapEngine.Find(overview,
            (state.SkippedGaps ?? []).ToHashSet(StringComparer.Ordinal));
        var known = entities.Sum(e => e.Known);
        var expected = entities.Sum(e => e.Expected);
        var today = await TodayAsync(ownerId, cancellationToken);
        var answeredToday = state.LastAnsweredOn == today ? state.AnsweredOnLastDay : 0;
        // The daily question rotates through the gaps by date, so a skipped one does not block the rest.
        var question = gaps.Count == 0 ? null : gaps[today.DayNumber % gaps.Count];
        return new KnowledgeMap(expected == 0 ? 0 : (int)Math.Round(100.0 * (expected - known) / expected),
            known, expected, question, entities, answeredToday);
    }

    private async Task<KnowingState> StateAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await settings.GetAsync<KnowingState>(ownerId, SettingsSections.Knowing, cancellationToken) ??
        new KnowingState();

    private async Task<DateOnly> TodayAsync(Guid ownerId, CancellationToken cancellationToken) =>
        TimelineRules.LocalDate(clock.GetUtcNow(),
            await OwnerDay.ZoneAsync(settings, briefings, ownerId, cancellationToken));
}

/// <summary>The trust ladder, computed from the owner's approval decisions over the last year.</summary>
public sealed class TrustLadderService(
    IApprovalHistory history,
    IStandingApprovalService standing,
    TimeProvider? timeProvider = null) : ITrustLadderService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<TrustRung>> LadderAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var decided = await history.ListDecidedAsync(ownerId, now.AddDays(-365), 5_000, cancellationToken);
        return TrustEngine.Climb(decided, await standing.ListAsync(ownerId, cancellationToken), now);
    }

    public async Task<TrustHandOverResult> HandOverAsync(Guid ownerId, string category,
        CancellationToken cancellationToken)
    {
        var rung = (await LadderAsync(ownerId, cancellationToken))
            .FirstOrDefault(r => r.Category == category);
        if (rung is null) return TrustHandOverResult.NotFound;
        if (!TrustEngine.MayHandOver(category)) return TrustHandOverResult.NotAllowed;
        if (!rung.CanHandOver) return TrustHandOverResult.NotEarned;
        var result = await standing.GrantAsync(ownerId, new ApprovalCategory(rung.Category, rung.Label, true),
            cancellationToken, new GrantTerms(TrustEngine.HandOverFor));
        return result == StandingApprovalGrantResult.Granted
            ? TrustHandOverResult.HandedOver
            : TrustHandOverResult.NotAllowed;
    }

    public Task<bool> TakeBackAsync(Guid ownerId, string category, CancellationToken cancellationToken) =>
        standing.RevokeAsync(ownerId, category, cancellationToken);
}

/// <summary>Starts special chats: a normal conversation with a mode the agent reads on every turn.</summary>
public sealed class ChatModeService(
    IKnowingRepository repository,
    IConversationStore conversations,
    IPeopleService people,
    IOwnerSettingsStore settings,
    IDailyBriefingRepository briefings,
    TimeProvider? timeProvider = null) : IChatModeService
{
    public const int MaxTextLength = 500;
    public const int MaxAdvisers = 5;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public static readonly IReadOnlyList<string> DefaultAdvisers = ["The sceptic", "The stoic", "Your accountant",
        "Your future self"];

    public async Task<ChatModeOperation> StartAsync(Guid ownerId, ChatModeRequest request,
        CancellationToken cancellationToken)
    {
        var kind = request.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ChatModeKinds.All.Contains(kind)) return ChatModeOperation.Invalid("kind", "Choose a kind of chat.");
        var scenario = Clean(request.Scenario);
        var goal = Clean(request.Goal);
        var question = Clean(request.Question);
        if (TooLong(scenario) || TooLong(goal) || TooLong(question))
            return ChatModeOperation.Invalid("text", $"Keep it under {MaxTextLength} characters.");

        string title;
        string opening;
        object payload;
        switch (kind)
        {
            case ChatModeKinds.Stranger:
                title = "The stranger";
                opening = "Hello. I don’t think we’ve met. I know nothing about you, so let’s start from the " +
                          "beginning: what do you do with most of your days?";
                payload = new { };
                break;
            case ChatModeKinds.Rehearse:
            {
                if (request.PersonId is not { } personId)
                    return ChatModeOperation.Invalid("personId", "Choose who you want to rehearse with.");
                var person = await people.GetAsync(personId, ownerId, cancellationToken);
                if (person is null) return ChatModeOperation.Invalid("personId", "That person is not in People.");
                if (scenario is null)
                    return ChatModeOperation.Invalid("scenario", "Say what the conversation is about.");
                title = $"Rehearsal with {person.Name}";
                opening = $"Rehearsal with {person.Name}. I’ll answer the way {person.Name} tends to write. Start " +
                          "whenever you’re ready, and say “debrief” when you want my notes.";
                payload = new { personId, personName = person.Name, scenario, goal };
                break;
            }
            case ChatModeKinds.PastSelf:
            {
                var zone = await OwnerDay.ZoneAsync(settings, briefings, ownerId, cancellationToken);
                var today = TimelineRules.LocalDate(clock.GetUtcNow(), zone);
                if (request.AsOf is not { } asOf || asOf >= today)
                    return ChatModeOperation.Invalid("asOf", "Choose a day in the past.");
                if (asOf < today.AddYears(-10))
                    return ChatModeOperation.Invalid("asOf", "Choose a day in the last ten years.");
                title = $"You, on {asOf:d MMM yyyy}";
                opening = $"It’s {asOf:dddd d MMMM yyyy}. I only know what you had told Jarvis by then. " +
                          "What do you want to ask me?";
                payload = new { asOf = asOf.ToString("yyyy-MM-dd") };
                break;
            }
            default:
            {
                if (question is null) return ChatModeOperation.Invalid("question", "What should the council weigh?");
                var advisers = (request.Advisers ?? DefaultAdvisers)
                    .Select(Clean).OfType<string>().Where(a => a.Length <= 60)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxAdvisers).ToArray();
                if (advisers.Length < 2)
                    return ChatModeOperation.Invalid("advisers", "Choose at least two advisers.");
                title = "The council";
                opening = $"The council is seated: {string.Join(", ", advisers)}. Say “Begin” and they will " +
                          "argue it out. The decision stays yours.";
                payload = new { question, advisers };
                break;
            }
        }

        var conversation = await conversations.CreateAsync(ownerId, title, cancellationToken);
        await repository.AddChatModeAsync(new ChatMode(conversation.Id, ownerId, kind,
            JsonSerializer.Serialize(payload), clock.GetUtcNow()), cancellationToken);
        await conversations.AddMessageAsync(new Message(conversation.Id, "assistant", opening), cancellationToken);
        return new ChatModeOperation(new ChatModeStarted(conversation.Id, kind, title, opening));
    }

    public Task<ChatMode?> GetAsync(Guid conversationId, Guid ownerId, CancellationToken cancellationToken) =>
        repository.GetChatModeAsync(conversationId, ownerId, cancellationToken);

    public async Task<IReadOnlyList<ChatModeView>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var modes = await repository.ListChatModesAsync(ownerId, cancellationToken);
        var titles = (await conversations.ListAsync(ownerId, cancellationToken)).ToDictionary(c => c.Id, c => c.Title);
        return modes.Where(m => titles.ContainsKey(m.ConversationId))
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new ChatModeView(m.ConversationId, m.Kind, titles[m.ConversationId] ?? m.Kind, m.CreatedAt))
            .ToArray();
    }

    private static string? Clean(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static bool TooLong(string? text) => text is { Length: > MaxTextLength };
}
